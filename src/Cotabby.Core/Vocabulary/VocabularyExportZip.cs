using System.Buffers.Binary;
using System.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Encodes the Mac stored ZIP envelope over one already-owned export snapshot. The archive worker
/// calls this before destination writing; no source media is read again and no compressor, process
/// or filesystem participates. A closed path grammar admits only the data filename and app image
/// names. Header order, DOS epoch, CRC and little-endian fields retain the original byte format.
/// </summary>
public static class VocabularyExportZip
{
    public const int MaximumBytes = 192 * 1024 * 1024;
    public const int MaximumFiles = 10_000;
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Data(VocabularyExportPackage package, CancellationToken cancellationToken = default)
    {
        var files = new List<(string Path, ReadOnlyMemory<byte> Bytes)> { (package.DataFilename, package.DataBytes) };
        files.AddRange(package.ImageBytes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => ("images/" + pair.Key, pair.Value)));
        long total = 0;
        if (files.Count > MaximumFiles) throw TooLarge();
        foreach (var file in files)
        {
            if (!ValidPath(file.Path) || file.Bytes.Length > MaximumBytes - total) throw TooLarge();
            total += file.Bytes.Length;
        }
        // Mac admits aggregate payload before cancellation, then checks once per file. The
        // admitted bytes plus at most 10,000 fixed-size headers bound temporary allocation too.
        using var output = new MemoryStream(checked((int)total + files.Count * 200));
        using var central = new MemoryStream();
        foreach (var (path, bytes) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] name = Encoding.UTF8.GetBytes(path);
            uint offset = checked((uint)output.Position), size = (uint)bytes.Length, checksum = Crc32(bytes.Span);
            U32(output, 0x04034b50); U16(output, 20); U16(output, 0); U16(output, 0);
            U16(output, 0); U16(output, 0x21); U32(output, checksum); U32(output, size); U32(output, size);
            U16(output, (ushort)name.Length); U16(output, 0); output.Write(name); output.Write(bytes.Span);
            U32(central, 0x02014b50); U16(central, 20); U16(central, 20);
            U16(central, 0); U16(central, 0); U16(central, 0); U16(central, 0x21);
            U32(central, checksum); U32(central, size); U32(central, size); U16(central, (ushort)name.Length);
            for (int i = 0; i < 4; i++) U16(central, 0);
            U32(central, 0); U32(central, offset); central.Write(name);
        }
        uint centralOffset = checked((uint)output.Position);
        central.Position = 0; central.CopyTo(output);
        U32(output, 0x06054b50); U16(output, 0); U16(output, 0);
        U16(output, (ushort)files.Count); U16(output, (ushort)files.Count);
        U32(output, checked((uint)central.Length)); U32(output, centralOffset); U16(output, 0);
        if (output.Length > MaximumBytes) throw TooLarge();
        return output.ToArray();
    }
    private static bool ValidPath(string path)
    {
        if (path is "Lingrove-vocabulary.txt" or "Lingrove-vocabulary.csv" or "Lingrove-vocabulary.json") return true;
        const string prefix = "images/lg-image-";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.Ordinal)
            || path.Length != prefix.Length + 64 + 4) return false;
        foreach (char value in path.AsSpan(prefix.Length, 64))
            if (value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        return ~crc;
    }
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
    private static void U16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); stream.Write(bytes);
    }
    private static void U32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes);
    }
    private static VocabularyExportException TooLarge() => new(VocabularyExportFailure.TooLarge);
}
