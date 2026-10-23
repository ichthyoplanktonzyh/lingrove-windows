using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Review drafts and verified receipts use the existing serialized notebook owner.
/// Candidate construction reads the latest revision; media staging and SQL publication retain
/// their established shared lock/rollback boundary. A remote write never replaces an entry draft.</summary>
public sealed partial class VocabularyArchive
{
    public Task<VocabularySnapshot> SaveReviewAsync(Guid entryId, Guid senseId, VocabularyReview value,
        VocabularyReview? expected, Guid? cardId = null, IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null,
        CancellationToken cancellationToken = default)
    {
        var supplied = importedImages is null ? new Dictionary<string, VocabularyPreparedImage>() : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return Run(() => CommitWithImages(VocabularyReviewDraftPolicy.SaveReview(Load().Document, entryId, senseId, value, expected, cardId), supplied, cancellationToken));
    }
    public Task<VocabularySnapshot> RecordAnkiBindingAsync(Guid entryId, Guid senseId, AnkiNoteBinding binding, Guid? cardId = null) =>
        Run(() => Commit(VocabularyReviewDraftPolicy.RecordAnkiBinding(Load().Document, entryId, senseId, binding, cardId)));
    public Task<VocabularySnapshot> RemoveReviewAsync(Guid entryId, Guid senseId, Guid cardId, VocabularyReview expected) =>
        Run(() => Commit(VocabularyReviewDraftPolicy.RemoveReview(Load().Document, entryId, senseId, cardId, expected)));
    public Task<VocabularySnapshot> SaveTemplateAsync(ReviewLayoutTemplate template, IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null,
        CancellationToken cancellationToken = default)
    {
        var supplied = importedImages is null ? new Dictionary<string, VocabularyPreparedImage>() : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CommitWithImages(VocabularyReviewDraftPolicy.SaveTemplate(Load().Document, template), supplied, cancellationToken);
        });
    }
    public Task<VocabularySnapshot> RemoveTemplateAsync(Guid id) => Run(() => Commit(VocabularyReviewDraftPolicy.RemoveTemplate(Load().Document, id)));
}
