using System.Buffers.Binary;
using System.IO;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// One bounded, binary stdout reply from the sibling transfer host. No paths, text, commands or
/// native handles cross this pipe: magic + signed byte count (-1 unavailable, -2 failure) + bytes.
/// Parent and host compile this same framing. The parent still validates the image through WIC.
/// </summary>
internal static class VocabularyImageTransferProtocol
{
    private const uint Magic = 0x3143474C; // LGC1, little endian.
    internal static void Write(Stream output, byte[]? bytes, bool failed = false)
    {
        if (bytes is { Length: > VocabularyClipboardImageReader.MaximumBytes }) throw new InvalidDataException();
        Span<byte> header = stackalloc byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], failed ? -2 : bytes?.Length ?? -1);
        output.Write(header); if (!failed && bytes is not null) output.Write(bytes); output.Flush();
    }
    internal static async Task<byte[]?> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        byte[] header = new byte[8]; await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || length < -1 || length > VocabularyClipboardImageReader.MaximumBytes) throw new InvalidDataException();
        byte[]? data = length < 0 ? null : new byte[length];
        if (data is not null) await input.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        byte[] extra = new byte[1]; if (await input.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0) throw new InvalidDataException();
        return data;
    }
}
