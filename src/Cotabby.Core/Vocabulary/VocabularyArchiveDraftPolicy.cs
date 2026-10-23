using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Stable Mac archive editing rules separated from SQLite/actor effects. A future archive supplies
/// its latest document and the editor's original baseline, then prunes/validates/commits the returned
/// detached draft. Three-way field comparisons retain concurrent captures and reject conflicting
/// edits by stable identity. A returned draft is never a durable acknowledgement.
/// </summary>
public static class VocabularyArchiveDraftPolicy
{
    public static VocabularyDocument Update(VocabularyEntry edited, VocabularyEntry baseline, VocabularyDocument document, DateTimeOffset now)
    {
        VocabularyArchiveValidation.ValidateRepresentability(document);
        if (!VocabularyArchiveValidation.Representable(edited) || !VocabularyArchiveValidation.Representable(baseline)) throw Invalid();
        var entries = document.Entries.ToList(); int index = entries.FindIndex(entry => entry.Id == edited.Id);
        if (index < 0 || baseline.Id != edited.Id || edited.Senses.Select(sense => sense.Id).Distinct().Count() != edited.Senses.Count
            || edited.Examples.Select(example => example.Id).Distinct().Count() != edited.Examples.Count) throw Invalid();
        var original = entries[index];
        if (edited.Language != original.Language || edited.Kind != original.Kind || VocabularyCapturePolicy.Normalized(edited.Expression).Length == 0
            || entries.Any(entry => entry.Id != edited.Id && VocabularyCapturePolicy.EntryKey(entry) == VocabularyCapturePolicy.EntryKey(edited))) throw Invalid();
        entries[index] = original with
        {
            Expression = Merge(baseline.Expression, original.Expression, edited.Expression, CollectionValues.Same),
            DictionaryForm = Merge(baseline.DictionaryForm, original.DictionaryForm, edited.DictionaryForm, CollectionValues.Same),
            Senses = MergeSenses(baseline.Senses, original.Senses, edited.Senses, now), UpdatedAt = now,
        };
        return document with { Entries = entries };
    }

    private static IReadOnlyList<VocabularySense> MergeSenses(IReadOnlyList<VocabularySense> baseline, IReadOnlyList<VocabularySense> current,
        IReadOnlyList<VocabularySense> edited, DateTimeOffset now)
    {
        var result = current.ToList();
        foreach (var value in edited)
        {
            var before = baseline.FirstOrDefault(sense => sense.Id == value.Id);
            if (before is null)
            {
                if (result.Any(sense => sense.Id == value.Id || sense.MergedFrom.Contains(value.Id))) throw Conflict();
                result.Add(value); continue;
            }
            if (value == before) continue;
            int index = result.FindIndex(sense => sense.Id == value.Id); if (index < 0) throw Conflict();
            var live = result[index];
            var meaning = Merge(new VocabularyMeaning(before.Meaning, before.MeaningLanguage), new(live.Meaning, live.MeaningLanguage),
                new(value.Meaning, value.MeaningLanguage));
            bool changedMeaning = !CollectionValues.Same(value.Meaning, before.Meaning) || value.MeaningLanguage != before.MeaningLanguage;
            var standalone = live.StandaloneMeanings;
            if (live.IsSavedWithoutContext && changedMeaning && !standalone.Contains(meaning)) standalone = standalone.Append(meaning).ToArray();
            result[index] = live with
            {
                Meaning = meaning.Text, MeaningLanguage = meaning.Language,
                MeaningOrigin = changedMeaning || value.MeaningOrigin == VocabularyMeaningOrigin.User && before.MeaningOrigin != VocabularyMeaningOrigin.User
                    ? VocabularyMeaningOrigin.User : live.MeaningOrigin,
                StandaloneMeanings = standalone,
                Note = Merge(before.Note, live.Note, value.Note, CollectionValues.Same),
                Tags = Merge(before.Tags, live.Tags, value.Tags, CollectionValues.SequenceEqual),
                Content = Merge(before.Content, live.Content, value.Content, CollectionValues.SequenceEqual),
                Examples = MergeExamples(before.Examples, live.Examples, value.Examples), UpdatedAt = now,
            };
        }
        foreach (var before in baseline.Where(before => !edited.Any(value => value.Id == before.Id)))
        {
            var live = result.FirstOrDefault(sense => sense.Id == before.Id);
            if (live is not null && live != before) throw Conflict();
            result.RemoveAll(sense => sense.Id == before.Id);
        }
        return result;
    }

