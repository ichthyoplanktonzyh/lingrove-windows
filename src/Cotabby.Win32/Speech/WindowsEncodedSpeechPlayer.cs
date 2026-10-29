using System.Buffers.Binary;
using System.IO;
using System.Runtime.Versioning;
using Cotabby.Core.Speech;
using NAudio.Wave;
using SoundTouch;

namespace Cotabby.Win32.Speech;

/// <summary>Encoded playback owns a short-lived WASAPI graph only after an explicit action.
/// Media Foundation decodes the bounded owned bytes; SoundTouch retains both channels and
/// changes tempo without resampling pitch. Its worker, device and callbacks drain on disposal.</summary>
[SupportedOSPlatform("windows10.0")]
public sealed class WindowsEncodedSpeechPlayer : ISpeechAudioPlaying, IAsyncDisposable
{
    private readonly SynchronizationContext _context = SynchronizationContext.Current ?? throw new InvalidOperationException("Speech requires a UI context.");
    private readonly WindowsSpeechAudioCodec _codec = new();
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task<WindowsSpeechDecodedAudio>> _decode;
    private readonly Action<WindowsSpeechDecodedAudio, double, SpeechPlaybackToken, CancellationToken> _render;
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private CancellationTokenSource? _active;
    private Task? _disposal;
    public Action<SpeechProviderEvent>? EventHandler { get; set; }
    public WindowsEncodedSpeechPlayer() { _decode = (data, token) => _codec.DecodeAsync(data, token); _render = Render; }
    internal WindowsEncodedSpeechPlayer(Func<ReadOnlyMemory<byte>, CancellationToken, Task<WindowsSpeechDecodedAudio>> decode,
        Action<WindowsSpeechDecodedAudio, double, SpeechPlaybackToken, CancellationToken> render) { _decode = decode; _render = render; }

    public void Play(ReadOnlySpan<byte> data, double rate, SpeechPlaybackToken token)
    {
        if (data.IsEmpty || data.Length > SpeechExportPolicy.MaximumBytes || !double.IsFinite(rate) || rate is < .5 or > 1.5) throw new SpeechException(SpeechFailure.InvalidAudio);
        byte[] owned = data.ToArray(); var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            if (_disposal is not null) { cancellation.Dispose(); throw new ObjectDisposedException(nameof(WindowsEncodedSpeechPlayer)); }
            _active?.Cancel(); _active = cancellation;
            Task task = RunAsync(owned, rate, token, cancellation.Token); _work.Add(task);
            _ = task.ContinueWith(t => { _ = t.Exception; lock (_gate) { _work.Remove(t); if (ReferenceEquals(_active, cancellation)) _active = null; } cancellation.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    public void Stop() { lock (_gate) _active?.Cancel(); }
    private async Task RunAsync(byte[] audio, double rate, SpeechPlaybackToken token, CancellationToken cancellation)
    {
        try
        {
            var decoded = await _decode(audio, cancellation).ConfigureAwait(false);
            await WindowsSpeechMta.RunAsync(() => { _render(decoded, rate, token, cancellation); return true; }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch { Post(new SpeechProviderEvent.Failed(token, "Unable to play the speech audio.")); }
    }
    private void Render(WindowsSpeechDecodedAudio audio, double rate, SpeechPlaybackToken token, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var samples = BuildSamples(audio, rate, cancellation);
        using var source = new RawSourceWaveStream(samples, WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels));
        using var finished = new ManualResetEventSlim(); Exception? failure = null;
        using var output = new WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithLatency(100).Build();
        var callbackGate = new object(); bool retired = false;
        EventHandler<StoppedEventArgs> stopped = (_, e) => { lock (callbackGate) { if (!retired) { failure = e.Exception; finished.Set(); } } };
        output.PlaybackStopped += stopped;
        try
        {
            output.Init(source); cancellation.ThrowIfCancellationRequested(); output.Play(); Post(new SpeechProviderEvent.Started(token));
            while (!finished.Wait(50)) { if (cancellation.IsCancellationRequested) { output.Stop(); throw new OperationCanceledException(cancellation); } }
            cancellation.ThrowIfCancellationRequested(); if (failure is not null) throw failure;
            Post(new SpeechProviderEvent.Finished(token));
        }
        finally { output.PlaybackStopped -= stopped; lock (callbackGate) retired = true; }
    }
    internal static MemoryStream BuildSamples(WindowsSpeechDecodedAudio audio, double rate, CancellationToken cancellation)
    {
        var output = new MemoryStream(); SoundTouchProcessor? tempo = rate == 1 ? null : new() { SampleRate = audio.SampleRate, Channels = audio.Channels, Rate = 1, Pitch = 1, Tempo = rate };
        try
        {
            foreach (var packet in audio.Packets)
            {
                cancellation.ThrowIfCancellationRequested();
                if (tempo is null) { output.Write(packet.Span); continue; }
                var input = packet.Span; int channels = audio.Channels;
                for (int offset = 0; offset < input.Length;)
                {
                    cancellation.ThrowIfCancellationRequested(); int frames = Math.Min(1024, (input.Length - offset) / (channels * 4));
                    float[] values = new float[frames * channels];
                    for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(input.Slice(offset + i * 4, 4));
                    tempo.PutSamples(values, frames); offset += values.Length * 4; Drain();
                }
            }
            if (tempo is not null) { tempo.Flush(); Drain(); }
            if (output.Length == 0 || output.Length > (long)audio.SampleRate * audio.Channels * 4 * 181) throw new SpeechException(SpeechFailure.InvalidAudio);
            output.Position = 0; return output;
        }
        catch { output.Dispose(); throw; }
        void Drain()
        {
            while (tempo!.AvailableSamples > 0)
            {
                cancellation.ThrowIfCancellationRequested(); int frames = Math.Min(8192, tempo.AvailableSamples);
                var values = new float[frames * audio.Channels]; int actual = tempo.ReceiveSamples(values, frames);
                if (actual != frames) throw new SpeechException(SpeechFailure.InvalidAudio);
                byte[] bytes = new byte[values.Length * 4];
                for (int i = 0; i < values.Length; i++) { if (!float.IsFinite(values[i]) || MathF.Abs(values[i]) > 8) throw new SpeechException(SpeechFailure.InvalidAudio); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]); }
                if (output.Length > (long)audio.SampleRate * audio.Channels * 4 * 181 - bytes.Length) throw new SpeechException(SpeechFailure.InvalidAudio);
                output.Write(bytes);
            }
        }
    }
    private void Post(SpeechProviderEvent value) => _context.Post(_ => { lock (_gate) if (_disposal is not null) return; EventHandler?.Invoke(value); }, null);
    public ValueTask DisposeAsync() { lock (_gate) { if (_disposal is not null) return new(_disposal); _active?.Cancel(); _disposal = Task.WhenAll(_work.ToArray()); return new(_disposal); } }
}
