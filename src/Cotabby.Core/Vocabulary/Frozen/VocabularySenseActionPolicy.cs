using Cotabby.Core.Support;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary.Resources;
using ExplanationInput = Cotabby.Core.Vocabulary.Resources.LearningExplanationInput;
using ExplanationContext = Cotabby.Core.Vocabulary.Resources.LearningExplanationContext;

namespace Cotabby.Core.Vocabulary.Frozen;

public sealed record VocabularySenseDraft(VocabularyDocument Document, Guid? DestinationId = null);

/// <summary>Frozen sense/usage ownership actions over the complete joined canonical graph.</summary>
public static class VocabularySenseActionPolicy
{
    public static VocabularySenseDraft Create(VocabularyDocument document, Guid entryId, Guid senseId, SourceDate now)
    {
        var entries = document.Entries.ToArray(); int index = Array.FindIndex(entries, entry => entry.Id == entryId);
        if (index < 0) throw Invalid();
        entries[index] = entries[index] with { Senses = entries[index].Senses.Append(new VocabularySense(senseId, "", now, now)).ToArray(), UpdatedAt = now };
        return new(document.WithEntries(entries), senseId);
    }
    public static VocabularySenseDraft Remove(VocabularyDocument document, Guid entryId, Guid senseId)
    {
        if (!document.Catalog.Any(entry => entry.Id == entryId && entry.Senses.Any(sense => sense.Id == senseId))) throw Invalid();
        return new(document with { Collections = document.Collections.Where(value => value.SenseId != senseId).ToArray() });
    }
    public static VocabularySenseDraft Move(VocabularyDocument document, Guid entryId, Guid exampleId, Guid sourceId, Guid? targetId, SourceDate now)
    {
        var entries = document.Entries.ToArray(); int entryIndex = Array.FindIndex(entries, entry => entry.Id == entryId);
        if (entryIndex < 0) throw Invalid();
        var entry = entries[entryIndex]; var senses = entry.Senses.ToList(); int from = senses.FindIndex(value => value.Id == sourceId);
        if (from < 0 || senses[from].Examples.FirstOrDefault(value => value.Id == exampleId) is not { } usage) throw Conflict();
        var selected = document.Collections.FirstOrDefault(value => value.SenseId == sourceId)?.InterpretationIds.Contains(exampleId) == true;
        Guid destination;
        if (targetId is { } target)
        {
            int to = senses.FindIndex(value => value.Id == target); if (to < 0 || to == from) throw Invalid();
            if (senses[to].Examples.Any(value => VocabularyEntryDraftPolicy.Key(value) == VocabularyEntryDraftPolicy.Key(usage))) throw Conflict();
            destination = target; senses[to] = senses[to] with { Examples = senses[to].Examples.Append(usage).ToArray(), UpdatedAt = now };
        }
        else { destination = Guid.NewGuid(); senses.Add(new VocabularySense(destination, "", now, now) { Examples = [usage] }); }
        senses[from] = senses[from] with { Examples = senses[from].Examples.Where(value => value.Id != exampleId).ToArray(), UpdatedAt = now };
        entries[entryIndex] = entry with { Senses = senses, UpdatedAt = now };
        var next = document.WithEntries(entries);
        if (selected) next = VocabularyNotebookProjection.Collect(destination, exampleId, next, now);
        return new(next, destination);
    }
    public static VocabularySenseDraft Merge(VocabularyDocument document, Guid entryId, Guid sourceId, Guid targetId, SourceDate now)
    {
        var entries = document.Entries.ToArray(); int entryIndex = Array.FindIndex(entries, entry => entry.Id == entryId);
        if (entryIndex < 0) throw Invalid();
        var entry = entries[entryIndex]; var senses = entry.Senses.ToList();
        int from = senses.FindIndex(value => value.Id == sourceId), to = senses.FindIndex(value => value.Id == targetId);
        if (from < 0 || to < 0 || from == to) throw Invalid();
        var source = senses[from]; var target = senses[to];
        if (source.Examples.Concat(target.Examples).Select(VocabularyEntryDraftPolicy.Key).Distinct(StringComparer.Ordinal).Count() != source.Examples.Count + target.Examples.Count) throw Conflict();
        var selected = document.Collections.FirstOrDefault(value => value.SenseId == sourceId)?.InterpretationIds.ToArray() ?? [];
        var alternate = target.AlternateMeanings.ToList();
        if (target.Meaning.Length == 0) target = target with { Meaning = source.Meaning, MeaningLanguage = source.MeaningLanguage, MeaningOrigin = source.MeaningOrigin };
        else if (source.Meaning.Length != 0 && (!CollectionValues.Same(source.Meaning, target.Meaning) || source.MeaningLanguage != target.MeaningLanguage)) alternate.Add(new(source.Meaning, source.MeaningLanguage));
        alternate.AddRange(source.AlternateMeanings);
        string note = target.Note;
        if (source.Note.Length != 0 && !CollectionValues.Same(source.Note, note)) note = string.Join("\n\n", new[] { note, source.Note }.Where(value => value.Length != 0));
        var blocks = target.Content ?? [];
        target = target with { AlternateMeanings = alternate, Note = note,
            Tags = target.Tags.Concat(source.Tags).Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance).ToArray(),
            Content = blocks.Concat((source.Content ?? []).Where(value => !blocks.Any(item => item.Id == value.Id))).ToArray(),
            Examples = target.Examples.Concat(source.Examples).ToArray(),
            StandaloneMeanings = target.StandaloneMeanings.Concat(source.StandaloneMeanings.Where(value => !target.StandaloneMeanings.Contains(value))).ToArray(),
            IsSavedWithoutContext = target.IsSavedWithoutContext || source.IsSavedWithoutContext,
            CreatedWithoutContext = source.CreatedWithoutContext == true ? true : target.CreatedWithoutContext,
            Explanations = target.Explanations.Concat(source.Explanations.Where(value => !target.Explanations.Any(item => item.Id == value.Id))).ToArray(),
            MergedFrom = target.MergedFrom.Concat([source.Id]).Concat(source.MergedFrom).ToArray(),
            ReviewCards = (target.ReviewCards ?? []).Concat(source.ReviewCards ?? []).ToArray(), Review = MergeReview(target.Review, source.Review), UpdatedAt = now };
        target = target with { Explanations = Rebind(target.Explanations, entry, target) };
        senses[to] = target; senses.RemoveAt(from); entries[entryIndex] = entry with { Senses = senses, UpdatedAt = now };
        var next = document.WithEntries(entries);
        foreach (var id in selected) next = VocabularyNotebookProjection.Collect(targetId, id, next, now);
        return new(next, targetId);
    }
    private static VocabularyReview? MergeReview(VocabularyReview? target, VocabularyReview? source)
    {
        if (target is null) return source; if (source is null) return target;
        var bindings = target.Bindings.ToList();
        foreach (var binding in source.Bindings)
            if (!bindings.Any(value => CollectionValues.Same(value.Endpoint, binding.Endpoint) && CollectionValues.Same(value.Profile, binding.Profile)
                && (value.NoteId is not null && value.NoteId == binding.NoteId || value.OriginSenseId == binding.OriginSenseId))) bindings.Add(binding);
        return target with { Bindings = bindings, PreservedPlans = target.PreservedPlans.Concat([source.Plan]).Concat(source.PreservedPlans).ToArray(),
            ActiveBindingId = bindings.Count > 1 ? null : bindings.FirstOrDefault()?.Id };
    }
    private static IReadOnlyList<LearningExplanationRecord> Rebind(IReadOnlyList<LearningExplanationRecord> records, VocabularyEntry entry, VocabularySense sense)
    {
        var expected = CanonicalWritingPolicy.Input(entry, sense, null);
        var aliases = new[] { sense.Id }.Concat(sense.MergedFrom).Select(id => "sense:" + id.ToString("D").ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        return records.Select(record =>
        {
            var old = record.Input;
            if (old.ContextKind != ExplanationContext.None || old.OccurrenceIdentity is { } identity && !aliases.Contains(identity)) return record;
            var input = new ExplanationInput(old.Target, old.DictionaryForm, old.Language, old.Kind, old.Context, old.Ranges, old.ContextKind, expected.OccurrenceIdentity);
            return input == expected ? record with { Input = input } : record;
        }).ToArray();
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
}
