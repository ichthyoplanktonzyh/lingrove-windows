namespace Cotabby.Core.Speech;

/// <summary>A generator delivers one encoded response or ordered PCM blocks. These detached
/// values carry no playback device, runtime or view; a request-owned export/playback receiver
/// decides when to consume them. Encoded admission intentionally belongs to the receiving policy.</summary>
public abstract class SpeechAudioChunk
{
    private SpeechAudioChunk() { }
    public sealed class Encoded : SpeechAudioChunk
    {
        private readonly byte[] _bytes;
        public Encoded(ReadOnlySpan<byte> bytes) { _bytes = bytes.ToArray(); }
        internal ReadOnlySpan<byte> Bytes => _bytes;
        public int ByteCount => _bytes.Length;
        public byte[] CopyBytes() => (byte[])_bytes.Clone();
    }
    public sealed class Pcm : SpeechAudioChunk
    {
        public Pcm(SpeechPCMChunk value) { ArgumentNullException.ThrowIfNull(value); Value = value; }
        public SpeechPCMChunk Value { get; }
    }
}
