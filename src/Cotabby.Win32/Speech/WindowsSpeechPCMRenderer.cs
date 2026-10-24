using System.Collections.Concurrent;
using Cotabby.Core.Speech;

namespace Cotabby.Win32.Speech;

/// <summary>One PCM facade borrows this Windows counterpart of Mac's audio actor. Its serial
/// worker owns utterance/revision/tempo state; a separate device owner handles real-time WASAPI
/// pumping, so CPU-heavy tempo work cannot block that thread. Only detached PCM and callback
/// identities cross the boundaries. Stop retires identities before reset; Dispose joins every
/// accepted operation, device cleanup and UI notification before releasing its serialization.</summary>
internal sealed class WindowsSpeechPCMRenderer : ISpeechPCMRendering, IAsyncDisposable
{
    private sealed record PendingBuffer(long Id, long OutputEnd);
    private readonly IWindowsSpeechOutputDeviceFactory _factory;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly object _lifetime = new();
    private readonly HashSet<Task> _work = [];
    private readonly HashSet<Task> _notifications = [];
    private readonly ConcurrentQueue<Exception> _cleanupErrors = [];
    private bool _accepting = true;
    private Task? _disposeTask;
    // The following fields are accessed only while _serial is held, including device callbacks.
    private IWindowsSpeechOutputDevice? _device;
    private int? _sampleRate;
    private Guid? _graphId;
    private ulong _revision;
    private bool _active;
    private bool _eof;
    private WindowsSpeechTempoProcessor? _tempo;
    private Func<long, Task>? _played;
    private Func<Task>? _invalidated;
    private TaskCompletionSource? _outputDrain;
    private long _playedOutput;
    private readonly Queue<PendingBuffer> _pending = [];

    internal WindowsSpeechPCMRenderer(IWindowsSpeechOutputDeviceFactory factory) => _factory = factory;

