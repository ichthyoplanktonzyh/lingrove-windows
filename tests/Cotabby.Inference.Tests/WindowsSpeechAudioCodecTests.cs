using System.Buffers.Binary;
using System.Runtime.Versioning;
using Cotabby.Core.Speech;
using Cotabby.Win32.Speech;

namespace Cotabby.Inference.Tests;

/// <summary>Windows CI executes the actual memory-stream Media Foundation decoder without an
/// audio device. The MP3 is our generated tone; WAVs are built in memory. These check codecs and
/// joined apartments, not speaker behavior, installed voices or model-backed generation.</summary>
[SupportedOSPlatform("windows10.0")]
public sealed class WindowsSpeechAudioCodecTests
{
    [Fact]
    public async Task ActualWaveAndMp3DecodingPreservesStereoAndCountsReturnedFrames()
    {
        var codec = new WindowsSpeechAudioCodec();
        byte[] wave = Wave(24000, 2, 24000);
        var decoded = await codec.DecodeAsync(wave);
        Assert.Equal(24000, decoded.SampleRate); Assert.Equal(2, decoded.Channels); Assert.Equal(24000, decoded.Frames); Assert.Equal(1, decoded.Duration);
        Assert.Equal(3, decoded.Packets.Count());
        byte[] values = decoded.Packets.SelectMany(packet => packet.ToArray()).ToArray();
        Assert.Equal(24000 * 2 * 4, values.Length);
        for (int index = 0; index < 24000 * 2; index++)
        {
            float expected = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + index * 2)) / 32768f;
            float actual = BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(index * 4));
            Assert.InRange(MathF.Abs(actual - expected), 0, 1f / 32768);
        }
        Assert.Equal(decoded.Duration, await codec.DurationAsync(wave, CancellationToken.None));
        byte[] mp3 = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "generated-speech-tone.mp3"));
        Assert.Equal("f3d1b676b534c6ed7e4586bf7bf2005e476b58490d1b34a59e3aba72855ca574", SpeechExportPolicy.Digest(mp3));
        var compressed = await codec.DecodeAsync(mp3);
        Assert.Equal(24000, compressed.SampleRate); Assert.Equal(1, compressed.Channels);
        Assert.InRange(compressed.Duration, .15, .4); Assert.Equal((double)compressed.Frames / compressed.SampleRate, compressed.Duration);
        var samples = compressed.Packets.SelectMany(packet => packet.ToArray()).ToArray(); double energy = 0;
        for (int offset = 0; offset < samples.Length; offset += 4) { float value = BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(offset)); energy += value * value; }
        Assert.InRange(Math.Sqrt(energy / (samples.Length / 4)), .04, .15);
    }

    [Fact]
    public async Task InvalidOrOverlongMediaAndCancelledAdmissionCannotPoisonTheNextDecode()
    {
        var codec = new WindowsSpeechAudioCodec();
        byte[] corrupt = new byte[44]; "RIFF"u8.CopyTo(corrupt); "WAVE"u8.CopyTo(corrupt.AsSpan(8));
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var error = await Assert.ThrowsAsync<SpeechException>(() => codec.DecodeAsync(corrupt));
            Assert.Equal(SpeechFailure.InvalidAudio, error.Failure);
        }
        await Assert.ThrowsAsync<SpeechException>(() => codec.DecodeAsync(Wave(8000, 1, 8000 * 90 + 1)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => codec.DecodeAsync(Wave(8000, 1, 100), cancellation.Token));
        Assert.Equal(100, (await codec.DecodeAsync(Wave(8000, 1, 100))).Frames);
    }

    [Fact]
    public async Task CancellingAnAcceptedApartmentJoinsHeldWorkAndItsActualThread()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(); Thread? owner = null;
        Task<int> work = WindowsSpeechMta.RunAsync(() =>
        {
            owner = Thread.CurrentThread; Assert.Equal(ApartmentState.MTA, owner.GetApartmentState());
            entered.Set(); release.Wait(); return 7;
        }, cancellation.Token);
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(15))));
            cancellation.Cancel(); Assert.False(work.IsCompleted);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.False(owner!.IsAlive);
        }
        finally { release.Set(); try { await work; } catch (OperationCanceledException) { } }
    }

    private static byte[] Wave(int rate, int channels, int frames)
    {
        var wave = new byte[44 + frames * channels * 2];
        "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8); "WAVEfmt "u8.CopyTo(wave.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), (short)channels); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), rate * channels * 2); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16); "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), wave.Length - 44);
        for (int frame = 0; frame < frames; frame++)
        for (int channel = 0; channel < channels; channel++)
        {
            short value = (short)(Math.Sin(frame * .071 + channel * 1.2) * (channel == 0 ? 16000 : 8000));
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(44 + (frame * channels + channel) * 2), value);
        }
        return wave;
    }
}
