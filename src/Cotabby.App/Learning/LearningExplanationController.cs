using System.IO;
using System.Diagnostics;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Models;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Support.Runtime;
using Cotabby.Core.Runtime.Metrics;
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
    internal event Action<PerformanceRequestObservation>? RequestObserved;
    private readonly object _gate = new();
    private readonly ILearningLocalRuntime _runtime;
    private readonly Func<LearningLocalModelConfiguration, ILearningExplanationEngine> _engineFactory;
    private readonly Func<LearningModelSnapshot, ILearningExplanationEngine>? _endpointFactory;
    private readonly Func<LearningModelSnapshot, ILearningInstructionEngine>? _endpointInstructionFactory;
    private readonly SemaphoreSlim _instructionSerial = new(1, 1);
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
    private LearningLanguage _instructionLanguage;
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
        Func<LearningLocalModelConfiguration, ILearningExplanationEngine>? engineFactory = null,
        Func<LearningModelSnapshot, ILearningExplanationEngine>? endpointFactory = null,
        Func<LearningModelSnapshot, ILearningInstructionEngine>? endpointInstructionFactory = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _modelDirectory = Path.GetFullPath(modelDirectory);
        _batterySaverOn = batterySaverOn ?? (() => false);
        _clock = clock ?? TimeProvider.System;
        _idleDelay = idleDelay ?? TimeSpan.FromSeconds(60);
        if (_idleDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleDelay));
        _engineFactory = engineFactory ?? (configuration => new LearningLocalExplanationEngine(configuration, _runtime));
        _endpointFactory = endpointFactory; _endpointInstructionFactory = endpointInstructionFactory;
        _profile = settings.LearningExplanationSnapshot();
        _instructionLanguage = settings.ResolvedLearningExplanationLanguage;
        Service = new(_profile, settings.ResolvedLearningExplanationLanguage,
            _ => throw new InvalidOperationException("Explanation requires an admission reservation."), _clock, ReserveExplanation);
        ApplySettings(settings);
    }

    internal LearningExplanationService Service { get; }
    internal LocalRuntimeResidency Residency { get { lock (_gate) return _residency; } }

    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var next = settings.LearningExplanationSnapshot();
        bool admitted = next.Backend == LearningModelBackend.Endpoint
            ? _endpointFactory is not null && _endpointInstructionFactory is not null && !string.IsNullOrWhiteSpace(next.EndpointModel)
                && LearningExplanationModelPolicy.SupportsGeneralInstructions(next.EndpointModel, LearningChatTemplate.Automatic)
            : settings.IsLocalModelCandidate(next.LocalModel, LocalModelTask.Explanation)
                && LocalModelTaskPolicy.PermitsTemplate(next.Template, LocalModelTask.Explanation);
        lock (_gate)
        {
            if (_disposal is not null) return;
            bool allocationChanged = !_profile.UsesSameRuntime(next) || _modelAdmitted != admitted;
            if (_profile != next) StopIdleLocked();
            if (_profile != next || _instructionLanguage != settings.ResolvedLearningExplanationLanguage) RetireInstructionLocked();
            _profile = next;
            _instructionLanguage = settings.ResolvedLearningExplanationLanguage;
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
        _residency = LocalRuntimeResidencyPolicy.Residency(usesLocalModel: _profile.Backend == LearningModelBackend.Local,
            enabled: allowed, paused: !allowed, keepWarm: false);
        if (!allowed)
        {
            RetireInstructionLocked();
            StopIdleLocked(); Service.CancelPending();
            if (_nativeMayBeResident) ReleaseLocked();
        }
        else if (_activeFacades == 0) ScheduleIdleLocked();
    }

    private ILearningExplanationAdmission ReserveExplanation(CancellationToken caller)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            caller.ThrowIfCancellationRequested();
            return new ExplanationAdmission(this, ReserveInstructionLocked());
        }
    }

    private ILearningExplanationEngine CreateEngine(LearningModelSnapshot profile, CancellationTokenSource lease)
    {
        lock (_gate)
        {
            if (_disposal is not null || !AllowedLocked() || _profile != profile || lease.IsCancellationRequested || !ReferenceEquals(_currentInstruction, lease)) throw new OperationCanceledException();
            if (!_modelAdmitted) throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            if (profile.Backend == LearningModelBackend.Endpoint)
            {
                var endpoint = _endpointFactory!(profile);
                StopIdleLocked(); _activeFacades++;
                return new OwnedFacade(this, endpoint, profile, lease);
            }
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
            return new OwnedFacade(this, inner, profile, lease);
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
            if (_disposal is not null || !AllowedLocked())
                throw new OperationCanceledException();
            if (!_modelAdmitted) throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            var profile = _profile;
            var lease = ReserveInstructionLocked();
            StopIdleLocked(); _activeFacades++; _nativeMayBeResident |= profile.Backend == LearningModelBackend.Local;
            var task = Task.Run(() => GenerateExpressionCoreAsync(request, lowercase, profile, lease, token));
            _instructionWork.Add(task);
            _ = task.ContinueWith(t => { lock (_gate) _instructionWork.Remove(t); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    /// <summary>Reading lookup shares the same instruction lease and local runtime as explanation
    /// and expression, but its nullable source and accepted basic pair remain request-owned.</summary>
    internal Task<T> GenerateReadingLookupAsync<T>(ReadingLookupInput input, ReadingLookupResult? basic,
        Func<string, T> parse, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(parse);
        return GenerateReadingLookupWithProfileAsync(input, basic, (raw, _, _) => parse(raw), token);
    }

    internal Task<LearningExplanationRecord> GenerateReadingLookupDetailsAsync(ReadingLookupInput input, ReadingLookupResult basic,
        LearningExplanationInput target, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(basic); ArgumentNullException.ThrowIfNull(target);
        if (!LearningExplanationPolicy.IsValid(target) || target.Target != input.Target || target.Language != input.Source)
            throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
        return GenerateReadingLookupWithProfileAsync(input, basic, (raw, profile, language) =>
            new LearningExplanationRecord(Guid.NewGuid(), target, language,
                new(basic.Meaning, ReadingLookupPolicy.ParseDetails(raw)), ModelName(profile), _clock.GetUtcNow()), token);
    }

    private Task<T> GenerateReadingLookupWithProfileAsync<T>(ReadingLookupInput input, ReadingLookupResult? basic,
        Func<string, LearningModelSnapshot, LearningLanguage, T> parse, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(parse);
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_disposal is not null || !AllowedLocked())
                throw new OperationCanceledException();
            if (!_modelAdmitted) throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
            var profile = _profile; var language = _instructionLanguage;
            var lease = ReserveInstructionLocked(); StopIdleLocked(); _activeFacades++; _nativeMayBeResident |= profile.Backend == LearningModelBackend.Local;
            var task = Task.Run(() => GenerateReadingLookupCoreAsync(input, basic, parse, profile, language, lease, token));
            _instructionWork.Add(task);
            _ = task.ContinueWith(t => { lock (_gate) _instructionWork.Remove(t); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<T> GenerateReadingLookupCoreAsync<T>(ReadingLookupInput input, ReadingLookupResult? basic,
        Func<string, LearningModelSnapshot, LearningLanguage, T> parse, LearningModelSnapshot profile, LearningLanguage language, CancellationTokenSource lease, CancellationToken caller)
    {
        using var registration = caller.Register(() => RetireInstruction(lease));
        bool serial = false;
        try
        {
            await _instructionSerial.WaitAsync(lease.Token).ConfigureAwait(false); serial = true;
            caller.ThrowIfCancellationRequested(); CheckCurrent(profile, lease.Token);
            if (profile.Backend == LearningModelBackend.Endpoint && input.Admission is null) throw new OperationCanceledException();
            int output = Math.Min(profile.MaximumOutputTokens ?? 1024, basic is null ? 512 : 1024);
            string prompt = ReadingLookupPolicy.Prompt(input, language, profile.ContextWindowTokens, output, basic);
            await using var engine = InstructionEngine(profile);
            string raw = basic is null
                ? await engine.GenerateLookupAsync(prompt, output, 24_000, input.Context is not null, lease.Token, input.Admission).ConfigureAwait(false)
                : await engine.GenerateAsync(prompt, output, 24_000, lease.Token, input.Admission).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested(); CheckCurrent(profile, lease.Token);
            lock (_gate) if (_instructionLanguage != language) throw new OperationCanceledException();
            var result = parse(raw, profile, language);
            caller.ThrowIfCancellationRequested(); CheckCurrent(profile, lease.Token);
            lock (_gate) if (_instructionLanguage != language) throw new OperationCanceledException();
            return result;
        }
        finally { if (serial) _instructionSerial.Release(); ReleaseInstruction(lease); FacadeDrained(); }
    }

    private async Task<ExpressionAssistResult> GenerateExpressionCoreAsync(ExpressionAssistRequest request, Func<string, string> lowercase,
        LearningModelSnapshot profile, CancellationTokenSource lease, CancellationToken caller)
    {
        string requestId = LearningRequestIds.Generate(); long start = 0; DateTimeOffset timestamp = default;
        var outcome = ModelRequestOutcome.Failed; long? usable = null;
        using var registration = caller.Register(() => RetireInstruction(lease));
        bool serial = false;
        try
        {
            await _instructionSerial.WaitAsync(lease.Token).ConfigureAwait(false); serial = true;
            caller.ThrowIfCancellationRequested(); CheckCurrent(profile, lease.Token);
            if (profile.Backend == LearningModelBackend.Endpoint && request.Admission is null) throw new OperationCanceledException();
            int output = Math.Min(profile.MaximumOutputTokens ?? 768, 1536);
            string prompt = ExpressionAssistPrompt.Render(request, profile.ContextWindowTokens, output);
            await using var engine = InstructionEngine(profile);
            timestamp = _clock.GetUtcNow(); start = Stopwatch.GetTimestamp();
            var raw = await engine.GenerateAsync(prompt, output, 12_000, lease.Token, request.Admission).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested(); CheckCurrent(profile, lease.Token);
            var result = ExpressionAssistPrompt.Parse(raw, request, lowercase);
            usable = Elapsed(start); outcome = ModelRequestOutcome.Completed; return result;
        }
        catch (OperationCanceledException) { outcome = ModelRequestOutcome.Cancelled; throw; }
        finally
        {
            if (start != 0)
            {
                if (caller.IsCancellationRequested || lease.IsCancellationRequested) outcome = ModelRequestOutcome.Cancelled;
                try { RequestObserved?.Invoke(new(ModelRequestSource.ExpressionAssist, requestId, ModelName(profile), timestamp, Elapsed(start), outcome, UsableResultMs: usable)); }
                catch { /* Diagnostics cannot change generation. */ }
            }
            if (serial) _instructionSerial.Release(); ReleaseInstruction(lease); FacadeDrained();
        }
    }

    private static string ModelName(LearningModelSnapshot profile) => profile.Backend == LearningModelBackend.Local ? profile.LocalModel : profile.EndpointModel;
    private ILearningInstructionEngine InstructionEngine(LearningModelSnapshot profile)
    {
        if (profile.Backend == LearningModelBackend.Endpoint) return _endpointInstructionFactory!(profile);
        string name = profile.LocalModel;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetExtension(name), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new LearningTranslationException(LearningTranslationFailure.LocalModelNotConfigured);
        return new LearningLocalInstructionEngine(new(Path.Combine(_modelDirectory, name), profile.Template,
            profile.ContextWindowTokens, profile.SamplingOverrides, requiresGeneralInstructions: true), _runtime);
    }

    private static long Elapsed(long started) => Math.Max(0, (long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, MidpointRounding.AwayFromZero));

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
            RetireInstructionLocked();
            // Close service admission now, including cache and saved lookups. Deferring this
            // call to our cleanup worker would leave a window for new non-native requests.
            var serviceDrain = Service.DisposeAsync().AsTask();
            var instructions = _instructionWork.ToArray();
            return new(_disposal = Task.Run(() => DisposeCoreAsync(serviceDrain, instructions)));
        }
    }
    private async Task DisposeCoreAsync(Task serviceDrain, Task[] instructions)
    {
        try
        {
            Exception? failure = null;
            try { await serviceDrain.ConfigureAwait(false); } catch (Exception error) { failure = error; }
            try { await Task.WhenAll(instructions).ConfigureAwait(false); } catch { }
            Task[] cleanup; lock (_gate) cleanup = _leaseCleanup.ToArray();
            try { await Task.WhenAll(cleanup).ConfigureAwait(false); } catch { }
            Task[] releases; lock (_gate) releases = _releases.ToArray();
            try { await Task.WhenAll(releases).ConfigureAwait(false); } catch { }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            try { await _runtime.DisposeAsync().ConfigureAwait(false); }
            finally { _instructionSerial.Dispose(); }
        }
    }

    /// <summary>One service request owns this wrapper until its native lease has drained.</summary>
    private sealed class OwnedFacade(LearningExplanationController owner, ILearningExplanationEngine inner,
        LearningModelSnapshot profile, CancellationTokenSource lease) : ILearningExplanationEngine
    {
        private readonly object _gate = new();
        private bool _serial;
        private int _generationStarted;
        private Task? _disposal;
        public async Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage responseLanguage,
            int maximumOutputTokens, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _generationStarted, 1) != 0)
                throw new InvalidOperationException("An explanation facade owns one request.");
            using var registration = ct.Register(() => owner.RetireInstruction(lease));
            await owner._instructionSerial.WaitAsync(lease.Token).ConfigureAwait(false); _serial = true;
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
            finally { if (_serial) owner._instructionSerial.Release(); owner.FacadeDrained(); }
        }
    }

    private sealed class ExplanationAdmission(LearningExplanationController owner, CancellationTokenSource lease) : ILearningExplanationAdmission
    {
        private int _disposed;
        public CancellationToken Token => lease.Token;
        public ILearningExplanationEngine CreateEngine(LearningModelSnapshot profile) => owner.CreateEngine(profile, lease);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.ReleaseInstruction(lease);
        }
    }
}
