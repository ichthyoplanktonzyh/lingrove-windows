using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Separate semantic and classification drafts share the single canonical owner.</summary>
public interface ICanonicalVocabularySemanticStore
{
    Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(VocabularySemanticIndex edited, VocabularySemanticIndex baseline, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(VocabularyClassificationIndex edited, VocabularyClassificationIndex baseline, CancellationToken cancellationToken = default);
}
