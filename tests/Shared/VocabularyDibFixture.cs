using System.Buffers.Binary;

/// <summary>Literal 3x2 RGB rows, independent of the product's stride/header parser; no clipboard.</summary>
internal static class VocabularyDibFixture
{
    internal static byte[] Image(int header = 40, bool topDown = false, bool alpha = false)
    {
        byte[] dib = new byte[header + 24]; U32(dib, 0, (uint)header); I32(dib, 4, 3); I32(dib, 8, topDown ? -2 : 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1); BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), alpha ? (ushort)32 : (ushort)24);
        U32(dib, 20, 24);
        if (header >= 108) U32(dib, 56, 0x73524742);
        if (header == 124) U32(dib, 108, 4);
        if (alpha)
        { U32(dib, 16, 3); U32(dib, 40, 0x00FF0000); U32(dib, 44, 0x0000FF00); U32(dib, 48, 0x000000FF); U32(dib, 52, 0xFF000000); }
        byte[] red = alpha ? [0, 0, 255, 127, 0, 0, 255, 127, 0, 0, 255, 127] : [0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 0];
        byte[] blue = alpha ? [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255] : [255, 0, 0, 255, 0, 0, 255, 0, 0, 0, 0, 0];
        (topDown ? red : blue).CopyTo(dib, header); (topDown ? blue : red).CopyTo(dib, header + 12); return dib;
    }
    internal static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    internal static void I32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
}
