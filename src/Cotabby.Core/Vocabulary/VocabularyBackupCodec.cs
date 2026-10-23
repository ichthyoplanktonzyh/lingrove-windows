namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Complete Mac Codable notebook/backup shape over the existing row leaf adapters. Encoding never
/// flattens senses or removes child collections as database projection does. The envelope transports
/// media as base64 and preserves absent versus empty optional fields. This is a wire boundary only:
/// callers must bound input/output and validate version, graph and exact media before any restore.
/// It performs no filesystem access, image decode, semantic admission or durable mutation.
/// </summary>
public static class VocabularyBackupCodec
{
    private sealed record Wire(long Version, VocabularyDocument Document, IReadOnlyDictionary<string, byte[]> Images);
    public static byte[] Encode(VocabularyBackup backup)
    { ArgumentNullException.ThrowIfNull(backup); return VocabularyPayloadCodec.EncodePortableValue(new Wire(backup.Version, backup.Document, backup.ImageBytes)); }
    public static VocabularyBackup Decode(ReadOnlyMemory<byte> bytes)
    {
        var wire = VocabularyPayloadCodec.DecodePortableValue<Wire>(bytes);
        return new(wire.Document, wire.Images, wire.Version);
    }
    public static byte[] EncodeDocument(VocabularyDocument document) => VocabularyPayloadCodec.EncodePortableValue(document);
    public static VocabularyDocument DecodeDocument(ReadOnlyMemory<byte> bytes) => VocabularyPayloadCodec.DecodePortableValue<VocabularyDocument>(bytes);
}
