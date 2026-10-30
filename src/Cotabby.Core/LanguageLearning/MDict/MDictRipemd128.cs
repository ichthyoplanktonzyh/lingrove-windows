using System.Buffers.Binary;
using System.Numerics;

namespace Cotabby.Core.LanguageLearning.MDict;

// Adapted from MDictRIPEMD128.swift, Lingrove 3b53402 / Easydict b4eced7.
// Copyright © 2026 izual and Easydict contributors; GPL-3.0, see resources/ThirdParty/MDictReader.
public static class MDictRipemd128
{
    private static readonly int[] LeftIndex = [0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,7,4,13,1,10,6,15,3,12,0,9,5,2,14,11,8,3,10,14,4,9,15,8,1,2,7,0,6,13,11,5,12,1,9,11,10,0,8,12,4,13,3,7,15,14,5,6,2];
    private static readonly int[] RightIndex = [5,14,7,0,9,2,11,4,13,6,15,8,1,10,3,12,6,11,3,7,0,13,5,10,14,15,8,12,4,9,1,2,15,5,1,3,7,14,6,9,11,8,12,2,10,0,4,13,8,6,4,1,3,11,15,0,5,12,2,13,9,7,10,14];
    private static readonly int[] LeftShift = [11,14,15,12,5,8,7,9,11,13,14,15,6,7,9,8,7,6,8,13,11,9,7,15,7,12,15,9,11,7,13,12,11,13,6,7,14,9,13,15,14,8,13,6,5,12,7,5,11,12,14,15,14,15,9,8,9,14,5,6,8,6,5,12];
    private static readonly int[] RightShift = [8,9,9,11,13,15,15,5,7,7,8,11,14,14,12,6,9,13,15,7,12,8,9,11,7,7,12,7,6,15,13,11,9,7,15,11,8,6,6,14,12,13,5,14,13,13,7,5,15,5,8,11,14,14,6,14,6,9,12,9,12,5,15,8];
    private static readonly uint[] LeftK = [0, 0x5A827999, 0x6ED9EBA1, 0x8F1BBCDC];
    private static readonly uint[] RightK = [0x50A28BE6, 0x5C4DD124, 0x6D703EF3, 0];
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        int size = checked((data.Length + 9 + 63) / 64 * 64); var message = new byte[size]; data.CopyTo(message);
        message[data.Length] = 0x80; BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(size - 8), (ulong)data.Length * 8);
        uint h0 = 0x67452301, h1 = 0xEFCDAB89, h2 = 0x98BADCFE, h3 = 0x10325476;
        Span<uint> x = stackalloc uint[16];
        unchecked
        {
            for (int offset = 0; offset < size; offset += 64)
            {
                for (int i = 0; i < 16; i++) x[i] = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(offset + i * 4));
                uint a = h0, b = h1, c = h2, d = h3, aa = h0, bb = h1, cc = h2, dd = h3;
                for (int j = 0; j < 64; j++)
                {
                    int round = j / 16; uint t = BitOperations.RotateLeft(a + F(round, b, c, d) + x[LeftIndex[j]] + LeftK[round], LeftShift[j]);
                    a = d; d = c; c = b; b = t;
                    t = BitOperations.RotateLeft(aa + F(3 - round, bb, cc, dd) + x[RightIndex[j]] + RightK[round], RightShift[j]);
                    aa = dd; dd = cc; cc = bb; bb = t;
                }
                uint next = h1 + c + dd; h1 = h2 + d + aa; h2 = h3 + a + bb; h3 = h0 + b + cc; h0 = next;
            }
        }
        var result = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(result, h0); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), h1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), h2); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), h3); return result;
    }
    private static uint F(int round, uint x, uint y, uint z) => round switch
    { 0 => x ^ y ^ z, 1 => (x & y) | (~x & z), 2 => (x | ~y) ^ z, _ => (x & z) | (y & ~z) };
}