    private static IReadOnlyList<VocabularyExample> MergeExamples(IReadOnlyList<VocabularyExample> baseline,
        IReadOnlyList<VocabularyExample> current, IReadOnlyList<VocabularyExample> edited)
    {
        var result = current.ToList();
        foreach (var value in edited)
        {
            var before = baseline.FirstOrDefault(example => example.Id == value.Id);
            if (before is null)
            {
                if (!result.Any(example => VocabularyCapturePolicy.ExampleKey(example) == VocabularyCapturePolicy.ExampleKey(value))) result.Add(value);
                continue;
            }
            if (value == before) continue;
            int index = result.FindIndex(example => example.Id == value.Id); if (index < 0) throw Conflict();
            var next = value;
            // An automatic clear after editing source text is not a user-authored blank meaning.
            // A future explanation can fill the new context; an explicit meaning edit is owned.
            if (!CollectionValues.Same(value.Text, before.Text) && value.Meaning.Length == 0)
                next = next with { MeaningOrigin = VocabularyMeaningOrigin.Captured };
            else if (!CollectionValues.Same(value.Meaning, before.Meaning) || value.MeaningLanguage != before.MeaningLanguage)
                next = next with { MeaningOrigin = VocabularyMeaningOrigin.User };
            result[index] = Merge(before, result[index], next);
        }
        foreach (var before in baseline.Where(before => !edited.Any(value => value.Id == before.Id)))
        {
            var live = result.FirstOrDefault(example => example.Id == before.Id);
            if (live is not null && live != before) throw Conflict();
            result.RemoveAll(example => example.Id == before.Id);
        }
        return result;
    }

    private static T Merge<T>(T baseline, T current, T edited, Func<T, T, bool>? equal = null)
    {
        equal ??= EqualityComparer<T>.Default.Equals;
        if (equal(edited, baseline)) return current;
        if (!equal(current, baseline) && !equal(current, edited)) throw Conflict();
        return edited;
    }

    public static VocabularyDocument Remove(VocabularyCapture capture, VocabularyDocument document)
    {
        var entries = document.Entries.ToList(); int index = entries.FindIndex(entry => VocabularyCapturePolicy.EntryKey(entry) == VocabularyCapturePolicy.EntryKey(capture));
        if (index < 0 || VocabularySensePolicy.Destination(capture, entries[index]) is not { } destination
            || VocabularySensePolicy.Index(destination.SenseId, entries[index]) is not { } owner) return document;
        var senses = entries[index].Senses.ToList(); var sense = senses[owner];
        sense = destination.ExampleId is { } id ? sense with { Examples = sense.Examples.Where(example => example.Id != id).ToArray() }
            : sense with { StandaloneMeanings = [], IsSavedWithoutContext = false, Explanations = [] };
        if (sense.Examples.Count == 0 && !sense.IsSavedWithoutContext && sense.Note.Length == 0 && sense.Tags.Count == 0
            && sense.MeaningOrigin != VocabularyMeaningOrigin.User && sense.MergedFrom.Count == 0 && sense.Review is null
            && (sense.ReviewCards?.Count ?? 0) == 0 && (sense.Content?.Count ?? 0) == 0) senses.RemoveAt(owner);
        else senses[owner] = sense;
        if (senses.Count == 0) entries.RemoveAt(index); else entries[index] = entries[index] with { Senses = senses };
        return document with { Entries = entries };
    }

    public static VocabularyDocument CreateSense(Guid entryId, Guid senseId, VocabularyDocument document, DateTimeOffset now)
    {
        var entries = document.Entries.ToList(); int index = entries.FindIndex(entry => entry.Id == entryId); if (index < 0) throw Invalid();
        var entry = entries[index]; entries[index] = entry with { Senses = entry.Senses.Append(new VocabularySense(senseId, "", now, now)).ToArray(), UpdatedAt = now };
        return document with { Entries = entries };
    }
    public static VocabularyDocument RemoveSense(Guid entryId, Guid senseId, VocabularyDocument document)
    {
        var entries = document.Entries.ToList(); int index = entries.FindIndex(entry => entry.Id == entryId);
        if (index < 0 || !entries[index].Senses.Any(sense => sense.Id == senseId)) throw Invalid();
        var senses = entries[index].Senses.Where(sense => sense.Id != senseId).ToArray();
        if (senses.Length == 0) entries.RemoveAt(index); else entries[index] = entries[index] with { Senses = senses };
        return document with { Entries = entries };
    }
    public static VocabularyDocument RemoveEntry(Guid entryId, VocabularyDocument document) => document with { Entries = document.Entries.Where(entry => entry.Id != entryId).ToArray() };

    /// <summary>Mac commit normalization runs before whole-graph validation, without relabeling old AI output.</summary>
    public static VocabularyDocument PruneExplanations(VocabularyDocument document)
    {
        // This is only the .NET representability pass, not full semantic validation. Stale but
        // well-formed records must still reach the original prune step; null/invalid enum values
        // cannot be silently dropped before validation just because their owner was edited.
        VocabularyArchiveValidation.ValidateRepresentability(document);
        return document with
        {
            Entries = document.Entries.Select(entry => entry with
            {
                Senses = entry.Senses.Select(sense => sense with
                {
                    Explanations = sense.Explanations.Where(record => sense.IsSavedWithoutContext
                        && record.Input == LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, null, sense))).ToArray(),
                    Examples = sense.Examples.Select(example => example with
                    {
                        Explanations = example.Explanations.Where(record => record.Input == LearningExplanationPolicy.Input(
                            LearningExplanationPolicy.Capture(entry, example, sense))).ToArray(),
                    }).ToArray(),
                }).ToArray(),
            }).ToArray(),
        };
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
}
