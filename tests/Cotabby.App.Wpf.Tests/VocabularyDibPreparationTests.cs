using System.Windows.Media;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real WIC normalization/PNG decode of literal DIB rows, never a runner clipboard.</summary>
[Collection("WPF views")]
public sealed class VocabularyDibPreparationTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public Task CompressedClipboardRowsReachTheExistingPngPreparation(int bits) => dispatcher.RunAsync(async () =>
    {
        byte[] commands = bits == 4 ? [3, 0x11, 0, 0, 3, 0x22, 0, 0, 0, 1] : [3, 1, 0, 0, 3, 2, 0, 0, 0, 1];
        byte[] dib = new byte[40 + 16 * 4 + commands.Length]; VocabularyDibFixture.U32(dib, 0, 40);
        VocabularyDibFixture.I32(dib, 4, 3); VocabularyDibFixture.I32(dib, 8, 2);
        dib[12] = 1; dib[14] = (byte)bits; VocabularyDibFixture.U32(dib, 16, bits == 8 ? 1u : 2u);
        VocabularyDibFixture.U32(dib, 20, (uint)commands.Length); VocabularyDibFixture.U32(dib, 32, 16);
        dib[44] = 255; dib[50] = 255; commands.CopyTo(dib, 104);
        await using var archive = new VocabularyArchive((string?)null);
        var prepared = await archive.PrepareImageAsync(VocabularyClipboardDib.Wrap(dib));
        var image = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(prepared.CopyBytes(), prepared.Reference));
        byte[] pixels = new byte[24]; image.CopyPixels(pixels, 12, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels[12..16]);
    });
    [Fact]
    public Task RawWicBmpFrameCanBeOpenedBeforeArchiveNormalization() => dispatcher.Run(() =>
    {
        // Exercise the native boundary directly so a codec/HRESULT regression identifies the
        // failing WIC call instead of being hidden behind the product's fixed import message.
        using var frame = new WindowsVocabularyImageFrame(VocabularyClipboardDib.Wrap(VocabularyDibFixture.Image()));
        Assert.Equal(new Guid("0af1d87e-fcfe-4188-bdeb-a7906471cbe3"), frame.Container);
        Assert.Equal(3, frame.Width); Assert.Equal(2, frame.Height);
        var output = frame.CopyPixels(3, 2, default); Assert.Equal(new byte[] { 0, 0, 255, 255 }, output.Pixels[..4]);
    });
    [Theory]
    [InlineData(40, false)]
    [InlineData(40, true)]
    [InlineData(108, false)]
    [InlineData(108, true)]
    [InlineData(124, false)]
    [InlineData(124, true)]
    public Task NormalizesRowsToTheSameVisibleRedTopAndBlueBottom(int header, bool topDown) => dispatcher.RunAsync(async () =>
    {
        await using var archive = new VocabularyArchive((string?)null);
        byte[] bmp = VocabularyClipboardDib.Wrap(VocabularyDibFixture.Image(header, topDown));
        var prepared = await archive.PrepareImageAsync(bmp); Assert.Equal(3, prepared.Reference.Width); Assert.Equal(2, prepared.Reference.Height);
        var image = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(prepared.CopyBytes(), prepared.Reference));
        Assert.True(image.IsFrozen); Assert.Equal(PixelFormats.Bgra32, image.Format); byte[] pixels = new byte[24]; image.CopyPixels(pixels, 12, 0);
        for (int column = 0; column < 3; column++)
        {
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels.AsSpan(column * 4, 4).ToArray());
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.AsSpan(12 + column * 4, 4).ToArray());
        }
    });
    [Theory]
    [InlineData(108, 0u)]
    [InlineData(108, 3u)]
    [InlineData(124, 0u)]
    [InlineData(124, 3u)]
    public Task ExtendedHeadersPreserveStraightAlphaThroughActualPngPreparation(int header, uint compression) => dispatcher.RunAsync(async () =>
    {
        await using var archive = new VocabularyArchive((string?)null);
        byte[] dib = VocabularyDibFixture.Image(header, alpha: true); VocabularyDibFixture.U32(dib, 16, compression);
        var prepared = await archive.PrepareImageAsync(VocabularyClipboardDib.Wrap(dib, requiresV5: header == 124));
        var image = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(prepared.CopyBytes(), prepared.Reference));
        byte[] pixels = new byte[24]; image.CopyPixels(pixels, 12, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 127 }, pixels[..4]); Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels[12..16]);
    });
    [Theory]
    [InlineData(40)]
    [InlineData(108)]
    [InlineData(124)]
    public Task UnusedHighBytesAreOpaqueWhenNoAlphaMaskIsDeclared(int header) => dispatcher.RunAsync(async () =>
    {
        byte[] source = VocabularyDibFixture.Image(124, alpha: true), dib = new byte[header + 24];
        source.AsSpan(0, header).CopyTo(dib); source.AsSpan(124, 24).CopyTo(dib.AsSpan(header));
        VocabularyDibFixture.U32(dib, 0, (uint)header); VocabularyDibFixture.U32(dib, 16, 0);
        if (header >= 108) VocabularyDibFixture.U32(dib, 52, 0);
        await using var archive = new VocabularyArchive((string?)null);
        var prepared = await archive.PrepareImageAsync(VocabularyClipboardDib.Wrap(dib));
        var image = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(prepared.CopyBytes(), prepared.Reference));
        byte[] pixels = new byte[24]; image.CopyPixels(pixels, 12, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[..4]); Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels[12..16]);
    });
}
