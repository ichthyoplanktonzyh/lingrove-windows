using System.Diagnostics;
using Cotabby.Core.Speech;

namespace Cotabby.App.Speech;

/// <summary>The app's speech controller owns one facade for per-utterance queue accounting and
/// UI events, while its borrowed renderer owns the serialized audio device. This adapts Mac's
/// MainActor: construction and public calls require one serial UI SynchronizationContext, async
/// continuations return to it, and native callbacks explicitly hop to it. No renderer is created
/// here. Cancellation retires identities first so discarded buffers cannot finish a new request.
/// Shutdown retires promptly; the host must join callers and DrainAsync before releasing the
/// renderer/UI context. Drain tracks even a native preparation that ignores cancellation.</summary>
internal sealed class SpeechPCMPlayer : ISpeechPCMPlaying
{
    private readonly ISpeechPCMRendering _renderer;
    private readonly Func<SpeechFailure, string> _failureText;
    private readonly SynchronizationContext _context;
    private readonly int _ownerThread;
    private readonly HashSet<OwnedWork> _work = [];
    private readonly List<Exception> _retirementErrors = [];
    private OwnedWork? _preparation;
    private ulong _revision;
    private SpeechPlaybackToken? _token;
    private double _rate = 1;
    private int? _sampleRate;
    private SpeechPCMPlaybackState _state = new();
    private bool _playing;
    private OwnedWork? _drainWatchdog;
    private OwnedWork? _outputDrain;
    private bool _outputDrained;
    private bool _inputCompletionStarted;
    private Action<SpeechProviderEvent>? _eventHandler;
    public Action<SpeechProviderEvent>? EventHandler
    {
        get { RequireOwner(); return _eventHandler; }
        set { RequireOwner(); _eventHandler = value; }
    }

    internal SpeechPCMPlayer(ISpeechPCMRendering renderer, Func<SpeechFailure, string>? failureText = null)
    {
        _failureText = failureText ?? (failure => new SpeechException(failure).Message);
        _context = SynchronizationContext.Current ?? throw new InvalidOperationException("PCM playback requires a serial UI context.");
        _ownerThread = Environment.CurrentManagedThreadId;
        _renderer = renderer;
    }

