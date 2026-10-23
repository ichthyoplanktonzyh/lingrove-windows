using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Cotabby.Core.Speech;

/// <summary>Pure in-memory adaptation of the original short-audio exporter. Float32 mono and
/// admitted WAV become canonical PCM16 WAV; MP3 retains the source's narrow header admission.
/// The future export owner supplies bytes, and review owns explicit upload/acknowledgement.</summary>
public static partial class SpeechExportPolicy
{
    public const int MaximumBytes = 12 * 1024 * 1024;
    public static string Digest(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
    public static string Filename(SpeechExportAudio audio) => "lingrove-" + Digest(audio.Bytes) + "." + audio.FileExtension;
    public static bool ValidFilename(string? value) => value is not null && FilenamePattern().IsMatch(value);
    /// <summary>Typed frozen values let admission read owned bytes without first cloning an
    /// oversized producer body. The view stays inside Core and never escapes to mutable callers.</summary>
    public static SpeechExportAudio Wav(SystemSpeechRecording recording)
    { ArgumentNullException.ThrowIfNull(recording); return Wav(recording.Bytes, recording.SampleRate); }
    public static SpeechExportAudio Encoded(SpeechAudioChunk.Encoded chunk)
    { ArgumentNullException.ThrowIfNull(chunk); return Encoded(chunk.Bytes); }
    public static SpeechExportAudio Wav(ReadOnlySpan<byte> bytes, int sampleRate)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes - 44 || bytes.Length % 4 != 0 || sampleRate is < 8000 or > 192000 || bytes.Length / 4 > sampleRate * 90) throw Invalid();
        var pcm = new byte[bytes.Length / 2];
        for (int offset = 0; offset < bytes.Length; offset += 4)
        {
            float sample = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]));
            if (!float.IsFinite(sample)) throw Invalid();
            short value = (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * 32767f, MidpointRounding.AwayFromZero);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset / 2), value);
        }
        return WaveContainer(pcm, sampleRate, 1);
    }
    public static SpeechExportAudio Encoded(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > MaximumBytes) throw new SpeechExportException(SpeechExportFailure.ResponseTooLarge);
        if (data.StartsWith("RIFF"u8) && data.Length >= 44 && data[8..12].SequenceEqual("WAVE"u8)) return NormalizeWave(data);
        if (data.StartsWith("ID3"u8) || data.Length > 2 && data[0] == 0xff && (data[1] & 0xe0) == 0xe0 && (data[1] & 0x06) != 0 && (data[1] & 0x18) != 0x08)
            return new(data, "mp3");
        throw Invalid();
    }
    private static SpeechExportAudio NormalizeWave(ReadOnlySpan<byte> data)
    {
        int offset = 12; WaveFormat? format = null; byte[]? samples = null;
        while (offset <= data.Length - 8)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]); int start = offset + 8;
            if (size > data.Length - start) throw Invalid();
            var name = data.Slice(offset, 4);
            if (name.SequenceEqual("fmt "u8) && size >= 16)
                format = new(BinaryPrimitives.ReadUInt16LittleEndian(data[start..]), BinaryPrimitives.ReadUInt16LittleEndian(data[(start + 2)..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(data[(start + 4)..]), BinaryPrimitives.ReadUInt16LittleEndian(data[(start + 14)..]));
            if (name.SequenceEqual("data"u8)) samples = data.Slice(start, (int)size).ToArray();
            offset = start + (int)size + (int)(size % 2);
        }
        if (format is not { Channels: >= 1 and <= 2, Rate: >= 8000 and <= 192000 } f || samples is null || samples.Length == 0) throw Invalid();
        if (f.Tag == 1 && f.Bits == 16 && samples.Length % (f.Channels * 2) == 0 && samples.Length / (f.Channels * 2) <= f.Rate * 90)
            return WaveContainer(samples, (int)f.Rate, f.Channels);
        if (f.Tag == 3 && f.Bits == 32 && f.Channels == 1) return Wav(samples, (int)f.Rate);
        throw Invalid();
    }
    /// <summary>Only bounded RIFF metadata needed for source timing survives parsing. Unknown
    /// chunks are skipped; original last fmt/data choice and trailing-fragment behavior remain.</summary>
    private readonly record struct WaveFormat(ushort Tag, ushort Channels, uint Rate, ushort Bits);
    private static SpeechExportAudio WaveContainer(ReadOnlySpan<byte> pcm, int sampleRate, int channels)
    {
        var bytes = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(pcm.Length + 36)); "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), (ushort)channels); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)(sampleRate * channels * 2)); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), (ushort)(channels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16); "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)pcm.Length);
        pcm.CopyTo(bytes.AsSpan(44)); return new(bytes, "wav");
    }
    private static SpeechExportException Invalid() => new(SpeechExportFailure.InvalidAudio);
    // Native/JSONDecoder Swift strings reject final newlines. The NSString bridge differs
    // (documented separately); this follows the actual notebook and Anki decoding path.
    [GeneratedRegex("^lingrove-[a-f0-9]{64}\\.(wav|mp3)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex FilenamePattern();
}
