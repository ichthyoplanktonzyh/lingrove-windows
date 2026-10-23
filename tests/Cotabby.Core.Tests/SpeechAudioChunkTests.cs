using Cotabby.Core.Speech;

namespace Cotabby.Core.Tests;

public sealed class SpeechAudioChunkTests
{
    [Fact]
    public void DetachedProducerValuesPreserveEncodedAndRawRecordingBytes()
    {
        byte[] bytes = [0, 0, 128, 63]; var encoded = new SpeechAudioChunk.Encoded(bytes);
        var recording = new SystemSpeechRecording(bytes, 24_000); var pcm = new SpeechPCMChunk(bytes, 24_000); var chunk = new SpeechAudioChunk.Pcm(pcm);
        Array.Fill(bytes, (byte)255); Array.Fill(encoded.CopyBytes(), (byte)255); Array.Fill(recording.CopyBytes(), (byte)255);
        Assert.Equal(new byte[] { 0, 0, 128, 63 }, encoded.CopyBytes()); Assert.Equal(4, encoded.ByteCount);
        Assert.Equal(new byte[] { 0, 0, 128, 63 }, recording.CopyBytes()); Assert.Equal(4, recording.ByteCount); Assert.Equal(24_000, recording.SampleRate);
        Assert.Same(pcm, chunk.Value); Assert.Equal(new byte[] { 0, 0, 128, 63 }, chunk.Value.CopyBytes());
        // System recordings are not helper PCM blocks: their bytes reach WAV admission later.
        var raw = new SystemSpeechRecording(new byte[3], 1); Assert.Equal(3, raw.ByteCount); Assert.Equal(1, raw.SampleRate);
    }
    [Fact]
    public void OversizedFrozenValuesAreRejectedWithoutAnotherFullBodyAllocation()
    {
        var bytes = new byte[SpeechExportPolicy.MaximumBytes + 1];
        var recording = new SystemSpeechRecording(bytes, 24_000); var encoded = new SpeechAudioChunk.Encoded(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(SpeechExportFailure.InvalidAudio, Assert.Throws<SpeechExportException>(() => SpeechExportPolicy.Wav(recording)).Failure);
        Assert.Equal(SpeechExportFailure.ResponseTooLarge, Assert.Throws<SpeechExportException>(() => SpeechExportPolicy.Encoded(encoded)).Failure);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Leave ample room for exception/assertion machinery while catching a 12 MiB copy.
        // The owning values are created before measurement; their public snapshot copy is
        // intentional, while the receiving policy must admit size before output allocation.
        Assert.InRange(allocated, 0, SpeechExportPolicy.MaximumBytes / 4);
    }

}
