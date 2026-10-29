using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Resources;
using FrozenIndex = Cotabby.Core.Vocabulary.Frozen.VocabularySemanticIndex;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore
{
    public Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(FrozenIndex edited, FrozenIndex baseline, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => new VocabularyReviewDraft(VocabularySemanticDraftPolicy.UpdateSemantics(document, edited, baseline), true), cancellationToken);
    public Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(VocabularyClassificationIndex edited, VocabularyClassificationIndex baseline, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => new VocabularyReviewDraft(VocabularySemanticDraftPolicy.UpdateClassification(document, edited, baseline), true), cancellationToken);
}