    public void Begin(int sampleRate, double rate, SpeechPlaybackToken token)
    {
        RequireOwner(); Stop(); _revision++;
        ulong revision = _revision;
        _rate = Math.Min(Math.Max(rate, .5), 1.5); _sampleRate = sampleRate; _token = token;
        double playbackRate = _rate;
        var callbacks = Callbacks(new WeakReference<SpeechPCMPlayer>(this), token);
        _preparation = StartOwned(async cancellationToken =>
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                await _renderer.PrepareAsync(sampleRate, playbackRate, revision,
                    callbacks.Played, callbacks.Invalidated, cancellationToken);
                RequireActive(token, cancellationToken);
                // ContinuousClock's integer milliseconds truncate rather than round.
                _eventHandler?.Invoke(new SpeechProviderEvent.Prepared(token, (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds));
            }
            catch (OperationCanceledException) { throw; }
            catch { Failed(token); throw; }
        });
    }

    public async Task EnqueueAsync(SpeechPCMChunk chunk, SpeechPlaybackToken token, CancellationToken cancellationToken = default)
    {
        RequireOwner(); RequireOpenInput(token, cancellationToken);
        if (_sampleRate != chunk.SampleRate) throw InvalidAudio();
        // As in Swift's Task.value, caller cancellation does not abandon the shared preparation.
        // Check it after readiness; Stop can still retire this token while native setup is held.
        if (_preparation is { } preparation) await preparation.Task;
        RequireOpenInput(token, cancellationToken);
        while ((double)_state.QueuedSamples / chunk.SampleRate / _rate >= 2)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            RequireOpenInput(token, cancellationToken);
        }
        long bufferId;
        try { bufferId = _state.Enqueue(chunk.SampleCount); }
        catch (SpeechExportException) { throw InvalidAudio(); }
        await _renderer.EnqueueAsync(chunk, bufferId, _revision, cancellationToken);
        RequireActive(token, cancellationToken);
        await StartIfBufferedAsync(token, cancellationToken);
    }

    private async Task StartIfBufferedAsync(SpeechPlaybackToken token, CancellationToken cancellationToken)
    {
        if (!_playing && _sampleRate is { } sampleRate && _state.CanStart(sampleRate, _rate))
        {
            await _renderer.PlayAsync(_revision, cancellationToken);
            RequireActive(token, cancellationToken); _playing = true;
            _eventHandler?.Invoke(new SpeechProviderEvent.Started(token));
        }
    }

    public async Task EndInputAsync(SpeechPlaybackToken token, CancellationToken cancellationToken = default)
    {
        RequireOwner(); RequireActive(token, cancellationToken);
        if (_state.TotalSamples == 0 || _state.InputEnded || _inputCompletionStarted) throw InvalidAudio();
        _inputCompletionStarted = true;
        // Windows' streaming tempo processor needs an explicit EOF to release its tail, even
        // if earlier callbacks already emptied the ledger during a generation gap. Keep that
        // ledger open during this await: its last played callback must not finish/stop the
        // device before flushing itself has returned. Nonempty input has already joined prep.
        await _renderer.CompleteInputAsync(_revision, cancellationToken);
        RequireActive(token, cancellationToken);
        try { _state.EndInput(); }
        catch (SpeechExportException) { throw InvalidAudio(); }
        Task drain = _renderer.OutputDrainedAsync(_revision);
        _outputDrained = drain.IsCompletedSuccessfully;
        if (!_outputDrained)
        {
            // The device may have an EOF tail beyond its earlier source-buffer callbacks.
            // Reserve and join this barrier even if Play fails or Stop retires us next.
            _outputDrain = StartOwned(async cancellation =>
            {
                try
                {
                    await drain; RequireActive(token, cancellation);
                    _outputDrained = true; FinishIfDrained(token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch { Failed(token); }
            });
        }
        await StartIfBufferedAsync(token, cancellationToken);
        FinishIfDrained(token);
        if (_token != token || _sampleRate is not { } sampleRate) return;
        double seconds = (double)_state.QueuedSamples / sampleRate / _rate + 5;
        _drainWatchdog = StartOwned(async cancellation =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation); }
            catch (OperationCanceledException) { return; }
            Failed(token);
        });
    }

    public void Stop() { RequireOwner(); Retire(release: false); }
    public void Shutdown() { RequireOwner(); Retire(release: true); }
    private void Retire(bool release)
    {
        _token = null; _revision++; ulong revision = _revision;
        if (_preparation?.Cancel() is { } preparationError) _retirementErrors.Add(preparationError); _preparation = null;
        if (_drainWatchdog?.Cancel() is { } watchdogError) _retirementErrors.Add(watchdogError); _drainWatchdog = null;
        if (_outputDrain?.Cancel() is { } drainError) _retirementErrors.Add(drainError); _outputDrain = null;
        _outputDrained = false;
        _inputCompletionStarted = false;
        _state = new(); _sampleRate = null; _playing = false;
        StartOwned(async _ =>
        {
            try { await _renderer.StopAsync(revision, release); }
            catch (Exception error) { _retirementErrors.Add(error); }
        });
    }
    private void RequireActive(SpeechPlaybackToken token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_token != token) throw new OperationCanceledException();
    }
    private void RequireOpenInput(SpeechPlaybackToken token, CancellationToken cancellationToken)
    {
        RequireActive(token, cancellationToken);
        if (_inputCompletionStarted) throw InvalidAudio();
    }
    private void Played(long bufferId, SpeechPlaybackToken token)
    {
        if (_token != token) return;
        _state.Played(bufferId); FinishIfDrained(token);
    }
    private void FinishIfDrained(SpeechPlaybackToken token)
    {
        if (!_state.IsFinished || !_outputDrained) return;
        Stop(); _eventHandler?.Invoke(new SpeechProviderEvent.Finished(token));
    }
    private void Failed(SpeechPlaybackToken token)
    {
        if (_token != token) return;
        Stop(); _eventHandler?.Invoke(new SpeechProviderEvent.Failed(token, _failureText(SpeechFailure.InvalidAudio)));
    }
    private static SpeechException InvalidAudio() => new(SpeechFailure.InvalidAudio);
    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("PCM playback must run on its owning UI context.");
    }
    // Build delegates in a separate static closure scope containing only a weak reference/token.
    // A native graph may retain its callback until a late completion; that must not root the UI
    // facade. Capturing this in the preparation task itself lasts only until preparation joins.
    private static (Func<long, Task> Played, Func<Task> Invalidated) Callbacks(WeakReference<SpeechPCMPlayer> weak, SpeechPlaybackToken token) =>
        (id => weak.TryGetTarget(out var owner) ? owner.OnOwnerAsync(() => owner.Played(id, token)) : Task.CompletedTask,
         () => weak.TryGetTarget(out var owner) ? owner.OnOwnerAsync(() => owner.Failed(token)) : Task.CompletedTask);
    private Task OnOwnerAsync(Action callback)
    {
        if (Environment.CurrentManagedThreadId == _ownerThread) { callback(); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(_ =>
        {
            try { RequireOwner(); callback(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }, null);
        return completion.Task;
    }

    /// <summary>Join facade-owned preparation/watchdog/stop work after Shutdown and after callers
    /// have joined generation/enqueue tasks. A native cleanup contract violation is reported here
    /// rather than becoming an unobserved background exception.</summary>
    internal async Task DrainAsync()
    {
        RequireOwner();
        while (_work.Count > 0) await Task.WhenAll(_work.Select(work => ObserveAsync(work.Task)).ToArray());
        if (_retirementErrors.Count > 0) { var errors = _retirementErrors.ToArray(); _retirementErrors.Clear(); throw new AggregateException(errors); }
    }
    private OwnedWork StartOwned(Func<CancellationToken, Task> operation)
    {
        var work = new OwnedWork(); var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(work); work.Task = ExecuteAsync(); _ = ObserveAsync(work.Task); admitted.SetResult(); return work;
        async Task ExecuteAsync()
        {
            // Reserve the task before executing even a synchronously-completing renderer. This
            // is the ordering provided by Mac's Task creation on MainActor, not by C# async calls.
            await admitted.Task;
            try { await operation(work.Cancellation); }
            finally { work.Complete(); _work.Remove(work); }
        }
    }
    private static async Task ObserveAsync(Task task) { try { await task; } catch { /* Preparation errors remain on Task for Enqueue and emit Failed at source. */ } }
    /// <summary>One facade task owns its cancellation source until completion. A completed
    /// preparation can remain the readiness barrier; cancelling it must not touch a disposed CTS.</summary>
    private sealed class OwnedWork
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _complete;
        internal Task Task { get; set; } = Task.CompletedTask;
        internal CancellationToken Cancellation => _cancellation.Token;
        internal Exception? Cancel()
        {
            // Swift Task.cancel() cannot interrupt retirement with a callback exception. .NET
            // cancellation can; keep the token retired and finish cleanup before reporting it.
            try { if (!_complete) _cancellation.Cancel(); return null; }
            catch (Exception error) { return error; }
        }
        internal void Complete() { _complete = true; _cancellation.Dispose(); }
    }
}
