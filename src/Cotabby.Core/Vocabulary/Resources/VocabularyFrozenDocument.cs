namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Frozen v8 value envelope; v7 resource graph is unchanged and never passed to the v3 archive.
/// The explicit upgrade retains the original evidence and supplies an empty classification directory.
/// This is not a decoder, storage migration or complete arbitrary-input admission gate.</summary>
public sealed record VocabularyFrozenDocument(VocabularyDocument Resources, VocabularyClassificationIndex Classification)
{
    public const long CurrentVersion = 8;
    public static VocabularyFrozenDocument Upgrade(VocabularyDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SchemaVersion != 7) throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
        return new(source, new());
    }
}
