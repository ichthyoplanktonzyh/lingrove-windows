using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// The transfer host wraps a detached packed Windows DIB in a bounded BMP file envelope for the
/// existing WIC preparation boundary. This owns no clipboard handle, decoded pixels, file IO or decoder.
/// Header/palette/mask/stride admission happens before allocation; linked/embedded profiles and
/// unsupported/unknown layouts fail closed rather than letting a decoder follow external paths.
/// </summary>
internal static class VocabularyClipboardDib
{
    // One parsed layout serves wrapping and the decoder's narrow color-evidence check. Keeping
    // them together prevents a looser second parser from admitting a profile or different rows.
    private readonly record struct Layout(uint Header, long Offset, long Pixels, int Extension, int Length);
    private static Layout ReadLayout(ReadOnlySpan<byte> dib, bool requiresV5)
    {
        if (dib.Length < 40 || dib.Length > VocabularyClipboardImageReader.MaximumBytes) throw Invalid();
        uint header = U32(dib, 0);
        if (header is not (40 or 108 or 124) || header > dib.Length || (requiresV5 && header != 124)) throw Invalid();
        int width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        long height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(dib[8..]));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]); uint compression = U32(dib, 16);
        if (width is <= 0 or > 30000 || height is <= 0 or > 30000 || width * height > 100000000
            || BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]) != 1
            || bits is not (1 or 4 or 8 or 16 or 24 or 32) || compression is not (0 or 1 or 2 or 3)
            || (compression == 1 && bits != 8) || (compression == 2 && bits != 4)
            || (compression is 1 or 2 && BinaryPrimitives.ReadInt32LittleEndian(dib[8..]) < 0)
            || (compression == 3 && bits is not (16 or 32))) throw Invalid();

        // V4/V5 profile-linked color spaces can contain a filename, including a network path.
        // Only inline calibrated/sRGB/Windows RGB values are admitted, with no V5 profile area.
        if (header >= 108 && U32(dib, 56) is not (0 or 0x73524742 or 0x57696E20)) throw Invalid();
        if (header == 124 && (U32(dib, 112) != 0 || U32(dib, 116) != 0)) throw Invalid();

        long extraMasks = compression == 3 && header == 40 ? 12 : 0;
        if (header + extraMasks > dib.Length) throw Invalid();
        uint alpha = header >= 108 ? U32(dib, 52) : 0;
        // WIC's BMP codec preserves alpha only in 32-bit V5 headers. Refuse 16-bit alpha and
        // extend a V4 header below so a declared 32-bit alpha channel cannot silently disappear.
        if (alpha != 0 && bits != 32) throw Invalid();
        if (compression == 3)
        {
            uint red = U32(dib, 40), green = U32(dib, 44), blue = U32(dib, 48);
            uint allowed = bits == 32 ? uint.MaxValue : (1u << bits) - 1;
            if (!Mask(red, allowed) || !Mask(green, allowed) || !Mask(blue, allowed)
                || (alpha != 0 && !Mask(alpha, allowed)) || (red & green) != 0 || (red & blue) != 0
                || (green & blue) != 0 || ((red | green | blue) & alpha) != 0) throw Invalid();
        }
        // BI_RGB fixes the B/G/R byte layout. V4/V5 can still declare alpha separately; admit
        // only the canonical spare byte, never an overlapping, partial or wrong-depth mask.
        else if (alpha != 0 && (bits != 32 || alpha != 0xFF000000)) throw Invalid();
        uint colors = U32(dib, 32);
        if (bits <= 8 && colors == 0) colors = 1u << bits;
        if (bits < 32 && colors > (1u << bits)) throw Invalid();
        long offset = header + extraMasks + 4L * colors;
        uint declaredPixels = U32(dib, 20);
        if (offset > dib.Length) throw Invalid();
        long stride = ((width * (long)bits + 31) / 32) * 4;
        long pixels = compression is 1 or 2 ? declaredPixels : stride * height;
        if (pixels > dib.Length - offset || (compression is 1 or 2 && pixels == 0)) throw Invalid();
        if (compression is not (1 or 2) && declaredPixels != 0 && (declaredPixels < pixels || declaredPixels > dib.Length - offset)) throw Invalid();
        if (compression is 1 or 2) ValidateRle(dib.Slice((int)offset, (int)pixels), width, (int)height, bits, colors);
        int extension = header == 108 && alpha != 0 ? 16 : 0;
        long length = 14 + extension + offset + pixels;
        if (length > VocabularyClipboardImageReader.MaximumBytes) throw Invalid();
        return new(header, offset, pixels, extension, (int)length);
    }
    // Validate every command before WIC sees the compressed stream. Never allow a run or
    // delta outside the bitmap or an index outside the admitted palette.
    private static void ValidateRle(ReadOnlySpan<byte> data, int width, int height, int bits, uint colors)
    {
        int position = 0, x = 0, y = 0;
        while (position + 2 <= data.Length)
        {
            int count = data[position++], value = data[position++];
            if (count != 0)
            {
                if (y >= height || count > width - x || (bits == 8 ? value >= colors : (value >> 4) >= colors || (count > 1 && (value & 15) >= colors))) throw Invalid();
                x += count; continue;
            }
            if (value == 0) { if (y >= height) throw Invalid(); x = 0; y++; continue; }
            if (value == 1) { if (position != data.Length) throw Invalid(); return; }
            if (value == 2)
            {
                if (position + 2 > data.Length || y >= height) throw Invalid();
                x += data[position++]; y += data[position++];
                if (x > width || y >= height) throw Invalid();
                continue;
            }
            int bytes = bits == 8 ? value : (value + 1) / 2, padded = (bytes + 1) & ~1;
            if (y >= height || value > width - x || position + padded > data.Length) throw Invalid();
            for (int i = 0; i < value; i++)
            {
                int index = bits == 8 ? data[position + i] : (i % 2 == 0 ? data[position + i / 2] >> 4 : data[position + i / 2] & 15);
                if (index >= colors) throw Invalid();
            }
            position += padded; x += value;
        }
        throw Invalid();
    }
    internal static byte[] Wrap(ReadOnlySpan<byte> dib, bool requiresV5 = false)
    {
        var layout = ReadLayout(dib, requiresV5);
        byte[] bmp = new byte[layout.Length]; bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)layout.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)(14 + layout.Extension + layout.Offset));
        // Discard unused HGLOBAL allocation tail; biSizeImage describes only admitted pixel rows.
        dib[..(int)layout.Header].CopyTo(bmp.AsSpan(14));
        dib[(int)layout.Header..(int)(layout.Offset + layout.Pixels)].CopyTo(bmp.AsSpan(14 + (int)layout.Header + layout.Extension));
        if (layout.Extension != 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 124);
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(122), 4); // V5 perceptual intent; no profile data.
        }
        if (layout.Header >= 108 && U32(dib, 16) == 0 && U32(dib, 52) != 0)
        {
            // WIC ignores BI_RGB alpha even with a valid V5 alpha mask. BI_RGB fixes B/G/R
            // to their standard bytes, so declare that same layout as BI_BITFIELDS instead.
            // This changes only the envelope's masks/compression, never the admitted pixel rows.
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(30), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(54), 0x00FF0000);
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(58), 0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(62), 0x000000FF);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(34), (uint)layout.Pixels); return bmp;
    }
    /// <summary>
    /// Independent positive evidence for WIC BMP codecs that cannot enumerate color contexts.
    /// V3 carries no profile; V4/V5 must explicitly name sRGB/Windows RGB and have no profile area.
    /// Calibrated/unknown sources remain failures when their color context cannot be obtained.
    /// </summary>
    internal static bool HasProvenSrgbBitmap(ReadOnlySpan<byte> bmp)
    {
        if (bmp.Length < 54 || bmp.Length > VocabularyClipboardImageReader.MaximumBytes || bmp[0] != 'B' || bmp[1] != 'M'
            || U32(bmp, 2) != bmp.Length || U32(bmp, 6) != 0) return false;
        try
        {
            var layout = ReadLayout(bmp[14..], requiresV5: false);
            return layout.Extension == 0 && U32(bmp, 10) == 14 + layout.Offset
                && (layout.Header == 40 || U32(bmp, 70) is 0x73524742 or 0x57696E20);
        }
        catch (InvalidDataException) { return false; }
    }
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static bool Mask(uint value, uint allowed)
    {
        if (value == 0 || (value & ~allowed) != 0) return false;
        uint shifted = value >> BitOperations.TrailingZeroCount(value);
        return (shifted & unchecked(shifted + 1)) == 0;
    }
    private static InvalidDataException Invalid() => new("The clipboard bitmap could not be verified.");
}
