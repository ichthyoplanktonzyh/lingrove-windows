using System.Buffers.Binary;

namespace Cotabby.Core.Speech;

/// <summary>Detached mono Float32 little-endian bytes delivered by a speech generator. This
/// boundary validates rate, block size and every sample before an audio device can schedule it.
/// A caller owns the immutable block; playback/export may borrow copies without retaining the
/// generator's mutable pipe buffer. Construction neither opens a file nor schedules playback.</summary>
public sealed class SpeechPCMChunk : IEquatable<SpeechPCMChunk>
{
    public const int MaximumBytes = 262_144;
    private readonly byte[] _data;
    public SpeechPCMChunk(ReadOnlySpan<byte> data, int sampleRate)
    {
        if (sampleRate is < 8_000 or > 192_000 || data.IsEmpty || data.Length > MaximumBytes || data.Length % 4 != 0) throw Invalid();
        // Freeze before inspecting samples: another owner may still mutate the input array.
        // Validate the exact detached bytes we publish, not a pre-copy observation of them.
        _data = data.ToArray();
        for (int offset = 0; offset < _data.Length; offset += 4)
        {
            float sample = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset)));
            if (!float.IsFinite(sample) || MathF.Abs(sample) > 8) throw Invalid();
        }
        SampleRate = sampleRate;
    }
    public int SampleRate { get; }
    public int SampleCount => _data.Length / 4;
    public int ByteCount => _data.Length;
    public byte[] CopyBytes() => (byte[])_data.Clone();
    public bool Equals(SpeechPCMChunk? other) => other is not null && SampleRate == other.SampleRate && _data.AsSpan().SequenceEqual(other._data);
    public override bool Equals(object? obj) => obj is SpeechPCMChunk other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(SampleRate); hash.AddBytes(_data); return hash.ToHashCode(); }
    internal static SpeechExportException Invalid() => new(SpeechExportFailure.InvalidAudio);
}
