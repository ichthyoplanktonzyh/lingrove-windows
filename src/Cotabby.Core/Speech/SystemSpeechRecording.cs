namespace Cotabby.Core.Speech;

/// <summary>A system speech callback sink hands off one detached Float32 recording. The sink
/// owns capture limits; the export policy owns WAV admission. This value preserves raw bytes/rate
/// without imposing PCMChunk's separate block-size/amplitude rules or retaining a system voice.</summary>
public sealed class SystemSpeechRecording
{
    private readonly byte[] _bytes;
    public SystemSpeechRecording(ReadOnlySpan<byte> bytes, int sampleRate) { _bytes = bytes.ToArray(); SampleRate = sampleRate; }
    public int SampleRate { get; }
    internal ReadOnlySpan<byte> Bytes => _bytes;
    public int ByteCount => _bytes.Length;
    public byte[] CopyBytes() => (byte[])_bytes.Clone();
}
