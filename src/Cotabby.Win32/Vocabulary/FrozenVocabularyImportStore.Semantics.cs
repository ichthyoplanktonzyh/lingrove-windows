using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Resources;
using FrozenIndex = Cotabby.Core.Vocabulary.Frozen.VocabularySemanticIndex;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore : ICanonicalVocabularyMeaningOrganizationStore
{
    public Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(FrozenIndex edited, FrozenIndex baseline, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => new VocabularyReviewDraft(VocabularySemanticDraftPolicy.UpdateSemantics(document, edited, baseline), true), cancellationToken);
    public Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(VocabularyClassificationIndex edited, VocabularyClassificationIndex baseline, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => new VocabularyReviewDraft(VocabularySemanticDraftPolicy.UpdateClassification(document, edited, baseline), true), cancellationToken);
    public Task<FrozenVocabularyImportSnapshot> UpdateMeaningOrganizationAsync(FrozenIndex editedSemantics, FrozenIndex semanticBaseline, VocabularyClassificationIndex editedClassification, VocabularyClassificationIndex classificationBaseline, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => new VocabularyReviewDraft(VocabularySemanticDraftPolicy.UpdateMeaningOrganization(document, editedSemantics, semanticBaseline, editedClassification, classificationBaseline), true), cancellationToken);
}
