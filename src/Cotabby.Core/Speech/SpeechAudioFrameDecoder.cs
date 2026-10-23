using System.Buffers.Binary;

namespace Cotabby.Core.Speech;

/// <summary>One serial pipe reader owns the bounded undecoded bytes. Append accepts small pipe
/// fragments; Next returns at most one detached header/payload so its caller can apply playback
/// backpressure. Invalid/incomplete frames leave the buffer intact. No process/device is owned.</summary>
public sealed partial class SpeechAudioFrameDecoder
{
    private readonly MemoryStream _buffer = new();
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length > 16_384 || _buffer.Length + data.Length > SpeechPCMChunk.MaximumBytes + 20_484) throw SpeechPCMChunk.Invalid();
        _buffer.Position = _buffer.Length; _buffer.Write(data);
    }
    public SpeechAudioFrame? Next()
    {
        int count = checked((int)_buffer.Length); if (count < 4) return null;
        uint size = BinaryPrimitives.ReadUInt32BigEndian(_buffer.GetBuffer());
        if (size is < 1 or > 4_096) throw SpeechPCMChunk.Invalid();
        int length = (int)size; if (count < 4 + length) return null;
        var header = ReadHeader(_buffer.GetBuffer().AsMemory(4, length)); long payloadBytes = header.PayloadBytes ?? 0;
        if (payloadBytes is < 0 or > SpeechPCMChunk.MaximumBytes || header.Event != "chunk" && payloadBytes != 0) throw SpeechPCMChunk.Invalid();
        int end = 4 + length + (int)payloadBytes; if (count < end) return null;
        var frame = new SpeechAudioFrame(header, _buffer.GetBuffer().AsSpan(4 + length, (int)payloadBytes));
        // Consume only after decoding, bounds admission and the complete payload copy succeed.
        // A retry after malformed JSON must see the same bytes, not silently skip into PCM.
        Buffer.BlockCopy(_buffer.GetBuffer(), end, _buffer.GetBuffer(), 0, count - end);
        _buffer.SetLength(count - end); _buffer.Position = _buffer.Length; return frame;
    }
    public void Finish() { if (_buffer.Length != 0) throw SpeechPCMChunk.Invalid(); }
}

/// <summary>Immutable helper metadata. Framing decodes shape only; the later runtime must verify
/// protocol version, request identity, sequence, format/channels and semantic sample counts.</summary>
public sealed class SpeechAudioFrameHeader(string @event, long? protocolVersion, string? id, long? sequence,
    long? sampleRate, long? samples, long? payloadBytes, bool? ok, string? format, long? channels)
{
    public string Event { get; } = @event;
    public long? ProtocolVersion { get; } = protocolVersion;
    public string? Id { get; } = id;
    public long? Sequence { get; } = sequence;
    public long? SampleRate { get; } = sampleRate;
    public long? Samples { get; } = samples;
    public long? PayloadBytes { get; } = payloadBytes;
    public bool? Ok { get; } = ok;
    public string? Format { get; } = format;
    public long? Channels { get; } = channels;
}
/// <summary>A frame owns its payload independently of the reader's reusable pipe buffer. Raw
/// payload is not automatically valid Float32: SpeechPCMChunk performs that separate admission.</summary>
public sealed class SpeechAudioFrame
{
    private readonly byte[] _payload;
    internal SpeechAudioFrame(SpeechAudioFrameHeader header, ReadOnlySpan<byte> payload) { Header = header; _payload = payload.ToArray(); }
    public SpeechAudioFrameHeader Header { get; }
    public int PayloadByteCount => _payload.Length;
    public byte[] CopyPayload() => (byte[])_payload.Clone();
}
