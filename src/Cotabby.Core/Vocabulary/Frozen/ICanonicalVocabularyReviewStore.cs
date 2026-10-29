using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Review transactions borrowed from the single canonical owner. Returned snapshots
/// acknowledge committed revisions; supplied images join the owner's media transaction.</summary>
public interface ICanonicalVocabularyReviewStore
{
    Task<FrozenVocabularyImportSnapshot> SaveReviewAsync(Guid entryId, Guid senseId, VocabularyReview value, VocabularyReview? expected,
        Guid? cardId = null, IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> RecordAnkiBindingAsync(Guid entryId, Guid senseId, AnkiNoteBinding binding,
        Guid? cardId = null, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> RemoveReviewAsync(Guid entryId, Guid senseId, Guid cardId, VocabularyReview expected, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> SaveTemplateAsync(ReviewLayoutTemplate template,
        IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> RemoveTemplateAsync(Guid id, CancellationToken cancellationToken = default);
}
