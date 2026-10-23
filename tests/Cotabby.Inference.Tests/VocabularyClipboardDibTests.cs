using System.Buffers.Binary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests;

public sealed class VocabularyClipboardDibTests
{
    [Theory]
    [InlineData(40, false)]
    [InlineData(40, true)]
    [InlineData(108, false)]
    [InlineData(124, true)]
    public void WrapsLiteralRowsWithoutChangingOrderOrPixels(int header, bool topDown)
    {
        byte[] dib = VocabularyDibFixture.Image(header, topDown), original = dib.ToArray(); var bmp = VocabularyClipboardDib.Wrap(dib);
        Assert.Equal(new byte[] { 66, 77 }, bmp[..2]); Assert.Equal((uint)bmp.Length, U32(bmp, 2));
        Assert.Equal(new byte[4], bmp[6..10]); Assert.Equal((uint)(14 + header), U32(bmp, 10)); Assert.Equal(original, bmp[14..]);
        Array.Fill(dib, (byte)0); Assert.Equal(original, bmp[14..]);
    }
    [Fact]
    public void OmitsAllocationTailAndUsesTheAdmittedRowSize()
    {
        byte[] dib = [.. VocabularyDibFixture.Image(), .. Enumerable.Repeat((byte)7, 100)]; VocabularyDibFixture.U32(dib, 20, 0);
        var bmp = VocabularyClipboardDib.Wrap(dib); Assert.Equal(78, bmp.Length); Assert.Equal(24u, U32(bmp, 34)); Assert.DoesNotContain((byte)7, bmp.AsSpan(54).ToArray());
    }
    [Fact]
    public void ExtendsV4AlphaToV5WithoutChangingMasksOrPixelRows()
    {
        byte[] dib = VocabularyDibFixture.Image(108, alpha: true), bmp = VocabularyClipboardDib.Wrap(dib);
        Assert.Equal(124u, U32(bmp, 14)); Assert.Equal(138u, U32(bmp, 10)); Assert.Equal(162, bmp.Length);
        Assert.Equal(dib.AsSpan(4, 104).ToArray(), bmp.AsSpan(18, 104).ToArray()); Assert.Equal(4u, U32(bmp, 122));
        Assert.Equal(new byte[12], bmp.AsSpan(126, 12).ToArray()); Assert.Equal(dib[108..], bmp[138..]);
    }
    [Theory]
    [InlineData("short")]
    [InlineData("unknown-header")]
    [InlineData("truncated-v5")]
    [InlineData("negative-width")]
    [InlineData("zero-height")]
    [InlineData("min-height")]
    [InlineData("huge-width")]
    [InlineData("huge-pixels")]
    [InlineData("planes")]
    [InlineData("bit-depth")]
    [InlineData("rle")]
    [InlineData("bitfields-depth")]
    [InlineData("palette-overflow")]
    [InlineData("row-truncated")]
    [InlineData("declared-short")]
    [InlineData("declared-long")]
    [InlineData("linked-profile")]
    [InlineData("embedded-profile")]
    [InlineData("profile-offset")]
    [InlineData("profile-size")]
    [InlineData("overlapping-masks")]
    [InlineData("empty-mask")]
    [InlineData("noncontiguous-mask")]
    [InlineData("rgb-overlapping-alpha")]
    [InlineData("rgb-partial-alpha")]
    [InlineData("rgb-wrong-depth-alpha")]
    public void RejectsMalformedUnsupportedOrExternalLayoutsBeforeAFileEnvelope(string mode)
    {
        byte[] dib = VocabularyDibFixture.Image(124, alpha: true);
        switch (mode)
        {
            case "short": dib = dib[..39]; break;
            case "unknown-header": VocabularyDibFixture.U32(dib, 0, 52); break;
            case "truncated-v5": dib = dib[..123]; break;
            case "negative-width": VocabularyDibFixture.I32(dib, 4, -3); break;
            case "zero-height": VocabularyDibFixture.I32(dib, 8, 0); break;
            case "min-height": VocabularyDibFixture.I32(dib, 8, int.MinValue); break;
            case "huge-width": VocabularyDibFixture.I32(dib, 4, int.MaxValue); break;
            case "huge-pixels": VocabularyDibFixture.I32(dib, 4, 30000); VocabularyDibFixture.I32(dib, 8, 30000); break;
            case "planes": dib[12] = 2; break;
            case "bit-depth": dib[14] = 12; break;
            case "rle": VocabularyDibFixture.U32(dib, 16, 1); break;
            case "bitfields-depth": dib[14] = 24; break;
            case "palette-overflow": VocabularyDibFixture.U32(dib, 32, uint.MaxValue); break;
            case "row-truncated": dib = dib[..^1]; break;
            case "declared-short": VocabularyDibFixture.U32(dib, 20, 1); break;
            case "declared-long": VocabularyDibFixture.U32(dib, 20, uint.MaxValue); break;
            case "linked-profile": VocabularyDibFixture.U32(dib, 56, 0x4C494E4B); break;
            case "embedded-profile": VocabularyDibFixture.U32(dib, 56, 0x4D424544); break;
            case "profile-offset": VocabularyDibFixture.U32(dib, 112, 124); break;
            case "profile-size": VocabularyDibFixture.U32(dib, 116, 2); break;
            case "overlapping-masks": VocabularyDibFixture.U32(dib, 44, 0x00FF0000); break;
            case "empty-mask": VocabularyDibFixture.U32(dib, 40, 0); break;
            case "noncontiguous-mask": VocabularyDibFixture.U32(dib, 40, 0x00550000); break;
            case "rgb-overlapping-alpha": VocabularyDibFixture.U32(dib, 16, 0); VocabularyDibFixture.U32(dib, 52, 0x00FF0000); break;
            case "rgb-partial-alpha": VocabularyDibFixture.U32(dib, 16, 0); VocabularyDibFixture.U32(dib, 52, 0x55000000); break;
            case "rgb-wrong-depth-alpha": VocabularyDibFixture.U32(dib, 16, 0); dib[14] = 24; break;
        }
        Assert.Throws<InvalidDataException>(() => VocabularyClipboardDib.Wrap(dib));
    }
    [Fact]
    public void V5ClipboardOfferRequiresTheDeclaredV5Header()
    {
        Assert.Throws<InvalidDataException>(() => VocabularyClipboardDib.Wrap(VocabularyDibFixture.Image(), requiresV5: true));
        Assert.NotEmpty(VocabularyClipboardDib.Wrap(VocabularyDibFixture.Image(124), requiresV5: true));
    }
    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 16)]
    [InlineData(8, 256)]
    public void PaletteEntriesPrecedeTheDwordAlignedRows(int bits, int colors)
    {
        byte[] dib = new byte[40 + colors * 4 + 4]; VocabularyDibFixture.U32(dib, 0, 40); VocabularyDibFixture.I32(dib, 4, 1); VocabularyDibFixture.I32(dib, 8, 1);
        dib[12] = 1; dib[14] = (byte)bits; dib[40 + (colors - 1) * 4 + 2] = 255;
        var bmp = VocabularyClipboardDib.Wrap(dib); Assert.Equal((uint)(54 + colors * 4), U32(bmp, 10)); Assert.Equal(4u, U32(bmp, 34));
        VocabularyDibFixture.U32(dib, 32, (uint)(colors + 1)); Assert.Throws<InvalidDataException>(() => VocabularyClipboardDib.Wrap(dib));
    }
    [Fact]
    public void V3BitfieldsHaveThreeExternalMasksAndEnforceThePixelBitWidth()
    {
        byte[] dib = new byte[56]; VocabularyDibFixture.U32(dib, 0, 40); VocabularyDibFixture.I32(dib, 4, 1); VocabularyDibFixture.I32(dib, 8, 1);
        dib[12] = 1; dib[14] = 16; VocabularyDibFixture.U32(dib, 16, 3);
        VocabularyDibFixture.U32(dib, 40, 0xF800); VocabularyDibFixture.U32(dib, 44, 0x07E0); VocabularyDibFixture.U32(dib, 48, 0x001F);
        Assert.Equal(66u, U32(VocabularyClipboardDib.Wrap(dib), 10));
        VocabularyDibFixture.U32(dib, 40, 0xFFFF0000); Assert.Throws<InvalidDataException>(() => VocabularyClipboardDib.Wrap(dib));
    }
    [Fact]
    public void RejectsTheTwentyFourMiBBoundaryBeforeEnvelopeAllocation()
    {
        Assert.Throws<InvalidDataException>(() => VocabularyClipboardDib.Wrap(new byte[VocabularyClipboardImageReader.MaximumBytes + 1]));
    }
    [Fact]
    public void TheFileHeaderAlsoCountsTowardTheTransferLimit()
    {
        // Valid dimensions/stride and an optional optimization palette fill the whole input
        // budget. Adding the required 14-byte file header must not exceed the pipe budget.
        byte[] dib = new byte[VocabularyClipboardImageReader.MaximumBytes]; VocabularyDibFixture.U32(dib, 0, 40);
        VocabularyDibFixture.I32(dib, 4, 5461); VocabularyDibFixture.I32(dib, 8, 1535); dib[12] = 1; dib[14] = 24; VocabularyDibFixture.U32(dib, 32, 4086);
        Assert.Throws<InvalidDataException>(() => VocabularyClipboardDib.Wrap(dib));
    }
    [Theory]
    [InlineData(40, 0u, true)]
    [InlineData(108, 0x73524742u, true)]
    [InlineData(124, 0x73524742u, true)]
    [InlineData(124, 0x57696E20u, true)]
    [InlineData(108, 0u, false)]
    [InlineData(124, 0u, false)]
    public void ColorQueryFallbackRequiresIndependentSrgbEvidence(int header, uint colorSpace, bool expected)
    {
        byte[] dib = VocabularyDibFixture.Image(header); if (header >= 108) VocabularyDibFixture.U32(dib, 56, colorSpace);
        Assert.Equal(expected, VocabularyClipboardDib.HasProvenSrgbBitmap(VocabularyClipboardDib.Wrap(dib)));
    }
    [Theory]
    [InlineData("signature")]
    [InlineData("size")]
    [InlineData("reserved")]
    [InlineData("pixel-offset")]
    [InlineData("linked-profile")]
    [InlineData("profile-area")]
    [InlineData("unadapted-v4-alpha")]
    public void MalformedOrUnadaptedFilesCannotBypassAColorContextFailure(string mode)
    {
        byte[] bmp = VocabularyClipboardDib.Wrap(VocabularyDibFixture.Image(124));
        switch (mode)
        {
            case "signature": bmp[0] = 0; break;
            case "size": VocabularyDibFixture.U32(bmp, 2, 0); break;
            case "reserved": bmp[6] = 1; break;
            case "pixel-offset": VocabularyDibFixture.U32(bmp, 10, 1); break;
            case "linked-profile": VocabularyDibFixture.U32(bmp, 70, 0x4C494E4B); break;
            case "profile-area": VocabularyDibFixture.U32(bmp, 126, 124); break;
            case "unadapted-v4-alpha":
                byte[] dib = VocabularyDibFixture.Image(108, alpha: true); bmp = new byte[146]; bmp[0] = 66; bmp[1] = 77;
                VocabularyDibFixture.U32(bmp, 2, 146); VocabularyDibFixture.U32(bmp, 10, 122); dib.CopyTo(bmp, 14); break;
        }
        Assert.False(VocabularyClipboardDib.HasProvenSrgbBitmap(bmp));
    }
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
