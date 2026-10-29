using System.Buffers.Binary;
using System.Runtime.Versioning;
using Cotabby.Win32.Speech;

namespace Cotabby.App.Wpf.Tests;

[SupportedOSPlatform("windows10.0")]
public sealed class WindowsEncodedSpeechPlayerTests
{
    [Fact]
    public void StereoSamplesRemainSeparateAtNormalSpeedAndCancellationIsObserved()
    {
        var bytes = new byte[8000 * 2 * 4];
        for (int i = 0; i < 8000; i++) { BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8), .25f); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8 + 4), -.5f); }
        var decoded = new WindowsSpeechDecodedAudio(8000, 2, 8000, 1, [bytes]);
        using var audio = WindowsEncodedSpeechPlayer.BuildSamples(decoded, 1, CancellationToken.None);
        Assert.Equal(bytes, audio.ToArray());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsEncodedSpeechPlayer.BuildSamples(decoded, .75, cancellation.Token));
        using var slower = WindowsEncodedSpeechPlayer.BuildSamples(decoded, .75, CancellationToken.None);
        Assert.InRange(slower.Length / 8, 10660, 10672);
    }
}
