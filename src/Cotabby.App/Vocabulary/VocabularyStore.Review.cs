using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>Preparation sessions borrow this Store instead of opening another archive. Unlike
/// ordinary editing's Boolean/report boundary, original review methods propagate failure to their
/// caller and publish only acknowledged snapshots. Existing shared error state survives a failure;
/// the Store still admits/drains the full operation before closing its archive.</summary>
internal sealed partial class VocabularyStore
{
    internal Task SaveReviewAsync(Guid entryId, Guid senseId, VocabularyReview value, VocabularyReview? expected,
        Guid? cardId = null, IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null, CancellationToken cancellationToken = default)
    {
        var supplied = importedImages is null ? null : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return AcknowledgeReview(() => archive.SaveReviewAsync(entryId, senseId, value, expected, cardId, supplied, cancellationToken));
    }
    internal Task RecordAnkiBindingAsync(Guid entryId, Guid senseId, AnkiNoteBinding binding, Guid? cardId = null) =>
        AcknowledgeReview(() => archive.RecordAnkiBindingAsync(entryId, senseId, binding, cardId));
    internal Task RemoveReviewAsync(Guid entryId, Guid senseId, Guid cardId, VocabularyReview expected) =>
        AcknowledgeReview(() => archive.RemoveReviewAsync(entryId, senseId, cardId, expected));
    internal Task SaveTemplateAsync(ReviewLayoutTemplate template, IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null,
        CancellationToken cancellationToken = default)
    {
        var supplied = importedImages is null ? null : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return AcknowledgeReview(() => archive.SaveTemplateAsync(template, supplied, cancellationToken));
    }
    internal Task RemoveTemplateAsync(Guid id) => AcknowledgeReview(() => archive.RemoveTemplateAsync(id));
    private Task AcknowledgeReview(Func<Task<VocabularySnapshot>> operation)
    {
        lock (_gate) return _disposal is null ? StartLocked(async () =>
        {
            Apply(await operation().ConfigureAwait(false)); return true;
        }) : Task.FromException(new ObjectDisposedException(nameof(VocabularyStore)));
    }
}
