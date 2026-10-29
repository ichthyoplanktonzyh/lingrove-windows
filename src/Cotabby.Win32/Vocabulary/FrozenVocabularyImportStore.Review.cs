using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using FrozenReview = Cotabby.Core.Vocabulary.Frozen.VocabularyReview;
using FrozenBinding = Cotabby.Core.Vocabulary.Frozen.AnkiNoteBinding;
using DraftPolicy = Cotabby.Core.Vocabulary.Frozen.VocabularyReviewDraftPolicy;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore
{
    public Task<FrozenVocabularyImportSnapshot> SaveReviewAsync(Guid entryId, Guid senseId, FrozenReview value, FrozenReview? expected,
        Guid? cardId = null, IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null, CancellationToken cancellationToken = default)
    {
        var supplied = importedImages is null ? new Dictionary<string, VocabularyPreparedImage>() : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return ReviewTransaction(document => DraftPolicy.SaveReview(document, entryId, senseId, value, expected, cardId), cancellationToken, supplied);
    }
    public Task<FrozenVocabularyImportSnapshot> RecordAnkiBindingAsync(Guid entryId, Guid senseId, FrozenBinding binding,
        Guid? cardId = null, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => DraftPolicy.RecordAnkiBinding(document, entryId, senseId, binding, cardId), cancellationToken);
    public Task<FrozenVocabularyImportSnapshot> RemoveReviewAsync(Guid entryId, Guid senseId, Guid cardId, FrozenReview expected,
        CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => DraftPolicy.RemoveReview(document, entryId, senseId, cardId, expected), cancellationToken);
    public Task<FrozenVocabularyImportSnapshot> SaveTemplateAsync(ReviewLayoutTemplate template,
        IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null, CancellationToken cancellationToken = default)
    {
        var supplied = importedImages is null ? new Dictionary<string, VocabularyPreparedImage>() : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return ReviewTransaction(document => DraftPolicy.SaveTemplate(document, template), cancellationToken, supplied);
    }
    public Task<FrozenVocabularyImportSnapshot> RemoveTemplateAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReviewTransaction(document => DraftPolicy.RemoveTemplate(document, id), cancellationToken);

    private Task<FrozenVocabularyImportSnapshot> ReviewTransaction(Func<Cotabby.Core.Vocabulary.Frozen.VocabularyDocument, VocabularyReviewDraft> edit,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, VocabularyPreparedImage>? supplied = null) => Run(() => Database.Access(true, () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = LoadCurrent(cancellationToken);
        var draft = edit(before.Document);
        if (!draft.NeedsCommit) return new FrozenVocabularyImportSnapshot(before.Revision, before.Document);
        ValidateMediaContent(draft.Document);
        var existing = ImageReferences(before.Document); var references = ImageReferences(draft.Document);
        ValidateMediaBudget(references);
        var written = new List<VocabularyImageReference>();
        Cotabby.Core.Vocabulary.Frozen.VocabularyDocument committed;
        ulong revision;
        try
        {
            foreach (var image in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (supplied is not null && supplied.TryGetValue(image.Filename, out var prepared))
                {
                    if (prepared is null) throw new VocabularyException(VocabularyFailure.InvalidContent);
                    if (ImageStorage.Write(prepared.CopyBytes(), image)) written.Add(image);
                }
                else if (!existing.Contains(image)) _ = ImageStorage.Read(image);
            }
            cancellationToken.ThrowIfCancellationRequested();
            committed = CanonicalWritingPolicy.PrepareCommit(before.Document, draft.Document);
            revision = Database.Write(committed, before.Document, before.Revision, cancellationToken: cancellationToken);
        }
        catch { foreach (var image in written) RemoveImage(image); throw; }
        var retained = references.Select(image => image.Filename).ToHashSet(StringComparer.Ordinal);
        foreach (var image in existing.Where(image => !retained.Contains(image.Filename))) RemoveImage(image);
        var snapshot = new FrozenVocabularyImportSnapshot(revision, committed);
        _notifications.Writer.TryWrite(snapshot);
        return snapshot;
    }), cancellationToken);

    private static HashSet<VocabularyImageReference> ImageReferences(Cotabby.Core.Vocabulary.Frozen.VocabularyDocument document) =>
        document.Entries.SelectMany(entry => entry.Senses).SelectMany(sense =>
            VocabularyContentPolicy.Images(sense.Content ?? []).Concat(
                (sense.Review is null ? Enumerable.Empty<FrozenReview>() : [sense.Review]).Concat(sense.ReviewCards ?? [])
                    .SelectMany(review => new[] { review.Plan }.Concat(review.PreservedPlans))
                    .Where(plan => plan.Layout is not null).SelectMany(plan => VocabularyContentPolicy.Images(plan.Layout!.CustomContent))))
        .Concat((document.ReviewTemplates ?? []).SelectMany(template => VocabularyContentPolicy.Images(template.Layout.CustomContent))).ToHashSet();
}