    public Task PrepareAsync(int sampleRate, double rate, ulong revision, Func<long, Task> played,
        Func<Task> invalidated, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (revision <= _revision) throw new OperationCanceledException();
        Retire(revision); _played = played; _invalidated = invalidated;
        try
        {
            if (_device is null || !_device.IsValid || _sampleRate != sampleRate)
            {
                await ReleaseGraphAsync().ConfigureAwait(false);
                Guid graph = Guid.NewGuid(); _graphId = graph;
                var weak = new WeakReference<WindowsSpeechPCMRenderer>(this);
                _device = await _factory.CreateAsync(sampleRate,
                    (epoch, frames) => { if (weak.TryGetTarget(out var owner)) owner.PostProgress(graph, epoch, frames); },
                    () => { if (weak.TryGetTarget(out var owner)) owner.PostInvalidation(graph); }, cancellationToken).ConfigureAwait(false);
                _sampleRate = sampleRate;
            }
            await _device.ResetAsync(revision).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_device.IsValid) throw Invalid();
            _tempo = new(sampleRate, rate); _outputDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = _outputDrain.Task.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _active = true;
        }
        catch
        {
            Retire(revision); await ReleaseGraphAsync().ConfigureAwait(false); throw;
        }
    }, cancellationToken);

    public Task EnqueueAsync(SpeechPCMChunk chunk, long bufferId, ulong revision, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        RequireActive(revision, cancellationToken);
        if (_eof || chunk.SampleRate != _sampleRate) throw Invalid();
        var packets = _tempo!.Process(chunk, cancellationToken);
        // Intermediate SoundTouch offsets are approximate. Keep a full initial processing
        // window as a conservative acknowledgement margin; EOF caps endpoints to real output.
        _pending.Enqueue(new(bufferId, checked(_tempo.ExpectedOutputSamples + _tempo.OutputLookaheadSamples)));
        await _device!.EnqueueAsync(packets, revision).ConfigureAwait(false);
        RequireActive(revision, cancellationToken); SettlePlayed();
    }, cancellationToken);

    public Task CompleteInputAsync(ulong revision, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        RequireActive(revision, cancellationToken);
        if (_eof) throw Invalid(); _eof = true;
        var packets = _tempo!.CompleteInput(cancellationToken);
        await _device!.EnqueueAsync(packets, revision).ConfigureAwait(false);
        RequireActive(revision, cancellationToken); SettlePlayed();
    }, cancellationToken);

    public async Task OutputDrainedAsync(ulong revision)
    {
        Task? barrier = null;
        await RunAsync(() =>
        {
            RequireActive(revision, default);
            if (!_eof) throw Invalid(); barrier = _outputDrain!.Task; return Task.CompletedTask;
        }).ConfigureAwait(false);
        await barrier!.ConfigureAwait(false);
    }

    public Task PlayAsync(ulong revision, CancellationToken cancellationToken) => RunAsync(async () =>
    {
        RequireActive(revision, cancellationToken);
        await _device!.StartAsync(revision).ConfigureAwait(false);
        RequireActive(revision, cancellationToken);
    }, cancellationToken);

    public Task StopAsync(ulong revision, bool release) => RunAsync(async () =>
    {
        if (revision < _revision) return;
        Retire(revision);
        if (release) await ReleaseGraphAsync().ConfigureAwait(false);
        else if (_device is not null)
        {
            if (!_device.IsValid) { await ReleaseGraphAsync().ConfigureAwait(false); return; }
            try { await _device.ResetAsync(revision).ConfigureAwait(false); }
            catch { await ReleaseGraphAsync().ConfigureAwait(false); }
        }
    }, retirement: true);

    private void RequireActive(ulong revision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (revision != _revision) throw new OperationCanceledException();
        if (!_active || _device is null || !_device.IsValid || _tempo is null) throw Invalid();
    }
    private void Retire(ulong revision)
    {
        _revision = revision; _active = false; _eof = false;
        _played = null; _invalidated = null; _pending.Clear(); _playedOutput = 0;
        _tempo?.Retire(); _tempo = null;
        _outputDrain?.TrySetCanceled(); _outputDrain = null;
    }
    private async Task ReleaseGraphAsync()
    {
        _graphId = null; var device = _device; _device = null; _sampleRate = null;
        if (device is not null)
        {
            try { await device.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { _cleanupErrors.Enqueue(error); }
        }
    }
    private void PostProgress(Guid graph, ulong revision, long frames) => Post(async () =>
    {
        if (_graphId != graph || revision != _revision || !_active) return;
        if (_device?.IsValid != true) { await InvalidateAsync(graph).ConfigureAwait(false); return; }
        if (frames < 0 || frames > _tempo!.OutputSamples) { await InvalidateAsync(graph).ConfigureAwait(false); return; }
        // Device events are monotonic, but their queued actor tasks need not reach this gate in
        // FIFO order. An older valid progress sample must not invalidate the current device.
        if (frames <= _playedOutput) return;
        _playedOutput = frames; SettlePlayed();
    });
    private void PostInvalidation(Guid graph) => Post(() => InvalidateAsync(graph));
    private async Task InvalidateAsync(Guid graph)
    {
        if (_graphId != graph) return;
        var callback = _active ? _invalidated : null;
        _outputDrain?.TrySetException(Invalid());
        Retire(_revision); await ReleaseGraphAsync().ConfigureAwait(false);
        if (callback is not null) Notify(callback);
    }
    private void SettlePlayed()
    {
        if (!_active || _device?.IsValid != true) return;
        while (_pending.TryPeek(out var pending))
        {
            long target = _eof ? Math.Min(pending.OutputEnd, _tempo!.OutputSamples) : pending.OutputEnd;
            if (_playedOutput < target) break;
            _pending.Dequeue(); var callback = _played;
            if (callback is not null) Notify(() => callback(pending.Id));
        }
        if (_eof && _playedOutput >= _tempo!.OutputSamples) _outputDrain!.TrySetResult();
    }

    private Task RunAsync(Func<Task> operation, CancellationToken cancellationToken = default, bool retirement = false)
    {
        lock (_lifetime)
        {
            if (!_accepting) return retirement ? Task.CompletedTask : Task.FromException(new ObjectDisposedException(nameof(WindowsSpeechPCMRenderer)));
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task task = Task.Run(async () =>
            {
                await admitted.Task.ConfigureAwait(false);
                await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try { await operation().ConfigureAwait(false); }
                finally { _serial.Release(); }
            });
            _work.Add(task); ObserveRemoval(task, _work); admitted.SetResult(); return task;
        }
    }
    private void Post(Func<Task> operation)
    {
        // A device callback never blocks, invokes UI, waits for DSP, or acquires COM resources.
        _ = RunAsync(operation, retirement: true);
    }
    private void Notify(Func<Task> callback)
    {
        lock (_lifetime)
        {
            Task task = Task.Run(async () =>
            {
                try { await callback().ConfigureAwait(false); }
                catch (Exception error) { _cleanupErrors.Enqueue(error); }
            });
            _notifications.Add(task); ObserveRemoval(task, _notifications);
        }
    }
    private void ObserveRemoval(Task task, HashSet<Task> group) => _ = task.ContinueWith(completed =>
    {
        _ = completed.Exception;
        lock (_lifetime) group.Remove(completed);
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public ValueTask DisposeAsync()
    {
        lock (_lifetime)
        {
            if (_disposeTask is null)
            {
                _accepting = false; Task[] work = _work.ToArray();
                _disposeTask = Task.Run(async () =>
                {
                    await ObserveAsync(Task.WhenAll(work)).ConfigureAwait(false);
                    await _serial.WaitAsync().ConfigureAwait(false);
                    try { Retire(_revision); await ReleaseGraphAsync().ConfigureAwait(false); }
                    finally { _serial.Release(); }
                    Task[] notifications; lock (_lifetime) notifications = _notifications.ToArray();
                    await ObserveAsync(Task.WhenAll(notifications)).ConfigureAwait(false);
                    _serial.Dispose();
                    if (!_cleanupErrors.IsEmpty) throw new AggregateException(_cleanupErrors);
                });
            }
            return new(_disposeTask);
        }
    }
    private static async Task ObserveAsync(Task task) { try { await task.ConfigureAwait(false); } catch { /* Caller still owns operation errors; cleanup must proceed. */ } }
    private static SpeechException Invalid() => new(SpeechFailure.InvalidAudio);
}
