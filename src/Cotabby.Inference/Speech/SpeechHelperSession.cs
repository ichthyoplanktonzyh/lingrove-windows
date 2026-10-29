using System.Text.Encodings.Web;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;

namespace Cotabby.Inference.Speech;

/// <summary>One runtime-owned helper's framed PCM exchange. Short state turns consume readiness
/// and result waiters once; its reader awaits each receiver without holding the state lock. Stop
/// can fail those waiters and reap the exact child even while playback or telemetry is blocked.</summary>
internal sealed class SpeechHelperSession(string executable, SpeechLocalConfiguration configuration,
    TimeSpan timeout, ulong generation, Func<SpeechRuntimeEvent, Task> events,
    ISpeechHelperProcessFactory factory, TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly EventQueue _delivery = new(events);
    private ISpeechHelperProcess? _process;
    private Task<ISpeechHelperProcess>? _startup;
    private Task? _reader;
    private Task<bool>? _stop;
    private CancellationTokenSource? _timeoutCancellation;
    private readonly HashSet<Task> _background = [];
    private readonly List<Exception> _lifetimeErrors = [];
    private TaskCompletionSource? _readyWaiter, _resultWaiter;
    private bool _ready, _started, _closed, _failed;
    private SpeechPlaybackToken? _lastToken;
    private string? _expectedId;
    private Func<SpeechPCMChunk, Task>? _receiver;
    private int _nextChunk, _receivedSamples;
    private int? _sampleRate;
    private ulong _eventSequence;
    private long _loadingTimestamp;
    internal bool HasStopped { get { lock (_gate) return _closed || _started && _process is { HasExited: true }; } }
    internal bool ReaderCompleted { get { lock (_gate) return (_reader is null || _reader.IsCompleted) && _background.Count == 0; } }

    internal bool HasLifetimeErrors { get { lock (_gate) return _lifetimeErrors.Count > 0; } }

    internal async Task StartIfNeededAsync(SpeechPlaybackToken token, Func<bool>? admission = null)
    {
        Task<ISpeechHelperProcess> startup; Task readiness;
        lock (_gate)
        {
            _lastToken = token;
            if (_ready) { NotifyLocked(SpeechLocalRuntimePhase.Ready, loadMs: 0); return; }
            if (_started || _closed) throw Unavailable();
            _started = true; _loadingTimestamp = clock.GetTimestamp();
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _startup = LaunchAsync(); startup = _startup;
            async Task<ISpeechHelperProcess> LaunchAsync() { await admitted.Task.ConfigureAwait(false); SpeechAdmission.Require(admission); return await factory.StartAsync(executable, configuration, CancellationToken.None).ConfigureAwait(false); }
            _readyWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously); readiness = _readyWaiter.Task;
            admitted.SetResult();
        }
        ISpeechHelperProcess process;
        try { process = await startup.ConfigureAwait(false); }
        catch
        {
            lock (_gate)
            {
                if (_readyWaiter is { } waiter && waiter.Task == readiness) { waiter.TrySetCanceled(); _readyWaiter = null; }
                _ = readiness.Exception; // Startup did not await this reserved waiter; consume a concurrent stop's failure.
            }
            throw Unavailable();
        }
        lock (_gate)
        {
            _process = process;
            if (_closed) { _ = readiness.Exception; throw new OperationCanceledException(); }
            NotifyLocked(SpeechLocalRuntimePhase.Loading);
            _reader = Task.Run(ReadAsync); ScheduleTimeoutLocked();
            if (_failed || process.HasExited) FailPendingLocked();
        }
        await readiness.ConfigureAwait(false);
    }

    internal async Task SynthesizeAsync(SpeechAudioRequest request, string language, Func<SpeechPCMChunk, Task> receive)
    {
        var values = LearningModelSamplingValues.Speech.Applying(request.Sampling);
        var payload = new Dictionary<string, object>
        {
            ["id"] = request.Token.RawValue.ToString().ToUpperInvariant(), ["type"] = "synthesize", ["text"] = request.Text,
            ["language"] = language, ["max_frames"] = 512, ["seed"] = values.Seed, ["temperature"] = values.Temperature,
            ["top_p"] = values.TopP, ["top_k"] = values.TopK, ["repetition_penalty"] = values.RepetitionPenalty,
        };
        if (configuration.ReferenceAudioUrl is not null) payload["speaker_audio_path"] = configuration.ReferenceAudioUrl.LocalPath;
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        byte[] line = new byte[encoded.Length + 1]; encoded.CopyTo(line, 0); line[^1] = 10;
        Task result; ISpeechHelperProcess process;
        lock (_gate)
        {
            if (!_ready || _closed || _resultWaiter is not null || _process is null) throw Failed();
            _lastToken = request.Token; NotifyLocked(SpeechLocalRuntimePhase.Generating);
            _expectedId = (string)payload["id"]; _receiver = receive; _nextChunk = 0; _receivedSamples = 0; _sampleRate = null;
            _resultWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously); result = _resultWaiter.Task; process = _process;
            ScheduleTimeoutLocked();
        }
        try { SpeechAdmission.Require(request.Admission); await process.Input.WriteAsync(line, _readCancellation.Token).ConfigureAwait(false); await process.Input.FlushAsync(_readCancellation.Token).ConfigureAwait(false); }
        catch { lock (_gate) FailPendingLocked(); }
        await result.ConfigureAwait(false);
    }

    private async Task ReadAsync()
    {
        try
        {
            ISpeechHelperProcess process; lock (_gate) process = _process!;
            var decoder = new SpeechAudioFrameDecoder(); byte[] buffer = new byte[16_384];
            while (true)
            {
                _readCancellation.Token.ThrowIfCancellationRequested();
                int count = await process.Output.ReadAsync(buffer, _readCancellation.Token).ConfigureAwait(false); if (count == 0) break;
                decoder.Append(buffer.AsSpan(0, count));
                while (decoder.Next() is { } frame)
                { _readCancellation.Token.ThrowIfCancellationRequested(); await ReceiveAsync(frame).ConfigureAwait(false); }
            }
            decoder.Finish();
        }
        catch { /* Termination owns one fixed failure, never parser, pipe or native diagnostic text. */ }
        await TerminatedAsync().ConfigureAwait(false);
    }
    private async Task ReceiveAsync(SpeechAudioFrame frame)
    {
        Func<SpeechPCMChunk, Task>? receiver = null; SpeechPCMChunk? chunk = null;
        lock (_gate)
        {
            if (_closed) return;
            var header = frame.Header;
            if (header.Event == "ready" && header.ProtocolVersion == 2 && !_ready && _resultWaiter is null)
            {
                _ready = true; CancelTimeoutLocked();
                NotifyLocked(SpeechLocalRuntimePhase.Ready, Math.Max(0, (long)clock.GetElapsedTime(_loadingTimestamp).TotalMilliseconds));
                var waiter = _readyWaiter; _readyWaiter = null; waiter?.TrySetResult(); return;
            }
            if (header.Event == "chunk" && header.Id is { } id && id == _expectedId && header.Sequence == _nextChunk && header.Format == "f32le" && header.Channels == 1
                && header.SampleRate is >= 8_000 and <= 192_000 && header.Samples is > 0 and <= SpeechPCMChunk.MaximumBytes / 4
                && frame.PayloadByteCount == header.Samples * 4 && (_sampleRate is null || _sampleRate == header.SampleRate)
                && _receivedSamples + header.Samples <= header.SampleRate * 60 && _receivedSamples + header.Samples <= SpeechEndpointClient.MaximumBytes / 4 && _receiver is not null)
            {
                try
                {
                    chunk = new(frame.CopyPayload(), (int)header.SampleRate.Value); _sampleRate = chunk.SampleRate;
                    _receivedSamples += chunk.SampleCount; _nextChunk++; receiver = _receiver;
                }
                catch { FailPendingLocked(); StartBackground(() => StopAsync("playback_rejected")); return; }
            }
            else if (header.Event == "result" && header.Id == _expectedId && _resultWaiter is not null)
            {
                CancelTimeoutLocked(); var waiter = _resultWaiter; _resultWaiter = null; _expectedId = null; _receiver = null;
                if (header.Ok != true || _receivedSamples == 0 || header.Samples != _receivedSamples || header.SampleRate != _sampleRate) { waiter.TrySetException(Failed()); return; }
                NotifyLocked(SpeechLocalRuntimePhase.Ready); waiter.TrySetResult(); return;
            }
            else { FailPendingLocked(); StartBackground(() => StopAsync("cancelled")); return; }
        }
        try { await receiver!(chunk!).ConfigureAwait(false); }
        catch
        {
            lock (_gate) { if (_closed) return; FailPendingLocked(); StartBackground(() => StopAsync("playback_rejected")); }
        }
    }

    internal Task<bool> StopAsync(string reason)
    {
        lock (_gate)
        {
            if (_stop is not null) return _stop;
            _closed = true; _ready = false; CancelTimeoutLocked(); FailPendingLocked();
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> work = Task.FromResult(false); work = RetireAsync(); _stop = work; admitted.SetResult(); return work;
            async Task<bool> RetireAsync()
            {
                await admitted.Task.ConfigureAwait(false);
                Task<ISpeechHelperProcess>? startup; lock (_gate) { NotifyLocked(SpeechLocalRuntimePhase.Stopping, reason: reason); startup = _startup; }
                ISpeechHelperProcess? process = null;
                if (startup is not null) { try { process = await startup.ConfigureAwait(false); } catch { } }
                bool stopped;
                try { stopped = process is null || await process.StopAndReapAsync().ConfigureAwait(false); }
                catch (Exception error) { lock (_gate) _lifetimeErrors.Add(error); stopped = false; }
                if (!stopped) { lock (_gate) { if (_stop == work) _stop = null; } return false; }
                try { _readCancellation.Cancel(); } catch (Exception error) { lock (_gate) _lifetimeErrors.Add(error); }
                if (process is not null) { try { await process.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { lock (_gate) _lifetimeErrors.Add(error); } }
                lock (_gate) { _process = null; NotifyLocked(SpeechLocalRuntimePhase.Unloaded); }
                return true;
            }
        }
    }
    private Task TerminatedAsync()
    {
        lock (_gate)
        {
            _ready = false; FailPendingLocked(); if (_closed) return Task.CompletedTask;
            NotifyLocked(SpeechLocalRuntimePhase.Failed, reason: "helper_exit");
        }
        return StopAsync("helper_exit");
    }
    private void FailPendingLocked()
    {
        _failed = true; var ready = _readyWaiter; var result = _resultWaiter;
        _readyWaiter = null; _resultWaiter = null; _expectedId = null; _receiver = null;
        ready?.TrySetException(Failed()); result?.TrySetException(Failed());
    }
    private void ScheduleTimeoutLocked()
    {
        CancelTimeoutLocked(); var cancellation = new CancellationTokenSource(); _timeoutCancellation = cancellation;
        StartBackground(async () =>
        {
            try { await Task.Delay(timeout, clock, cancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            finally { lock (_gate) { if (_timeoutCancellation == cancellation) _timeoutCancellation = null; } cancellation.Dispose(); }
            await StopAsync("operation_timeout").ConfigureAwait(false);
        });
    }
    private void CancelTimeoutLocked()
    {
        try { _timeoutCancellation?.Cancel(); } catch (Exception error) { _lifetimeErrors.Add(error); }
        _timeoutCancellation = null;
    }
    private Task StartBackground(Func<Task> operation)
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task task = Task.CompletedTask; task = RunAsync();
        lock (_gate) _background.Add(task); admitted.SetResult(); return task;
        async Task RunAsync()
        {
            await admitted.Task.ConfigureAwait(false);
            try { await operation().ConfigureAwait(false); } catch (Exception error) { lock (_gate) _lifetimeErrors.Add(error); }
            finally { lock (_gate) _background.Remove(task); }
        }
    }
    internal async Task DrainReaderAsync()
    {
        Task? reader; lock (_gate) reader = _reader; if (reader is not null) await reader.ConfigureAwait(false);
        while (true) { Task[] tasks; lock (_gate) tasks = _background.ToArray(); if (tasks.Length == 0) break; await Task.WhenAll(tasks).ConfigureAwait(false); }
        lock (_gate) { if (_lifetimeErrors.Count > 0) { var errors = _lifetimeErrors.ToArray(); _lifetimeErrors.Clear(); throw new AggregateException(errors); } }
    }
    private void NotifyLocked(SpeechLocalRuntimePhase phase, long? loadMs = null, string? reason = null)
    {
        var snapshot = new SpeechRuntimeSnapshot { SessionId = _sessionId, Generation = generation, Phase = phase, ProcessId = _process is { HasExited: false } process ? process.Id : null,
            ModelName = Path.GetFileName(configuration.ModelUrl.LocalPath), LoadedModelFilenames = new HashSet<string> { Path.GetFileName(configuration.ModelUrl.LocalPath), Path.GetFileName(configuration.ProjectionUrl.LocalPath) } };
        unchecked { _eventSequence++; }
        _delivery.Post(new(snapshot, _lastToken, _eventSequence, loadMs, reason));
    }
    private static SpeechException Failed() => new(SpeechFailure.GenerationFailed);
    private static SpeechException Unavailable() => new(SpeechFailure.RuntimeUnavailable);

    /// <summary>Queued telemetry owns immutable events and its delegate, never the session's
    /// process/pipes. A blocked observer cannot hold generation, shutdown or reaping. Per-session
    /// ordering survives observer failure; native lifetime does not await this presentation queue.</summary>
    private sealed class EventQueue(Func<SpeechRuntimeEvent, Task> handler)
    {
        private Task _tail = Task.CompletedTask;
        internal void Post(SpeechRuntimeEvent value) { var previous = _tail; _tail = DeliverAsync(previous, handler, value); }
        private static async Task DeliverAsync(Task previous, Func<SpeechRuntimeEvent, Task> target, SpeechRuntimeEvent value)
        { await Task.Yield(); await previous.ConfigureAwait(false); try { await target(value).ConfigureAwait(false); } catch { /* Observer failures cannot become native generation failures. */ } }
    }
}
