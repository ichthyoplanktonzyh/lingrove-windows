using System.IO;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Models;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Support.Runtime;
using Cotabby.Inference.LanguageLearning.Local;

namespace Cotabby.App.Learning;

/// <summary>
/// AppHost owns this local explanation role once, with a runtime distinct from translation and
/// autocomplete. It adapts Mac ModelCollection/ModelController: committed settings to service
/// identity, global eligibility to native retirement, and on-demand idle release after 60 seconds.
/// No editor/UI/HTTP state lives here. Per-request facades borrow the runtime; shutdown drains the
/// service and those facades before disposing their native owner.
/// </summary>
internal sealed class LearningExplanationController : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ILearningLocalRuntime _runtime;
    private readonly Func<LearningLocalModelConfiguration, ILearningExplanationEngine> _engineFactory;
    private readonly string _modelDirectory;
    private readonly Func<bool> _batterySaverOn;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idleDelay;
    private readonly HashSet<Task> _releases = [];
    private readonly HashSet<Task> _instructionWork = [];
    private readonly Dictionary<CancellationTokenSource, Task> _instructionLeases = [];
    private readonly HashSet<Task> _leaseCleanup = [];
    private CancellationTokenSource? _currentInstruction;
    private LearningModelSnapshot _profile;
    private bool _enabled;
    private bool _modelAdmitted;
    private bool _pauseOnBatterySaver;
    private PauseState? _pause;
    private LocalRuntimeResidency _residency = LocalRuntimeResidency.Released;
    private bool _nativeMayBeResident;
    private int _activeFacades;
    private ITimer? _idleTimer;
    private long _idleRevision;
    private Task? _disposal;

    internal LearningExplanationController(ILearningLocalRuntime runtime, AppSettings settings, string modelDirectory,
        Func<bool>? batterySaverOn = null, TimeProvider? clock = null, TimeSpan? idleDelay = null,
        Func<LearningLocalModelConfiguration, ILearningExplanationEngine>? engineFactory = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _modelDirectory = Path.GetFullPath(modelDirectory);
        _batterySaverOn = batterySaverOn ?? (() => false);
        _clock = clock ?? TimeProvider.System;
        _idleDelay = idleDelay ?? TimeSpan.FromSeconds(60);
        if (_idleDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleDelay));
        _engineFactory = engineFactory ?? (configuration => new LearningLocalExplanationEngine(configuration, _runtime));
        _profile = settings.LearningExplanationSnapshot();
        Service = new(_profile, settings.ResolvedLearningExplanationLanguage, CreateEngine, _clock);
        ApplySettings(settings);
    }

    internal LearningExplanationService Service { get; }
    internal LocalRuntimeResidency Residency { get { lock (_gate) return _residency; } }

    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var next = settings.LearningExplanationSnapshot();
        bool admitted = settings.IsLocalModelCandidate(next.LocalModel, LocalModelTask.Explanation)
            && LocalModelTaskPolicy.PermitsTemplate(next.Template, LocalModelTask.Explanation);
        lock (_gate)
        {
            if (_disposal is not null) return;
            bool allocationChanged = !_profile.UsesSameRuntime(next) || _modelAdmitted != admitted;
            if (_profile != next) StopIdleLocked();
            if (_profile != next) RetireInstructionLocked();
            _profile = next;
            _modelAdmitted = admitted;
            _enabled = settings.Enabled;
            _pause = settings.Pause;
            _pauseOnBatterySaver = settings.PauseOnBatterySaver;
            if (allocationChanged)
            {
                // Record retirement under the same admission gate as facade creation. The
                // runtime changes its direct epoch now and drains native calls on its worker.
                if (_nativeMayBeResident) ReleaseLocked();
            }
            ReconcileLocked();
        }
        try { Service.ApplyConfiguration(next, settings.ResolvedLearningExplanationLanguage); }
        catch (ObjectDisposedException)
        {
            lock (_gate) if (_disposal is null) throw;
        }
    }

    internal void PowerStatusChanged()
    {
        lock (_gate) if (_disposal is null) ReconcileLocked();
    }

    private bool AllowedLocked() => _enabled && _pause?.IsActive(_clock.GetUtcNow()) != true
        && !(_pauseOnBatterySaver && _batterySaverOn());

    private void ReconcileLocked()
    {
        bool allowed = AllowedLocked() && _modelAdmitted;
        _residency = LocalRuntimeResidencyPolicy.Residency(usesLocalModel: true,
            enabled: allowed, paused: !allowed, keepWarm: false);
        if (!allowed)
        {
            RetireInstructionLocked();
            StopIdleLocked(); Service.CancelPending();
            if (_nativeMayBeResident) ReleaseLocked();
        }
        else if (_activeFacades == 0) ScheduleIdleLocked();
    }

    private ILearningExplanationEngine CreateEngine(LearningModelSnapshot profile)
    {
        lock (_gate)
        {
            if (_disposal is not null || !AllowedLocked() || _profile != profile) throw new OperationCanceledException();
            if (!_modelAdmitted) throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            string name = profile.LocalModel;
            // This setting denotes an installed filename, never an arbitrary path. Explicitly
            // reject both separators even in cross-platform tests; Windows owns the actual IO.
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(Path.GetExtension(name), ".gguf", StringComparison.OrdinalIgnoreCase))
                throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            var configuration = new LearningLocalModelConfiguration(Path.Combine(_modelDirectory, name), profile.Template,
                profile.ContextWindowTokens, profile.SamplingOverrides, requiresGeneralInstructions: true);
            var inner = _engineFactory(configuration);
            StopIdleLocked(); _activeFacades++; _nativeMayBeResident = true;
            return new OwnedFacade(this, inner, profile, ReserveInstructionLocked());
        }
    }

    // Reserve before native admission, so newer instruction requests cancel an older operation
    // even while it is queued or producing no deltas. Cancellation callbacks drain off-thread.
    private CancellationTokenSource ReserveInstructionLocked()
    {
        RetireInstructionLocked();
        var lease = new CancellationTokenSource();
        _instructionLeases.Add(lease, Task.CompletedTask); _currentInstruction = lease;
        return lease;
    }
    private void RetireInstructionLocked()
    {
        var old = _currentInstruction; _currentInstruction = null;
        if (old is null) return;
        try { _instructionLeases[old] = old.CancelAsync(); } catch { }
    }
    private void RetireInstruction(CancellationTokenSource lease)
    {
        lock (_gate)
        {
            if (!_instructionLeases.ContainsKey(lease) || lease.IsCancellationRequested) return;
            if (ReferenceEquals(_currentInstruction, lease)) _currentInstruction = null;
            try { _instructionLeases[lease] = lease.CancelAsync(); } catch { }
        }
    }

    private void ReleaseInstruction(CancellationTokenSource lease)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_currentInstruction, lease)) _currentInstruction = null;
            if (!_instructionLeases.Remove(lease, out var cancellation)) return;
            var cleanup = Task.Run(async () => { try { await cancellation.ConfigureAwait(false); } catch { } lease.Dispose(); });
            _leaseCleanup.Add(cleanup);
            _ = cleanup.ContinueWith(t => { lock (_gate) _leaseCleanup.Remove(t); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal Task<ExpressionAssistResult> GenerateExpressionAsync(ExpressionAssistRequest request, Func<string, string> lowercase, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lowercase);
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_disposal is not null || !AllowedLocked() || !_modelAdmitted || _profile.Backend != LearningModelBackend.Local)
                throw new OperationCanceledException();
            var profile = _profile;
            var lease = ReserveInstructionLocked();
            StopIdleLocked(); _activeFacades++; _nativeMayBeResident = true;
            var task = Task.Run(() => GenerateExpressionCoreAsync(request, lowercase, profile, lease, token));
            _instructionWork.Add(task);
            _ = task.ContinueWith(t => { lock (_gate) _instructionWork.Remove(t); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<ExpressionAssistResult> GenerateExpressionCoreAsync(ExpressionAssistRequest request, Func<string, string> lowercase,
        LearningModelSnapshot profile, CancellationTokenSource lease, CancellationToken caller)
    {
        using var registration = caller.Register(() => RetireInstruction(lease));
        try
        {
            caller.ThrowIfCancellationRequested(); CheckCurrent(profile, lease.Token);
            string name = profile.LocalModel;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(Path.GetExtension(name), ".gguf", StringComparison.OrdinalIgnoreCase))
                throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            var config = new LearningLocalModelConfiguration(Path.Combine(_modelDirectory, name), profile.Template,
                profile.ContextWindowTokens, profile.SamplingOverrides, requiresGeneralInstructions: true);
            int output = Math.Min(profile.MaximumOutputTokens ?? 768, 1536);
            string prompt = ExpressionAssistPrompt.Render(request, profile.ContextWindowTokens, output);
            await using var engine = new LearningLocalInstructionEngine(config, _runtime);
            var raw = await engine.GenerateAsync(prompt, output, 12_000, lease.Token).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested(); CheckCurrent(profile, lease.Token);
            return ExpressionAssistPrompt.Parse(raw, request, lowercase);
        }
        finally { ReleaseInstruction(lease); FacadeDrained(); }
    }

    private void CheckCurrent(LearningModelSnapshot profile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
            if (_disposal is not null || !AllowedLocked() || !_modelAdmitted || _profile != profile) throw new OperationCanceledException();
    }

    private void FacadeDrained()
    {
        lock (_gate)
        {
            _activeFacades--;
            if (_disposal is null && _activeFacades == 0) ReconcileLocked();
        }
    }

    private void ScheduleIdleLocked()
    {
        if (!_nativeMayBeResident || _activeFacades != 0 || _idleTimer is not null) return;
        long revision = ++_idleRevision;
        _idleTimer = _clock.CreateTimer(_ => IdleElapsed(revision), null, _idleDelay, Timeout.InfiniteTimeSpan);
    }

    private void IdleElapsed(long revision)
    {
        lock (_gate)
        {
            // Timer disposal does not join an already queued callback. Its identity still has
            // to match, or an old timeout could unload a newly admitted explanation.
            if (_disposal is not null || revision != _idleRevision || _activeFacades != 0) return;
            StopIdleLocked();
            RetireInstructionLocked();
            if (_nativeMayBeResident) ReleaseLocked();
        }
    }

    private void StopIdleLocked() { ++_idleRevision; _idleTimer?.Dispose(); _idleTimer = null; }
    private void ReleaseLocked()
    {
        _nativeMayBeResident = false;
        Task release;
        try { release = _runtime.UnloadAsync(); }
        catch { return; } // Native/foreign failures may contain paths; never forward their text.
        _releases.Add(release);
        _ = ObserveReleaseAsync(release);
    }
    private async Task ObserveReleaseAsync(Task release)
    {
        try { await release.ConfigureAwait(false); } catch { }
        finally { lock (_gate) _releases.Remove(release); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            StopIdleLocked();
            // Close service admission now, including cache and saved lookups. Deferring this
            // call to our cleanup worker would leave a window for new non-native requests.
            var serviceDrain = Service.DisposeAsync().AsTask();
            return new(_disposal = Task.Run(() => DisposeCoreAsync(serviceDrain, _instructionWork.ToArray())));
        }
    }
    private async Task DisposeCoreAsync(Task serviceDrain, Task[] instructions)
    {
        try
        {
            await serviceDrain.ConfigureAwait(false);
            try { await Task.WhenAll(instructions).ConfigureAwait(false); } catch { }
            Task[] cleanup; lock (_gate) cleanup = _leaseCleanup.ToArray();
            try { await Task.WhenAll(cleanup).ConfigureAwait(false); } catch { }
            Task[] releases; lock (_gate) releases = _releases.ToArray();
            try { await Task.WhenAll(releases).ConfigureAwait(false); } catch { }
        }
        finally { await _runtime.DisposeAsync().ConfigureAwait(false); }
    }

    /// <summary>One service request owns this wrapper until its native lease has drained.</summary>
    private sealed class OwnedFacade(LearningExplanationController owner, ILearningExplanationEngine inner,
        LearningModelSnapshot profile, CancellationTokenSource lease) : ILearningExplanationEngine
    {
        private readonly object _gate = new();
        private Task? _disposal;
        public async Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage responseLanguage,
            int maximumOutputTokens, CancellationToken ct)
        {
            using var registration = ct.Register(() => owner.RetireInstruction(lease));
            ct.ThrowIfCancellationRequested(); owner.CheckCurrent(profile, lease.Token);
            var result = await inner.GenerateAsync(input, responseLanguage, maximumOutputTokens, lease.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); owner.CheckCurrent(profile, lease.Token);
            return result;
        }
        public ValueTask DisposeAsync()
        {
            lock (_gate) return new(_disposal ??= DrainAsync());
        }
        private async Task DrainAsync()
        {
            try { await inner.DisposeAsync().ConfigureAwait(false); }
            finally { owner.ReleaseInstruction(lease); owner.FacadeDrained(); }
        }
    }
}
