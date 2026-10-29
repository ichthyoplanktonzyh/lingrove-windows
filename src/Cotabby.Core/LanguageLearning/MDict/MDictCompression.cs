using System.IO.Compression;

namespace Cotabby.Core.LanguageLearning.MDict;

// Adapted from MDictBinary.swift, Lingrove 3b53402 (Easydict b4eced7).
// Copyright © 2026 izual and Easydict contributors; GPL-3.0, see resources/ThirdParty/MDictReader.
public static class MDictCompression
{
    internal const int MaximumBlock = 32 * 1024 * 1024;
    public static byte[] DecryptKeyInfo(ReadOnlySpan<byte> input)
    {
        var result = input.ToArray(); if (result.Length <= 8) return result;
        Span<byte> material = stackalloc byte[8]; material.Clear(); input.Slice(4, 4).CopyTo(material); material[4] = 0x95; material[5] = 0x36;
        var key = MDictRipemd128.Hash(material); byte previous = 0x36;
        for (int i = 8; i < result.Length; i++)
        {
            byte original = input[i]; byte swapped = (byte)((original >> 4) | (original << 4));
            result[i] = (byte)(swapped ^ (byte)(i - 8) ^ key[(i - 8) % 16] ^ previous); previous = original;
        }
        return result;
    }
    public static byte[] Expand(ReadOnlySpan<byte> block, int expected)
    {
        if (block.Length < 8 || expected < 0 || expected > MaximumBlock) throw Invalid();
        var output = new byte[expected]; var payload = block[8..];
        switch (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(block))
        {
            case 0:
                if (payload.Length != expected) throw Invalid(); payload.CopyTo(output); return output;
            case 0x02000000:
                if (expected == 0 || payload.Length == 0) throw Invalid();
                using (var input = new MemoryStream(payload.ToArray(), false))
                using (var zip = new ZLibStream(input, CompressionMode.Decompress))
                {
                    int offset = 0;
                    while (offset < output.Length) { int n = zip.Read(output, offset, output.Length - offset); if (n == 0) throw Invalid(); offset += n; }
                    if (zip.ReadByte() != -1) throw Invalid(); return output;
                }
            case 0x01000000: return ExpandLzo(payload.ToArray(), output);
            default: throw Invalid();
        }
    }
    private static byte[] ExpandLzo(byte[] input, byte[] output)
    {
        int ip = 0, op = 0, state = 0;
        int Read() { if (ip >= input.Length) throw Invalid(); return input[ip++]; }
        int Read16() { int a = Read(); return a | (Read() << 8); }
        int Extended(int basis)
        {
            int length = basis;
            while (true) { int value = Read(); if (value != 0) return checked(length + value); length = checked(length + 255); }
        }
        void Literals(int count)
        {
            if (count < 0 || count > input.Length - ip || count > output.Length - op) throw Invalid();
            input.AsSpan(ip, count).CopyTo(output.AsSpan(op)); ip += count; op += count;
        }
        int instruction = Read();
        if (instruction >= 22) { Literals(instruction - 17); state = 4; }
        else if (instruction >= 18) { int n = instruction - 17; Literals(n); state = n; }
        while (true)
        {
            if (ip > 1 || state > 0) instruction = Read();
            int distance, length, next;
            if (instruction >= 64) { int tail = Read(); distance = (tail << 3) + ((instruction >> 2) & 7) + 1; length = (instruction >> 5) + 1; next = instruction & 3; }
            else if (instruction >= 32) { length = (instruction & 31) + 2; if (length == 2) length = checked(length + Extended(31)); int pair = Read16(); distance = (pair >> 2) + 1; next = pair & 3; }
            else if (instruction >= 16)
            {
                length = (instruction & 7) + 2; if (length == 2) length = checked(length + Extended(7)); int pair = Read16();
                int basis = ((instruction & 8) << 11) + (pair >> 2);
                if (basis == 0) { if (length != 3 || op != output.Length) throw Invalid(); return output; }
                distance = basis + 0x4000; next = pair & 3;
            }
            else if (state == 0)
            {
                int n = instruction + 3; if (n == 3) n = checked(n + Extended(15)); Literals(n);
                if (ip >= input.Length) throw Invalid(); state = 4; continue;
            }
            else
            {
                int tail = Read(); distance = state == 4 ? 0x0801 + (instruction >> 2) + (tail << 2) : (instruction >> 2) + (tail << 2) + 1;
                length = state == 4 ? 3 : 2; next = instruction & 3;
            }
            if (distance <= 0 || distance > op || length < 0 || length > output.Length - op) throw Invalid();
            for (int i = 0; i < length; i++) output[op + i] = output[op - distance + i];
            op += length; if (next > 0) Literals(next); state = next;
        }
    }
    private static InvalidDataException Invalid() => new("Invalid dictionary block.");
}
