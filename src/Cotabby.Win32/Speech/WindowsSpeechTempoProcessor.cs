using System.Buffers.Binary;
using Cotabby.Core.Speech;
using SoundTouch;

namespace Cotabby.Win32.Speech;

/// <summary>The Windows renderer owns one processor per utterance, on its serialized worker.
/// It replaces AVAudioUnitTimePitch's speed responsibility while the device keeps its original
/// sample rate. SoundTouch's Tempo changes duration; Rate and Pitch stay at one. Input helper
/// blocks form one continuous stream, and only EOF flushes the analysis window. Returned packets
/// are detached Float32 little-endian audio owned by the renderer, never a device callback buffer.
/// This boundary opens no device/file, synthesizes no speech and retains no text. Cancellation
/// can leave partial DSP state: its owner must retire that utterance rather than retry the block.</summary>
internal sealed class WindowsSpeechTempoProcessor
{
    private const long MaximumInputSamples = 11_520_000;
    private const int BatchSamples = 1024;
    private const int PacketSamples = 8192;
    private const int MaximumOutputPerCall = 262_144;
    private readonly SoundTouchProcessor? _processor;
    private readonly int _sampleRate;
    private readonly double _rate;
    private bool _completed;
    private bool _retired;
    internal long InputSamples { get; private set; }
    internal long OutputSamples { get; private set; }
    internal long ExpectedOutputSamples => (long)Math.Round(InputSamples / _rate, MidpointRounding.AwayFromZero);
    internal long OutputLookaheadSamples => _processor is null ? 0
        : (long)Math.Ceiling(_processor.GetSetting(SettingId.InitialLatency) / _rate);

    internal WindowsSpeechTempoProcessor(int sampleRate, double rate)
    {
        if (sampleRate is < 8000 or > 192000 || !double.IsFinite(rate) || rate is < .5 or > 1.5) throw Invalid();
        _sampleRate = sampleRate; _rate = rate;
        // Normal speed must preserve the exact validated PCM, without a needless DSP delay.
        if (rate == 1) return;
        _processor = new SoundTouchProcessor { SampleRate = sampleRate, Channels = 1, Rate = 1, Pitch = 1, Tempo = rate };
    }

    internal IReadOnlyList<byte[]> Process(SpeechPCMChunk chunk, CancellationToken cancellationToken = default)
    {
        RequireInput(cancellationToken);
        if (chunk.SampleRate != _sampleRate || InputSamples > MaximumInputSamples - chunk.SampleCount) throw Invalid();
        byte[] bytes = chunk.CopyBytes();
        InputSamples += chunk.SampleCount;
        if (_processor is null)
        {
            OutputSamples += chunk.SampleCount;
            var unchanged = new List<byte[]>();
            for (int offset = 0; offset < bytes.Length; offset += PacketSamples * 4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                unchanged.Add(bytes.AsSpan(offset, Math.Min(PacketSamples * 4, bytes.Length - offset)).ToArray());
            }
            return unchanged;
        }
        // Bound each non-cancellable library call. The renderer runs this on its worker, and
        // Stop can retire its revision between operations; the UI never does this DSP work.
        Span<float> batch = stackalloc float[BatchSamples];
        var packets = new List<byte[]>(); int produced = 0;
        for (int start = 0; start < chunk.SampleCount; start += BatchSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(BatchSamples, chunk.SampleCount - start);
            for (int index = 0; index < count; index++)
                batch[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan((start + index) * 4));
            _processor.PutSamples(batch[..count], count);
            TakeOutput(packets, ref produced, cancellationToken);
        }
        return packets;
    }

    internal IReadOnlyList<byte[]> CompleteInput(CancellationToken cancellationToken = default)
    {
        RequireInput(cancellationToken); _completed = true;
        if (_processor is null) return [];
        // The library's bounded Flush may add padding internally and trims output to its
        // accumulated duration. Do this once, never between helper chunks or after retirement.
        _processor.Flush();
        var packets = new List<byte[]>(); int produced = 0;
        TakeOutput(packets, ref produced, cancellationToken);
        // The library accumulates fractional expected output per submitted block. Different
        // packet boundaries can round a half-sample on opposite sides; keep its actual final
        // count for device drain, while rejecting any loss larger than that one-sample bound.
        if (Math.Abs(OutputSamples - ExpectedOutputSamples) > 1) throw Invalid();
        return packets;
    }

    internal void Retire()
    {
        _retired = true; _processor?.Clear();
    }

    private void TakeOutput(List<byte[]> packets, ref int produced, CancellationToken cancellationToken)
    {
        while (_processor!.AvailableSamples > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(PacketSamples, _processor.AvailableSamples);
            if (produced > MaximumOutputPerCall - count) throw Invalid();
            var samples = new float[count];
            int read = _processor.ReceiveSamples(samples.AsSpan(), count);
            if (read != count || samples.Any(sample => !float.IsFinite(sample) || MathF.Abs(sample) > 8)) throw Invalid();
            var packet = new byte[count * 4];
            for (int index = 0; index < count; index++) BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(index * 4), samples[index]);
            packets.Add(packet); produced += count; OutputSamples += count;
        }
    }

    private void RequireInput(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_retired) throw new OperationCanceledException();
        if (_completed) throw Invalid();
    }
    private static SpeechException Invalid() => new(SpeechFailure.InvalidAudio);
}
