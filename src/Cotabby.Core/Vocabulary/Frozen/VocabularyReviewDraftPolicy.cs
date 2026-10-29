using Cotabby.Core.Review;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>An uncommitted candidate. The archive owns validation, media and acknowledgement.</summary>
public sealed record VocabularyReviewDraft(VocabularyDocument Document, bool NeedsCommit);

/// <summary>Frozen review and template edits over collection-owned state. No IO, timestamps,
/// evidence deletion or persistence occurs here; callers must serialize against the latest graph.</summary>
public static class VocabularyReviewDraftPolicy
{
    public static VocabularyReviewDraft SaveReview(VocabularyDocument document, Guid entryId, Guid senseId,
        VocabularyReview value, VocabularyReview? expected, Guid? cardId = null) => Change(document, entryId, senseId, false, collection =>
    {
        if (ReviewValue(collection, cardId) != expected) throw Conflict();
        if (cardId is not null && value.Plan.Id != cardId) throw Invalid();
        return SetReview(collection, value, cardId);
    });

    public static VocabularyReviewDraft RecordAnkiBinding(VocabularyDocument document, Guid entryId, Guid senseId,
        AnkiNoteBinding binding, Guid? cardId = null) => Change(document, entryId, senseId, true, collection =>
    {
        var review = ReviewValue(collection, cardId) ?? throw Conflict();
        var bindings = review.Bindings.ToList(); int index = bindings.FindIndex(value => value.Id == binding.Id);
        Guid? active = review.ActiveBindingId;
        if (index >= 0) bindings[index] = binding;
        else { bindings.Add(binding); if (bindings.Count > 1) active = null; }
        if (bindings.Count == 1) active = binding.Id;
        return SetReview(collection, review with { Bindings = bindings, ActiveBindingId = active }, cardId);
    });

    public static VocabularyReviewDraft RemoveReview(VocabularyDocument document, Guid entryId, Guid senseId,
        Guid cardId, VocabularyReview expected) => Change(document, entryId, senseId, false, collection =>
    {
        if (ReviewValue(collection, cardId) != expected) throw Conflict();
        return collection with { ReviewCards = collection.ReviewCards?.Where(value => value.Plan.Id != cardId).ToArray() };
    });

    public static VocabularyReviewDraft SaveTemplate(VocabularyDocument document, ReviewLayoutTemplate template)
    {
        var templates = (document.ReviewTemplates ?? []).ToList(); int index = templates.FindIndex(value => value.Id == template.Id);
        if (index < 0) templates.Add(template); else templates[index] = template;
        Cotabby.Core.Vocabulary.VocabularyArchiveValidation.ValidateTemplates(templates);
        return new(document with { ReviewTemplates = templates }, true);
    }

    public static VocabularyReviewDraft RemoveTemplate(VocabularyDocument document, Guid id) =>
        new(document with { ReviewTemplates = document.ReviewTemplates?.Where(value => value.Id != id).ToArray() }, true);

    private static VocabularyReviewDraft Change(VocabularyDocument document, Guid entryId, Guid senseId, bool resolveMerged,
        Func<VocabularyCollectionItem, VocabularyCollectionItem> change)
    {
        var entry = document.Catalog.FirstOrDefault(value => value.Id == entryId) ?? throw Conflict();
        var sense = entry.Senses.FirstOrDefault(value => value.Id == senseId || resolveMerged && value.MergedFrom.Contains(senseId)) ?? throw Conflict();
        var collections = document.Collections.ToArray();
        int index = Array.FindIndex(collections, value => value.SenseId == sense.Id);
        if (index < 0) throw Conflict();
        collections[index] = change(collections[index]);
        return new(document with { Collections = collections }, true);
    }

    private static VocabularyReview? ReviewValue(VocabularyCollectionItem collection, Guid? cardId) => cardId is null
        ? collection.Review : collection.ReviewCards?.FirstOrDefault(value => value.Plan.Id == cardId);
    private static VocabularyCollectionItem SetReview(VocabularyCollectionItem collection, VocabularyReview review, Guid? cardId)
    {
        if (cardId is null) return collection with { Review = review };
        var cards = (collection.ReviewCards ?? []).ToList(); int index = cards.FindIndex(value => value.Plan.Id == cardId);
        if (index < 0) cards.Add(review); else cards[index] = review;
        return collection with { ReviewCards = cards };
    }
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
}
