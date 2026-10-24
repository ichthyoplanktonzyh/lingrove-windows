using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cotabby.Core.Speech;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Cotabby.Win32.Speech;

/// <summary>Creates a lazy Windows render endpoint after an explicit utterance. The device
/// owns its MTA thread and COM graph; no activation occurs when the factory itself is composed.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsWasapiSpeechDeviceFactory : IWindowsSpeechOutputDeviceFactory
{
    public Task<IWindowsSpeechOutputDevice> CreateAsync(int sampleRate, Action<ulong, long> progressed, Action invalidated, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("Speech playback requires Windows 10 or later.");
        return WindowsWasapiSpeechDevice.CreateAsync(sampleRate, progressed, invalidated, cancellationToken);
    }
}

/// <summary>One event-driven MTA owner opens, warms, pumps and releases a shared Float32 mono
/// WASAPI stream. All GetBuffer/ReleaseBuffer and COM service releases stay on that same thread.
/// A separate renderer performs tempo DSP. Endpoint callbacks only retire an atomic validity bit
/// and signal this owner; they never stop audio or release COM while Windows holds its callback
/// lock. The raw stream clock and authored/silence timeline settle playback, without QPC guessing.
/// Clock completion describes the endpoint stream, not acoustic microphone measurements.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsWasapiSpeechDevice : IWindowsSpeechOutputDevice
{
    private sealed record Command(Action Execute, TaskCompletionSource? Completion);
    private readonly int _sampleRate;
    private readonly Action<ulong, long> _progressed;
    private readonly Action _invalidated;
    private readonly object _lifetime = new();
    private readonly ConcurrentQueue<Command> _commands = [];
    private readonly AutoResetEvent _workReady = new(false);
    private readonly AutoResetEvent _audioReady = new(false);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> _cleanupErrors = [];
    private readonly Thread _thread;
    private readonly CancellationToken _startupCancellation;
    private bool _accepting = true;
    private Task? _disposeTask;
    private int _invalid;
    // Native fields below belong exclusively to _thread. IsValid is the sole cross-thread state.
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _endpoint;
    private MMDeviceNotificationClient? _notifications;
    private AudioClient? _client;
    private AudioRenderClient? _render;
    private AudioClockClient? _clock;
    private AudioSessionControl? _session;
    private SessionEvents? _sessionEvents;
    private string _endpointId = "";
    private int _bufferFrames;
    private ulong _clockFrequency;
    private ulong _epoch;
    private bool _playing;
    private bool _quit;
    private bool _invalidationPublished;
    private WindowsSpeechOutputTimeline _timeline = new();
    private readonly Queue<byte[]> _packets = [];
    private int _packetOffset;
    private long _queuedBytes;
    private long _publishedFrames;
    private int _clockMisses;

    private WindowsWasapiSpeechDevice(int sampleRate, Action<ulong, long> progressed, Action invalidated, CancellationToken cancellationToken)
    {
        if (sampleRate is < 8000 or > 192000) throw Invalid();
        _sampleRate = sampleRate; _progressed = progressed; _invalidated = invalidated;
        _startupCancellation = cancellationToken;
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "Lingrove speech output" };
        _thread.SetApartmentState(ApartmentState.MTA); _thread.Start();
    }
    internal static async Task<IWindowsSpeechOutputDevice> CreateAsync(int sampleRate, Action<ulong, long> progressed, Action invalidated, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = new WindowsWasapiSpeechDevice(sampleRate, progressed, invalidated, cancellationToken);
        try { await device._ready.Task.ConfigureAwait(false); return device; }
        catch
        {
            try { await device.DisposeAsync().ConfigureAwait(false); } catch { /* Initial failure remains the request's error. */ }
            cancellationToken.ThrowIfCancellationRequested();
            throw Invalid();
        }
    }
    public bool IsValid => Volatile.Read(ref _invalid) == 0;
    public Task ResetAsync(ulong epoch) => Submit(() =>
    {
        RequireValid();
        // Renderer identities have already retired; no reset-returned samples can finish them.
        StopAndReset(); _epoch = epoch;
    });
    public Task EnqueueAsync(IReadOnlyList<byte[]> packets, ulong epoch) => Submit(() =>
    {
        RequireEpoch(epoch); long bytes = 0;
        foreach (var packet in packets)
        {
            if (packet.Length is <= 0 or > 32768 || packet.Length % 4 != 0) throw Invalid();
            bytes = checked(bytes + packet.Length);
        }
        // Normal quarter-second admission/backpressure stays far below this detached-queue cap.
        if (bytes > 1_048_576 || _queuedBytes > 16_777_216 - bytes) throw Invalid();
        foreach (var packet in packets) _packets.Enqueue(packet);
        _queuedBytes += bytes;
    });
    public Task StartAsync(ulong epoch) => Submit(() =>
    {
        RequireEpoch(epoch);
        if (_playing) return;
        // Every start follows StopAndReset: WASAPI Reset flushes pending audio and resets the
        // stream clock to zero. Prefill that known coordinate before starting, rather than
        // requiring a clock reading from hardware which has not started yet. No completion is
        // reported here; only successful running-clock observations acknowledge audio.
        if (_timeline.WrittenDeviceFrames != 0) throw Invalid();
        FillBuffer(0); _client!.Start(); _playing = true;
    });

    private Task Submit(Action action)
    {
        lock (_lifetime)
        {
            if (!_accepting) return Task.FromException(Invalid());
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _commands.Enqueue(new(action, completion)); _workReady.Set(); return completion.Task;
        }
    }
    private void ThreadMain()
    {
        bool initialized = false;
        try
        {
            // Generated COM projections call native activation directly. Explicitly balance our
            // COM initialization even if the CLR has already initialized this MTA (S_FALSE).
            int hr = CoInitializeEx(IntPtr.Zero, 0); Marshal.ThrowExceptionForHR(hr); initialized = true;
            OpenAndWarm(); _ready.TrySetResult();
            WaitHandle[] waits = [_workReady, _audioReady];
            while (!_quit)
            {
                for (int count = 0; count < 32 && _commands.TryDequeue(out var command); count++)
                {
                    try { command.Execute(); command.Completion?.TrySetResult(); }
                    catch (Exception error) { command.Completion?.TrySetException(error is SpeechException or OperationCanceledException ? error : Invalid()); }
                }
                if (!IsValid && !_invalidationPublished)
                {
                    _invalidationPublished = true;
                    TryCleanup(StopAndReset); _invalidated();
                }
                if (_playing && IsValid)
                {
                    try { Pump(); }
                    catch { SignalInvalidation(); continue; }
                }
                if (!_quit) WaitHandle.WaitAny(waits, _playing ? 20 : Timeout.Infinite);
            }
        }
        catch
        {
            _ready.TrySetException(Invalid()); SignalInvalidation();
            if (!_quit && !_invalidationPublished) { _invalidationPublished = true; _invalidated(); }
        }
        finally
        {
            Interlocked.Exchange(ref _invalid, 1);
            lock (_lifetime) _accepting = false;
            while (_commands.TryDequeue(out var command)) command.Completion?.TrySetException(Invalid());
            TryCleanup(() => _notifications?.Dispose()); _notifications = null;
            TryCleanup(() => { if (_sessionEvents is not null) _session?.UnRegisterEventClient(_sessionEvents); });
            // MMDevice's manager also owns this cached control. Dispose is idempotent, and
            // releasing it here removes callbacks before client/service teardown on this MTA.
            TryCleanup(() => _session?.Dispose()); _session = null; _sessionEvents = null;
            TryCleanup(StopAndReset);
            // AudioClient owns the render/clock service references and releases them first.
            TryCleanup(() => _client?.Dispose()); _client = null; _render = null; _clock = null;
            TryCleanup(() => _endpoint?.Dispose()); _endpoint = null;
            TryCleanup(() => _enumerator?.Dispose()); _enumerator = null;
            if (initialized) CoUninitialize();
            _audioReady.Dispose(); _workReady.Dispose();
            _stopped.TrySetResult();
        }
    }
    private void OpenAndWarm()
    {
        // Cancellation is observed between synchronous driver calls. Such a call cannot be
        // forcibly interrupted in process; its owning task/thread must still join on shutdown.
        _startupCancellation.ThrowIfCancellationRequested();
        _enumerator = new MMDeviceEnumerator();
        _endpoint = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); _endpointId = _endpoint.ID;
        _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia && !string.Equals(e.DeviceId, _endpointId, StringComparison.Ordinal)) SignalInvalidation();
        };
        _notifications.DeviceRemoved += (_, e) => { if (string.Equals(e.DeviceId, _endpointId, StringComparison.Ordinal)) SignalInvalidation(); };
        _notifications.DeviceStateChanged += (_, e) =>
        {
            if (string.Equals(e.DeviceId, _endpointId, StringComparison.Ordinal) && e.NewState != DeviceState.Active) SignalInvalidation();
        };
        _notifications.PropertyValueChanged += (_, e) =>
        {
            // Volume/property chatter must not interrupt speech; device-format changes do.
            var format = PropertyKeys.PKEY_AudioEngine_DeviceFormat;
            if (string.Equals(e.DeviceId, _endpointId, StringComparison.Ordinal)
                && e.PropertyKey.formatId == format.formatId && e.PropertyKey.propertyId == format.propertyId) SignalInvalidation();
        };
        using (var current = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            if (!string.Equals(current.ID, _endpointId, StringComparison.Ordinal)) throw Invalid();
        _startupCancellation.ThrowIfCancellationRequested();
        _client = _endpoint.CreateAudioClient();
        _client.Initialize(AudioClientShareMode.Shared,
            AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
            0, 0, WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 1), Guid.Empty);
        _startupCancellation.ThrowIfCancellationRequested();
        _client.SetEventHandle(_audioReady.SafeWaitHandle.DangerousGetHandle());
        _bufferFrames = _client.BufferSize;
        if (_bufferFrames <= 0 || _bufferFrames > _sampleRate * 2) throw Invalid();
        _render = _client.AudioRenderClient; _clock = _client.AudioClockClient; _clockFrequency = _clock.Frequency;
        if (_clockFrequency == 0) throw Invalid();
        // Guid.Empty in Initialize and the endpoint manager select this process's default
        // session. Its disconnect callback also runs while stopped, when no pump can discover
        // service shutdown or an exclusive-mode takeover. It only signals our owner thread.
        _session = _endpoint.AudioSessionManager.AudioSessionControl;
        _sessionEvents = new SessionEvents(SignalInvalidation); _session.RegisterEventClient(_sessionEvents);
        // Initialize the hardware in parallel with model loading, then stop it throughout idle.
        // No model/user audio is involved in this short, explicitly silent warm-up.
        using (var lease = _render.GetBufferLease(_bufferFrames, 4)) { lease.Buffer.Clear(); lease.Release(); }
        _startupCancellation.ThrowIfCancellationRequested();
        _client.Start(); _playing = true; StopAndReset(); RequireValid();
        _startupCancellation.ThrowIfCancellationRequested();
    }
    private bool Pump()
    {
        RequireValid();
        // A false clock result has no usable out-position. Do not acknowledge or assign new
        // PCM to that cursor; a later successful sample can re-anchor after any ensuing gap.
        if (!TryCursorFrames(out long cursor))
        {
            if (++_clockMisses >= 100) SignalInvalidation();
            return false;
        }
        _clockMisses = 0; long played = _timeline.Advance(cursor);
        if (played != _publishedFrames)
        {
            _publishedFrames = played; _progressed(_epoch, played);
        }
        FillBuffer(cursor);
        return true;
    }
    private void FillBuffer(long cursor)
    {
        RequireValid();
        int padding = _client!.CurrentPadding;
        if (padding < 0 || padding > _bufferFrames) throw Invalid();
        int free = _bufferFrames - padding;
        if (free == 0) return;
        // Raw clock and padding are sampled separately. One endpoint buffer is a conservative
        // completion margin; re-anchor after underruns so new PCM never occupies an old gap.
        _timeline.AnchorNextWrite(checked(cursor + padding + _bufferFrames));
        using var lease = _render!.GetBufferLease(free, 4);
        Span<byte> buffer = lease.Buffer; buffer.Clear(); int written = 0;
        while (written < buffer.Length && _packets.TryPeek(out var packet))
        {
            int count = Math.Min(buffer.Length - written, packet.Length - _packetOffset);
            packet.AsSpan(_packetOffset, count).CopyTo(buffer[written..]);
            _timeline.Audio(count / 4); written += count; _packetOffset += count; _queuedBytes -= count;
            if (_packetOffset == packet.Length) { _packets.Dequeue(); _packetOffset = 0; }
        }
        if (written < buffer.Length) _timeline.Silence((buffer.Length - written) / 4);
        lease.Release(free);
    }
    private bool TryCursorFrames(out long frames)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!_clock!.GetPosition(out ulong position, out _)) continue;
            decimal value = decimal.Floor((decimal)position * _sampleRate / _clockFrequency);
            if (value > long.MaxValue) throw Invalid(); frames = (long)value; return true;
        }
        frames = 0; return false;
    }
    private void StopAndReset()
    {
        _playing = false;
        try
        {
            if (_client is not null) { _client.Stop(); _client.Reset(); }
        }
        finally
        {
            _packets.Clear(); _packetOffset = 0; _queuedBytes = 0; _timeline = new(); _publishedFrames = 0; _clockMisses = 0;
        }
    }
    private void RequireEpoch(ulong epoch) { RequireValid(); if (epoch != _epoch) throw new OperationCanceledException(); }
    private void RequireValid() { if (!IsValid) throw Invalid(); }
    private void SignalInvalidation()
    {
        Interlocked.Exchange(ref _invalid, 1);
        try { _workReady.Set(); } catch (ObjectDisposedException) { /* A late native notification has no owner left to wake. */ }
    }
    private void TryCleanup(Action cleanup)
    {
        try { cleanup(); }
        catch (Exception error) when ((uint)error.HResult == 0x88890004) { /* Device invalidation is the playback failure already reported. */ }
        catch (Exception error) { _cleanupErrors.Add(error); }
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifetime)
        {
            if (_disposeTask is null)
            {
                if (_accepting)
                {
                    _accepting = false;
                    _commands.Enqueue(new(() => { Interlocked.Exchange(ref _invalid, 1); _quit = true; }, null));
                    _workReady.Set();
                }
                _disposeTask = JoinAsync();
            }
            return new(_disposeTask);
        }
    }
    private async Task JoinAsync()
    {
        await _stopped.Task.ConfigureAwait(false);
        // The stopped signal is set at the last native-owner action; join the actual thread too.
        await Task.Run(_thread.Join).ConfigureAwait(false);
        if (_cleanupErrors.Count > 0) throw new AggregateException(_cleanupErrors);
    }
    private static SpeechException Invalid() => new(SpeechFailure.InvalidAudio);
    /// <summary>The native session owns callback registration; the MTA unregisters this handler
    /// before release. Mixer metadata and volume changes do not retire speech. A disconnect only
    /// marks validity and wakes the owner; no COM teardown runs inside Windows' callback.</summary>
    private sealed class SessionEvents(Action disconnected) : IAudioSessionEventsHandler
    {
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => disconnected();
        public void OnVolumeChanged(float volume, bool isMuted) { }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) { }
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint options);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
