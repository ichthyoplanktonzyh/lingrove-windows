using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>A full joined entry is a detached editor draft; the owner acknowledges its save.</summary>
public interface ICanonicalVocabularyEditorStore
{
    Task<FrozenVocabularyImportSnapshot> UpdateEntryAsync(VocabularyEntry edited, VocabularyEntry baseline,
        IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null, CancellationToken cancellationToken = default);
}
