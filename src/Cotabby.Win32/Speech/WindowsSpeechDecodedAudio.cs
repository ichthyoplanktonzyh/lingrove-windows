namespace Cotabby.Win32.Speech;

/// <summary>The codec transfers validated, detached interleaved Float32 packets to one playback
/// operation. Encoded audio can be stereo, unlike the helper's mono SpeechPCMChunk contract; this
/// Windows-only value preserves channels rather than silently downmixing review audio. No native
/// reference, stream or user text escapes the decoder owner. Packet storage is private and callers
/// receive read-only views; the codec relinquishes its arrays when constructing this value.</summary>
internal sealed class WindowsSpeechDecodedAudio
{
    private readonly byte[][] _packets;
    internal int SampleRate { get; }
    internal int Channels { get; }
    internal long Frames { get; }
    internal double Duration { get; }
    internal WindowsSpeechDecodedAudio(int sampleRate, int channels, long frames, double duration, byte[][] ownedPackets)
    { SampleRate = sampleRate; Channels = channels; Frames = frames; Duration = duration; _packets = ownedPackets; }
    internal IEnumerable<ReadOnlyMemory<byte>> Packets => _packets.Select(packet => (ReadOnlyMemory<byte>)packet);
}
