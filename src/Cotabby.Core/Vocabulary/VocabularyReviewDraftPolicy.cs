using Cotabby.Core.Review;

namespace Cotabby.Core.Vocabulary;

/// <summary>Original review/template transaction drafts, separate from entry editing. The archive
/// supplies its latest immutable document, then owns media/validation/SQL acknowledgement. A sheet
/// compares only its named review; an in-flight Anki receipt follows a merged sense without
/// replacing its newer plan. This stateless boundary performs no I/O or timestamp mutation.</summary>
public static class VocabularyReviewDraftPolicy
{
    public static VocabularyDocument SaveReview(VocabularyDocument document, Guid entryId, Guid senseId,
        VocabularyReview value, VocabularyReview? expected, Guid? cardId = null) => Change(document, entryId, senseId, false, sense =>
    {
        if (sense.ReviewValue(cardId) != expected) throw Conflict();
        if (cardId is not null && value.Plan.Id != cardId) throw Invalid();
        return sense.WithReview(value, cardId);
    });
    public static VocabularyDocument RecordAnkiBinding(VocabularyDocument document, Guid entryId, Guid senseId,
        AnkiNoteBinding binding, Guid? cardId = null) => Change(document, entryId, senseId, true, sense =>
    {
        var review = sense.ReviewValue(cardId) ?? throw Conflict();
        var bindings = review.Bindings.ToList(); int index = bindings.FindIndex(b => b.Id == binding.Id);
        Guid? active = review.ActiveBindingId;
        if (index >= 0) bindings[index] = binding;
        else { bindings.Add(binding); if (bindings.Count > 1) active = null; }
        if (bindings.Count == 1) active = binding.Id;
        return sense.WithReview(review with { Bindings = bindings, ActiveBindingId = active }, cardId);
    });
    public static VocabularyDocument RemoveReview(VocabularyDocument document, Guid entryId, Guid senseId,
        Guid cardId, VocabularyReview expected) => Change(document, entryId, senseId, false, sense =>
    {
        if (sense.ReviewValue(cardId) != expected) throw Conflict();
        return sense with { ReviewCards = sense.ReviewCards?.Where(r => r.Plan.Id != cardId).ToArray() };
    });
    public static VocabularyDocument SaveTemplate(VocabularyDocument document, ReviewLayoutTemplate template)
    {
        var templates = (document.ReviewTemplates ?? []).ToList(); int index = templates.FindIndex(t => t.Id == template.Id);
        if (index >= 0) templates[index] = template; else templates.Add(template);
        VocabularyArchiveValidation.ValidateTemplates(templates);
        return document with { ReviewTemplates = templates };
    }
    public static VocabularyDocument RemoveTemplate(VocabularyDocument document, Guid id) => document with
    { ReviewTemplates = document.ReviewTemplates?.Where(t => t.Id != id).ToArray() };

    private static VocabularyDocument Change(VocabularyDocument document, Guid entryId, Guid senseId, bool resolveMerged,
        Func<VocabularySense, VocabularySense> change)
    {
        var entries = document.Entries.ToArray(); int entry = System.Array.FindIndex(entries, e => e.Id == entryId);
        if (entry < 0) throw Conflict();
        var senses = entries[entry].Senses.ToArray();
        int sense = resolveMerged ? VocabularySensePolicy.Index(senseId, entries[entry]) ?? -1 : System.Array.FindIndex(senses, s => s.Id == senseId);
        if (sense < 0) throw Conflict();
        senses[sense] = change(senses[sense]); entries[entry] = entries[entry] with { Senses = senses };
        return document with { Entries = entries };
    }
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
}
