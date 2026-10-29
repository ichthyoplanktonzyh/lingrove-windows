using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore
{
    public async Task<CanonicalSenseReceipt> CreateSenseAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        Guid id = Guid.NewGuid();
        var snapshot = await ReviewTransaction(document => new VocabularyReviewDraft(VocabularySenseActionPolicy.Create(document, entryId, id, _now()).Document, true), cancellationToken).ConfigureAwait(false);
        return new(snapshot, id);
    }
    public Task<FrozenVocabularyImportSnapshot> MergeSensesAsync(Guid entryId, Guid sourceId, Guid targetId, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => new VocabularyReviewDraft(VocabularySenseActionPolicy.Merge(document, entryId, sourceId, targetId, _now()).Document, true), cancellationToken);
    public Task<FrozenVocabularyImportSnapshot> RemoveSenseAsync(Guid entryId, Guid senseId, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => new VocabularyReviewDraft(VocabularySenseActionPolicy.Remove(document, entryId, senseId).Document, true), cancellationToken);
    public async Task<CanonicalSenseReceipt> MoveExampleAsync(Guid entryId, Guid exampleId, Guid sourceId, Guid? targetId, CancellationToken cancellationToken = default)
    {
        Guid? destination = null;
        var snapshot = await ReviewTransaction(document =>
        {
            var draft = VocabularySenseActionPolicy.Move(document, entryId, exampleId, sourceId, targetId, _now());
            destination = draft.DestinationId; return new VocabularyReviewDraft(draft.Document, true);
        }, cancellationToken).ConfigureAwait(false);
        return new(snapshot, destination!.Value);
    }
}
