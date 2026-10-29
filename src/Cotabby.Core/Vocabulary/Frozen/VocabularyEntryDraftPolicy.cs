using Cotabby.Core.Support;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary.Resources;
using FrozenCompleteness = Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Source-derived detached editor merge. It does not commit or discard raw evidence.</summary>
public static class VocabularyEntryDraftPolicy
{
    public static VocabularyDocument Update(VocabularyEntry edited, VocabularyEntry baseline, VocabularyDocument document, SourceDate now)
    {
        VocabularyArchiveValidation.ValidateRepresentability(document);
        VocabularyArchiveValidation.ValidateEntryRepresentability(edited);
        VocabularyArchiveValidation.ValidateEntryRepresentability(baseline);
        var entries = document.Entries.ToList(); int index = entries.FindIndex(value => value.Id == edited.Id);
        if (index < 0 || baseline.Id != edited.Id || edited.Senses.Select(value => value.Id).Distinct().Count() != edited.Senses.Count
            || edited.Examples.Select(value => value.Id).Distinct().Count() != edited.Examples.Count) throw Invalid();
        // The editor does not create or delete senses; those are separate source commands.
        if (!baseline.Senses.Select(value => value.Id).ToHashSet().SetEquals(edited.Senses.Select(value => value.Id))) throw Invalid();
        var current = entries[index]; var collected = document.Collections.Select(value => value.SenseId).ToHashSet();
        if (!edited.Senses.Any(value => collected.Contains(value.Id)) || edited.Senses.Any(value =>
            baseline.Senses.FirstOrDefault(old => old.Id == value.Id) is { } old && old != value && !collected.Contains(value.Id))) throw Conflict();
        if (edited.Language != current.Language || edited.Kind != current.Kind || VocabularyCapturePolicy.Normalized(edited.Expression).Length == 0
            || entries.Any(value => value.Id != edited.Id && VocabularyArchiveValidation.EntryKey(value) == VocabularyArchiveValidation.EntryKey(edited))) throw Invalid();
        var merged = current with { Expression = Merge(baseline.Expression, current.Expression, edited.Expression, CollectionValues.Same),
            DictionaryForm = Merge(baseline.DictionaryForm, current.DictionaryForm, edited.DictionaryForm, CollectionValues.Same),
            Senses = MergeSenses(baseline.Senses, current.Senses, edited.Senses, now), UpdatedAt = now };
        var prior = current.Examples.GroupBy(Key, CanonicalStringComparer.Instance).ToDictionary(group => group.Key, group => group.Count(), CanonicalStringComparer.Instance);
        if (merged.Examples.GroupBy(Key, CanonicalStringComparer.Instance).Any(group => group.Count() > 1 && group.Count() > prior.GetValueOrDefault(group.Key))) throw Conflict();
        entries[index] = merged;
        var next = VocabularyNotebookProjection.Replace(entries, document);
        var collections = next.Collections.ToList();
        foreach (var before in baseline.Senses)
        {
            var changed = edited.Senses.FirstOrDefault(value => value.Id == before.Id);
            if (changed is null) { collections.RemoveAll(value => value.SenseId == before.Id); continue; }
            var removed = before.Examples.Select(value => value.Id).Except(changed.Examples.Select(value => value.Id)).ToHashSet();
            int offset = collections.FindIndex(value => value.SenseId == before.Id);
            if (offset >= 0) collections[offset] = collections[offset] with { InterpretationIds = collections[offset].InterpretationIds.Where(id => !removed.Contains(id)).ToArray() };
        }
        return next with { Collections = collections };
    }
    private static IReadOnlyList<VocabularySense> MergeSenses(IReadOnlyList<VocabularySense> baseline, IReadOnlyList<VocabularySense> current,
        IReadOnlyList<VocabularySense> edited, SourceDate now)
    {
        var result = current.ToList();
        foreach (var value in edited)
        {
            var before = baseline.FirstOrDefault(item => item.Id == value.Id);
            if (before is null)
            {
                if (result.Any(item => item.Id == value.Id || item.MergedFrom.Contains(value.Id))) throw Conflict();
                result.Add(value); continue;
            }
            if (value == before) continue;
            int index = result.FindIndex(item => item.Id == value.Id); if (index < 0) throw Conflict();
            var live = result[index];
            var meaning = Merge(new VocabularyMeaning(before.Meaning, before.MeaningLanguage), new(live.Meaning, live.MeaningLanguage), new(value.Meaning, value.MeaningLanguage));
            bool changed = !CollectionValues.Same(value.Meaning, before.Meaning) || value.MeaningLanguage != before.MeaningLanguage;
            var standalone = live.StandaloneMeanings;
            if (live.IsSavedWithoutContext && changed && !standalone.Contains(meaning)) standalone = standalone.Append(meaning).ToArray();
            result[index] = live with { Meaning = meaning.Text, MeaningLanguage = meaning.Language,
                MeaningOrigin = changed || value.MeaningOrigin == VocabularyMeaningOrigin.User && before.MeaningOrigin != VocabularyMeaningOrigin.User ? VocabularyMeaningOrigin.User : live.MeaningOrigin,
                StandaloneMeanings = standalone, Note = Merge(before.Note, live.Note, value.Note, CollectionValues.Same),
                Tags = Merge(before.Tags, live.Tags, value.Tags, CollectionValues.SequenceEqual), Content = Merge(before.Content, live.Content, value.Content, CollectionValues.SequenceEqual),
                Examples = MergeExamples(before.Examples, live.Examples, value.Examples), UpdatedAt = now };
        }
        foreach (var before in baseline.Where(item => !edited.Any(value => value.Id == item.Id)))
        { var live = result.FirstOrDefault(item => item.Id == before.Id); if (live is not null && live != before) throw Conflict(); }
        return result;
    }
    private static IReadOnlyList<VocabularyExample> MergeExamples(IReadOnlyList<VocabularyExample> baseline, IReadOnlyList<VocabularyExample> current, IReadOnlyList<VocabularyExample> edited)
    {
        var result = current.ToList();
        foreach (var value in edited)
        {
            var before = baseline.FirstOrDefault(item => item.Id == value.Id);
            if (before is null) { if (!result.Any(item => CanonicalStringComparer.Instance.Equals(Key(item), Key(value)))) result.Add(value); continue; }
            if (value == before) continue;
            int index = result.FindIndex(item => item.Id == value.Id); if (index < 0) throw Conflict();
            var corrected = value;
            if (!CollectionValues.Same(value.Text, before.Text) || !CollectionValues.Same(value.SourceApplicationId, before.SourceApplicationId))
                corrected = corrected with { SourceId = Guid.NewGuid(), SourceKind = VocabularySourceKind.Authored, OccurrenceId = Guid.NewGuid() };
            if (!CollectionValues.SequenceEqual(value.Ranges, before.Ranges)) corrected = corrected with { OccurrenceId = Guid.NewGuid() };
            if (!CollectionValues.Same(value.Text, before.Text) && value.Meaning.Length == 0) corrected = corrected with { MeaningOrigin = VocabularyMeaningOrigin.Captured };
            else if (!CollectionValues.Same(value.Meaning, before.Meaning) || value.MeaningLanguage != before.MeaningLanguage) corrected = corrected with { MeaningOrigin = VocabularyMeaningOrigin.User };
            result[index] = Merge(before, result[index], corrected);
        }
        foreach (var before in baseline.Where(item => !edited.Any(value => value.Id == item.Id)))
        { var live = result.FirstOrDefault(item => item.Id == before.Id); if (live is not null && live != before) throw Conflict(); }
        return result;
    }
    private static string Key(VocabularyExample value)
    {
        var ranges = string.Join(",", value.Ranges.Select(range => $"{range.Location}:{range.Length}"));
        string kind = value.Completeness switch { FrozenCompleteness.Sentence => "sentence", FrozenCompleteness.Fragment => "fragment", _ => "unknown" };
        return string.Concat(new[] { value.Text, ranges, kind }.Select(part => $"{System.Text.Encoding.UTF8.GetByteCount(part)}:{part}"));
    }
    private static T Merge<T>(T before, T current, T edited, Func<T, T, bool>? equal = null)
    { equal ??= EqualityComparer<T>.Default.Equals; if (equal(edited, before)) return current; if (!equal(current, before) && !equal(current, edited)) throw Conflict(); return edited; }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
}
