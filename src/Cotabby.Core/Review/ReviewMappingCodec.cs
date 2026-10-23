using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>Original UserDefaults Data stores enum-keyed mappings as alternating JSON arrays.
/// Reuse the established Codable leaf boundary, not ordinary .NET object-key serialization. This
/// stateless codec returns detached maps and never reads preferences, credentials or a notebook.</summary>
public static class ReviewMappingCodec
{
    public static IReadOnlyDictionary<ReviewField, string>? Decode(ReadOnlyMemory<byte> data)
    {
        try { return CollectionValues.Map(VocabularyPayloadCodec.DecodePortableValue<IReadOnlyDictionary<ReviewField, string>>(data)); }
        catch (VocabularyException) { return null; }
    }
    public static byte[] Encode(IReadOnlyDictionary<ReviewField, string> value)
        => VocabularyPayloadCodec.EncodePortableValue<IReadOnlyDictionary<ReviewField, string>>(value);
}

