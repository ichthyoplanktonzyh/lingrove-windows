using Cotabby.Core.Speech;

namespace Cotabby.Inference.Speech;

/// <summary>One audio generator owns one lazy helper session, independent of both other model
/// runtimes. Short locked turns replace Swift actor turns; no lock spans a pipe, receiver, observer
/// or process wait. A new token is reserved before awaiting old retirement, and an unreaped child
/// remains reachable so no later request can allocate overlapping model weights.</summary>
public sealed class LocalSpeechRuntime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ISpeechHelperProcessFactory _factory;
    private readonly ISpeechMemoryPressureSource _pressure;
    private readonly string _executable;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idleTimeout, _operationTimeout;
    private readonly Func<SpeechRuntimeEvent, Task> _events;
    private SpeechHelperSession? _session, _drainingSession;
    private SpeechLocalConfiguration? _configuration;
    private SpeechPlaybackToken? _activeToken;
    private CancellationTokenSource? _idleCancellation;
    private IDisposable? _pressureBinding;
    private ulong _nextGeneration;
    private Retirement? _drain;
    private readonly HashSet<SpeechHelperSession> _sessions = [];
    private readonly HashSet<Task> _work = [];
    private readonly List<Exception> _lifetimeErrors = [];

    public LocalSpeechRuntime(ISpeechHelperProcessFactory factory, ISpeechMemoryPressureSource pressure,
        string? executable = null, TimeSpan? idleTimeout = null, TimeSpan? operationTimeout = null,
        Func<SpeechRuntimeEvent, Task>? events = null, TimeProvider? clock = null)
    {
        _factory = factory; _pressure = pressure;
        _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "Helpers", "lingrove-tts-helper.exe");
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(120); _operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(180);
        _events = events ?? (_ => Task.CompletedTask); _clock = clock ?? TimeProvider.System;
    }

    public async Task StreamAsync(SpeechAudioRequest request, SpeechLocalConfiguration configuration,
        Func<SpeechPCMChunk, Task> receive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string language = LocalSpeechLanguage.Code(request.Language) ?? throw new SpeechException(SpeechFailure.UnsupportedLanguage);
        using var registration = cancellationToken.Register(() => StartOwned(() => CancelAsync(request.Token)));
        try
        {
            var helper = await PrepareAsync(request.Token, configuration, cancellationToken).ConfigureAwait(false);
            await helper.StartIfNeededAsync(request.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); RequireCurrent(request.Token);
            await helper.SynthesizeAsync(request, language, receive).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                RequireCurrentLocked(request.Token); _activeToken = null;
                var idle = new CancellationTokenSource(); _idleCancellation = idle;
                StartOwned(async () =>
                {
                    try { await Task.Delay(_idleTimeout, _clock, idle.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    finally { lock (_gate) { if (_idleCancellation == idle) _idleCancellation = null; } idle.Dispose(); }
                    await ReleaseIfIdleAsync("idle").ConfigureAwait(false);
                });
            }
        }
        catch { await CancelAsync(request.Token).ConfigureAwait(false); throw; }
    }

    private async Task<SpeechHelperSession> PrepareAsync(SpeechPlaybackToken token, SpeechLocalConfiguration configuration, CancellationToken cancellation)
    {
        Retirement? retirement;
        lock (_gate)
        {
            _pressureBinding ??= _pressure.Observe(PressureCallback(new WeakReference<LocalSpeechRuntime>(this)));
            CancelIdleLocked(); bool restart = _activeToken is not null || _configuration != configuration;
            _activeToken = token;
            retirement = restart || _drain is not null || _drainingSession is not null ? ReserveRetirementLocked("cancelled") : null;
        }
        await CompleteRetirementAsync(retirement).ConfigureAwait(false);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            lock (_gate)
            {
                RequireCurrentLocked(token);
                if (_session is { HasStopped: true }) retirement = ReserveRetirementLocked("cancelled");
                else if (_drain is not null || _drainingSession is not null) retirement = ReserveRetirementLocked("cancelled");
                else
                {
                    if (_session is not null) return _session;
                    unchecked { _nextGeneration++; }
                    _sessions.RemoveWhere(session => session != _drainingSession && session.ReaderCompleted && !session.HasLifetimeErrors);
                    _session = new(_executable, configuration, _operationTimeout, _nextGeneration, _events, _factory, _clock);
                    _configuration = configuration; _sessions.Add(_session); return _session;
                }
            }
            await CompleteRetirementAsync(retirement).ConfigureAwait(false);
        }
    }

    public async Task CancelAsync(SpeechPlaybackToken token)
    {
        Retirement? retirement;
        lock (_gate)
        {
            if (_activeToken != token) return;
            _activeToken = null; retirement = ReserveRetirementLocked("cancelled");
        }
        try { await CompleteRetirementAsync(retirement).ConfigureAwait(false); }
        catch (SpeechException error) when (error.Failure == SpeechFailure.RuntimeUnavailable) { /* Keep the unreaped session for a later retry. */ }
    }
    public async Task ShutdownAsync()
    {
        Retirement? retirement; IDisposable? binding;
        lock (_gate)
        {
            binding = _pressureBinding; _pressureBinding = null; CancelIdleLocked(); _activeToken = null;
            retirement = ReserveRetirementLocked("shutdown");
        }
        // A native subscription can join its callback while disposing. Never hold the state lock
        // here: that callback may already be waiting to evaluate the idle-only retirement guard.
        try { if (binding is not null) await Task.Run(binding.Dispose).ConfigureAwait(false); } catch (Exception error) { lock (_gate) _lifetimeErrors.Add(error); }
        try { await CompleteRetirementAsync(retirement).ConfigureAwait(false); }
        catch (SpeechException error) when (error.Failure == SpeechFailure.RuntimeUnavailable) { }
    }
    private static Action PressureCallback(WeakReference<LocalSpeechRuntime> weak) =>
        () => { if (weak.TryGetTarget(out var runtime)) runtime.StartOwned(() => runtime.ReleaseIfIdleAsync("memory_pressure")); };
    private Task ReleaseIfIdleAsync(string reason)
    {
        Retirement? retirement;
        lock (_gate) { if (_activeToken is not null) return Task.CompletedTask; retirement = ReserveRetirementLocked(reason); }
        return IgnoreUnavailableAsync(retirement);
    }
    private async Task IgnoreUnavailableAsync(Retirement? retirement)
    { try { await CompleteRetirementAsync(retirement).ConfigureAwait(false); } catch (SpeechException error) when (error.Failure == SpeechFailure.RuntimeUnavailable) { } }

    private Retirement? ReserveRetirementLocked(string reason)
    {
        if (_session is not null) { _drainingSession = _session; _session = null; _configuration = null; }
        if (_drain is null && _drainingSession is { } session)
        {
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var retirement = new Retirement(session, RetireAsync()); _drain = retirement; admitted.SetResult();
            async Task<bool> RetireAsync() { await admitted.Task.ConfigureAwait(false); return await session.StopAsync(reason).ConfigureAwait(false); }
        }
        return _drain;
    }
    private async Task CompleteRetirementAsync(Retirement? retirement)
    {
        if (retirement is null) return;
        bool stopped = await retirement.Work.ConfigureAwait(false);
        lock (_gate)
        {
            if (_drain == retirement)
            {
                _drain = null;
                if (stopped) _drainingSession = null;
            }
            if (stopped && retirement.Session.ReaderCompleted && !retirement.Session.HasLifetimeErrors) _sessions.Remove(retirement.Session);
        }
        if (!stopped) throw new SpeechException(SpeechFailure.RuntimeUnavailable);
    }
    private void RequireCurrent(SpeechPlaybackToken token) { lock (_gate) RequireCurrentLocked(token); }
    private void RequireCurrentLocked(SpeechPlaybackToken token) { if (_activeToken != token) throw new OperationCanceledException(); }
    private void CancelIdleLocked()
    {
        try { _idleCancellation?.Cancel(); }
        catch (Exception error) { _lifetimeErrors.Add(error); }
        _idleCancellation = null;
    }
    private void StartOwned(Func<Task> operation)
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task task = Task.CompletedTask; task = RunAsync();
        lock (_gate) _work.Add(task); admitted.SetResult();
        async Task RunAsync()
        {
            await admitted.Task.ConfigureAwait(false);
            try { await operation().ConfigureAwait(false); }
            catch (Exception error) { lock (_gate) _lifetimeErrors.Add(error); }
            finally { lock (_gate) _work.Remove(task!); }
        }
    }

    /// <summary>The host first stops/joins request callers and releases blocked PCM receivers.
    /// Retirement itself never waits for a receiver or observer: those must not prevent reaping or
    /// the next request. This explicit drain joins reader/background work before dependencies die.</summary>
    public async Task DrainAsync()
    {
        var errors = new List<Exception>();
        while (true)
        {
            Task[] work; SpeechHelperSession[] sessions; SpeechHelperSession? unreaped;
            lock (_gate)
            {
                if (_session is not null) throw new InvalidOperationException("Shut down speech before draining its reader lifetime.");
                work = _work.ToArray(); sessions = _sessions.ToArray(); unreaped = _drainingSession;
            }
            await Task.WhenAll(work).ConfigureAwait(false);
            // A failed native reaping barrier deliberately keeps ownership. Joining its pipe can
            // wait forever, so report that barrier while retaining it for the next shutdown retry.
            foreach (var session in sessions)
            {
                if (session == unreaped) continue;
                try { await session.DrainReaderAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            }
            lock (_gate)
            {
                _sessions.RemoveWhere(session => session != _drainingSession && session.ReaderCompleted && !session.HasLifetimeErrors);
                if (_work.Count > 0) continue;
                errors.AddRange(_lifetimeErrors); _lifetimeErrors.Clear();
                if (_drainingSession is not null) errors.Add(new SpeechException(SpeechFailure.RuntimeUnavailable));
                if (errors.Count > 0) throw new AggregateException(errors);
                return;
            }
        }
    }
    public async ValueTask DisposeAsync() { await ShutdownAsync().ConfigureAwait(false); await DrainAsync().ConfigureAwait(false); }
    private sealed record Retirement(SpeechHelperSession Session, Task<bool> Work);
}
