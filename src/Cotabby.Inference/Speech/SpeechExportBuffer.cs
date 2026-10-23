using Cotabby.Core.Speech;

namespace Cotabby.Inference.Speech;

/// <summary>One explicit export request owns this accumulator and releases it when the request
/// ends. It adapts the original actor to serialized worker operations: byte normalization/WAV
/// encoding run away from the UI, and the gate protects all mutable buffer state. A generator
/// awaits each ordered Append; concurrent calls are serialized without inventing a playback queue.
/// No device/process/file is owned, and Finish does not seal or clear the original accumulator.</summary>
public sealed class SpeechExportBuffer
{
    private readonly object _gate = new();
    private readonly MemoryStream _data = new();
    private int? _sampleRate;
    private SpeechExportAudio? _encoded;
    public Task AppendAsync(SpeechAudioChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return OnWorker(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (chunk)
            {
                case SpeechAudioChunk.Encoded encoded:
                    if (_data.Length != 0 || _encoded is not null) throw new SpeechExportException(SpeechExportFailure.InvalidAudio);
                    _encoded = SpeechExportPolicy.Encoded(encoded); break;
                case SpeechAudioChunk.Pcm pcm:
                    // The original groups mixed encoded/PCM, rate mismatch and byte overflow
                    // under ResponseTooLarge. Keep that classification instead of reinterpreting it.
                    if (_encoded is not null || _sampleRate is { } rate && rate != pcm.Value.SampleRate
                        || _data.Length + pcm.Value.ByteCount > SpeechExportPolicy.MaximumBytes - 44)
                        throw new SpeechExportException(SpeechExportFailure.ResponseTooLarge);
                    _sampleRate = pcm.Value.SampleRate; _data.Write(pcm.Value.CopyBytes()); break;
                default: throw new ArgumentOutOfRangeException(nameof(chunk));
            }
            return true;
        });
    }
    public Task<SpeechExportAudio> PackageAsync(SystemSpeechRecording recording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return OnWorker(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SpeechExportPolicy.Wav(recording);
        });
    }
    public Task<SpeechExportAudio> FinishAsync() => OnWorker(() =>
    {
        // There is deliberately no cancellation check here: the original Finish returns an
        // already admitted encoded value or packages current PCM even on a cancelled task.
        if (_encoded is not null) return _encoded;
        if (_sampleRate is not { } rate) throw new SpeechExportException(SpeechExportFailure.InvalidAudio);
        return SpeechExportPolicy.Wav(_data.GetBuffer().AsSpan(0, (int)_data.Length), rate);
    });
    private Task<T> OnWorker<T>(Func<T> operation) => Task.Run(() => { lock (_gate) return operation(); });
}
