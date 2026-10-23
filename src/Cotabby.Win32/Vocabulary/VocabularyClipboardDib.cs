using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// The transfer host wraps a detached packed Windows DIB in a bounded BMP file envelope for the
/// existing WIC preparation boundary. This owns no clipboard handle, decoded pixels, file IO or decoder.
/// Header/palette/mask/stride admission happens before allocation; linked/embedded profiles and
/// compressed/unknown layouts fail closed rather than letting a decoder follow external paths.
/// </summary>
internal static class VocabularyClipboardDib
{
    internal static byte[] Wrap(ReadOnlySpan<byte> dib, bool requiresV5 = false)
    {
        if (dib.Length < 40 || dib.Length > VocabularyClipboardImageReader.MaximumBytes) throw Invalid();
        uint header = U32(dib, 0);
        if (header is not (40 or 108 or 124) || header > dib.Length || (requiresV5 && header != 124)) throw Invalid();
        int width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        long height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(dib[8..]));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]); uint compression = U32(dib, 16);
        if (width is <= 0 or > 30000 || height is <= 0 or > 30000 || width * height > 100000000
            || BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]) != 1
            || bits is not (1 or 4 or 8 or 16 or 24 or 32) || compression is not (0 or 3)
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
        long stride = ((width * (long)bits + 31) / 32) * 4, pixels = stride * height;
        if (offset > dib.Length || pixels > dib.Length - offset) throw Invalid();
        uint declaredPixels = U32(dib, 20);
        if (declaredPixels != 0 && (declaredPixels < pixels || declaredPixels > dib.Length - offset)) throw Invalid();
        int extension = header == 108 && alpha != 0 ? 16 : 0;
        long length = 14 + extension + offset + pixels;
        if (length > VocabularyClipboardImageReader.MaximumBytes) throw Invalid();

        byte[] bmp = new byte[(int)length]; bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)length);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)(14 + extension + offset));
        // Discard unused HGLOBAL allocation tail; biSizeImage describes only admitted pixel rows.
        dib[..(int)header].CopyTo(bmp.AsSpan(14));
        dib[(int)header..(int)(offset + pixels)].CopyTo(bmp.AsSpan(14 + (int)header + extension));
        if (extension != 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 124);
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(122), 4); // V5 perceptual intent; no profile data.
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(34), (uint)pixels); return bmp;
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
