using FrozenCompleteness = Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Actual acknowledged import counts and stable notebook selection.</summary>
public sealed record VocabularyEntryImportResult(int AddedEntries, int AddedSenses, int SkippedRows,
    int AddedExamples, int AddedTranslations, int ChangedCollections, VocabularySelection? Selection)
{
    public bool HasChanges => AddedSenses > 0 || AddedExamples > 0 || AddedTranslations > 0 || ChangedCollections > 0;
}
public sealed record VocabularyEntryImportPlan(VocabularyDocument Document, VocabularyEntryImportResult Result);

/// <summary>Frozen append-only import plan. Archive owners must re-run it under their write lock;
/// a preview is not a commit. Joined edits are projected back into the canonical resources.</summary>
public static class VocabularyEntryImportPolicy
{
    private readonly record struct SenseKey(string Meaning, LearningLanguage? Language, string Note, string Tags);
    private readonly record struct Location(int Sense, int Example);
    private sealed class EntryIndex
    {
        internal readonly Dictionary<SenseKey, List<int>> Senses = [];
        internal readonly Dictionary<string, Location> Examples = new(StringComparer.Ordinal);
        internal EntryIndex(VocabularyEntry entry) { for (int i = 0; i < entry.Senses.Count; i++) Include(entry.Senses[i], i); }
        internal void Include(VocabularySense sense, int index)
        {
            foreach (var key in Keys(sense).Distinct())
            { if (!Senses.TryGetValue(key, out var found)) Senses[key] = found = []; found.Add(index); }
            for (int i = 0; i < sense.Examples.Count; i++) Examples[ExampleKey(sense.Examples[i])] = new(index, i);
        }
    }
    private static string Canonical(string value) => FoundationUnicodeNormalization.Normalize(value);
    private static string Tags(IEnumerable<string> tags) => string.Concat(tags.Select(Canonical).Order(StringComparer.Ordinal).Select(x => $"{x.Length}:{x}"));
    private static IEnumerable<SenseKey> Keys(VocabularySense sense)
    {
        var note = Canonical(FoundationText.TrimWhitespaceAndNewlines(sense.Note)); var tags = Tags(sense.Tags);
        foreach (var item in new[] { new VocabularyMeaning(sense.Meaning, sense.MeaningLanguage) }.Concat(sense.StandaloneMeanings).Concat(sense.AlternateMeanings))
        {
            string meaning = VocabularyCapturePolicy.Normalized(item.Text);
            yield return new(meaning, meaning.Length == 0 ? null : item.Language, note, tags);
            yield return new(meaning, null, note, tags);
        }
    }
    private static string ExampleKey(VocabularyExample example)
    {
        var spans = string.Join(",", example.Ranges.Select(x => $"{x.Location}:{x.Length}"));
        // Swift key includes the exact text, ordered UTF-16 ranges and completeness.
        string code = example.Completeness switch { FrozenCompleteness.Sentence => "sentence", FrozenCompleteness.Fragment => "fragment", _ => "unknown" };
        return string.Concat(new[] { example.Text, spans, code }.Select(x => $"{System.Text.Encoding.UTF8.GetByteCount(x)}:{x}"));
    }
    public static VocabularyEntryImportPlan Plan(IReadOnlyList<VocabularyEntryInput> inputs, VocabularyDocument document, SourceDate now, CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) throw new VocabularyWordListException(VocabularyWordListError.Empty);
        if (inputs.Count > VocabularyWordListPolicy.MaximumRows) throw new VocabularyWordListException(VocabularyWordListError.TooLarge);
        var entries = document.Entries.ToList();
        var offsets = entries.Select((x, i) => (Key: VocabularyArchiveValidation.EntryKey(x), Index: i)).ToDictionary(x => x.Key, x => x.Index);
        var indices = new Dictionary<int, EntryIndex>();
        // Keep growing example lists mutable during planning, freezing each once after the batch.
        var senseLists = new Dictionary<int, List<VocabularySense>>();
        List<VocabularySense> Senses(int entry)
        {
            if (!senseLists.TryGetValue(entry, out var values)) senseLists[entry] = values = entries[entry].Senses.ToList();
            return values;
        }
        var exampleLists = new Dictionary<(int Entry, int Sense), List<VocabularyExample>>();
        List<VocabularyExample> Examples(int entry, int sense)
        {
            var key = (entry, sense);
            if (!exampleLists.TryGetValue(key, out var values))
                exampleLists[key] = values = Senses(entry)[sense].Examples.ToList();
            return values;
        }
        var collect = new Dictionary<Guid, HashSet<Guid>>();
        int addedEntries = 0, addedSenses = 0, skipped = 0, addedExamples = 0, addedTranslations = 0, changedCollections = 0;
        VocabularySelection? selection = null;
        bool HasChanges() => addedSenses > 0 || addedExamples > 0 || addedTranslations > 0 || changedCollections > 0;
        void Collect(Guid sense, Guid? usage = null)
        { if (!collect.TryGetValue(sense, out var ids)) collect[sense] = ids = []; if (usage is { } id) ids.Add(id); }
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = VocabularyEntryInputPolicy.Validated(input);
            var candidate = new VocabularyEntry(Guid.NewGuid(), value.Expression, value.DictionaryForm, value.Language, value.Kind, [], now, now);
            var entryKey = VocabularyArchiveValidation.EntryKey(candidate);
            if (!offsets.TryGetValue(entryKey, out int index)) { index = entries.Count; offsets.Add(entryKey, index); entries.Add(candidate); addedEntries++; }
            var entry = entries[index];
            if (!indices.TryGetValue(index, out var lookup)) indices[index] = lookup = new(entry);
            var key = new SenseKey(VocabularyCapturePolicy.Normalized(value.Meaning), value.Meaning.Length == 0 ? null : value.MeaningLanguage, Canonical(value.Note), Tags(value.Tags));
            var matching = lookup.Senses.GetValueOrDefault(key) ?? [];
            if (value.Example.Length != 0)
            {
                var example = new VocabularyExample(Guid.NewGuid(), value.Example, value.ExampleTranslation,
                    VocabularyPolicy.ReconciledRanges(value.Example, value.Expression, value.DictionaryForm, cancellationToken), FrozenCompleteness.Sentence, "", now)
                    { SourceKind = VocabularySourceKind.Imported };
                string exampleKey = ExampleKey(example); bool newSense = false, newExample = false, translation = false; Location location;
                var senses = Senses(index);
                if (lookup.Examples.TryGetValue(exampleKey, out location))
                {
                    if ((value.Meaning.Length != 0 || value.Note.Length != 0 || value.Tags.Count != 0) && !matching.Contains(location.Sense))
                        throw new VocabularyWordListException(VocabularyWordListError.ExampleConflict);
                    var sense = senses[location.Sense]; var examples = Examples(index, location.Sense); var existing = examples[location.Example];
                    if (example.Translation.Length != 0 && !CollectionValues.Same(existing.Translation, example.Translation)
                        && !existing.TranslationVariants.Any(x => CollectionValues.Same(x.Text, example.Translation)))
                    {
                        existing = existing.Translation.Length == 0 ? existing with { Translation = example.Translation, TranslationLanguage = null }
                            : existing with { TranslationVariants = existing.TranslationVariants.Append(new VocabularyMeaning(example.Translation, null)).ToArray() };
                        examples[location.Example] = existing; senses[location.Sense] = sense with { UpdatedAt = now };
                        entry = entry with { UpdatedAt = now }; translation = true;
                    }
                }
                else
                {
                    int target = value.Meaning.Length != 0 && matching.Count == 1 ? matching[0] : senses.Count;
                    if (target == senses.Count)
                    {
                        senses.Add(NewSense(value, now, false)); newSense = true;
                    }
                    var sense = senses[target];
                    if (newSense) exampleLists[(index, target)] = [];
                    var examples = newSense ? exampleLists[(index, target)] : Examples(index, target);
                    location = new(target, examples.Count); examples.Add(example);
                    senses[target] = sense with { UpdatedAt = now };
                    entry = entry with { UpdatedAt = now }; newExample = true; translation = example.Translation.Length != 0;
                    if (newSense) lookup.Include(senses[target], target);
                    lookup.Examples[exampleKey] = location;
                }
                bool changed = newSense || newExample || translation;
                var senseId = senses[location.Sense].Id;
                if ((changed && !HasChanges()) || selection is null) selection = new(entry.Id, senseId);
                if (newSense) addedSenses++; if (newExample) addedExamples++; if (translation) addedTranslations++; if (!changed) skipped++;
                entries[index] = entry; Collect(senseId, Examples(index, location.Sense)[location.Example].Id);
                continue;
            }
            bool bare = value.Meaning.Length == 0 && value.Note.Length == 0 && value.Tags.Count == 0;
            var currentSenses = Senses(index);
            if (currentSenses.Count != 0 && (bare || matching.Count != 0))
            {
                Guid senseId = currentSenses[matching.Count == 0 ? 0 : matching[0]].Id; Collect(senseId); skipped++;
                selection ??= new(entry.Id, senseId); continue;
            }
            var created = NewSense(value, now, true); currentSenses.Add(created); entry = entry with { UpdatedAt = now };
            lookup.Include(created, currentSenses.Count - 1); entries[index] = entry;
            if (!HasChanges()) selection = new(entry.Id, created.Id); addedSenses++;
        }
        foreach (var item in exampleLists)
        {
            var senses = Senses(item.Key.Entry);
            senses[item.Key.Sense] = senses[item.Key.Sense] with { Examples = item.Value };
        }
        foreach (var (index, senses) in senseLists) entries[index] = entries[index] with { Senses = senses };
        var result = document.WithEntries(entries);
        var collections = result.Collections.ToList(); var collectionOffsets = collections.Select((x, i) => (x.SenseId, i)).ToDictionary(x => x.SenseId, x => x.i);
        foreach (var (sense, ids) in collect)
        {
            bool found = collectionOffsets.TryGetValue(sense, out int offset);
            var before = found ? collections[offset] : new VocabularyCollectionItem(sense, now);
            var after = before with { InterpretationIds = before.InterpretationIds.Concat(ids.Except(before.InterpretationIds).OrderBy(x => x.ToString("D"), StringComparer.Ordinal)).ToArray() };
            if (found) { if (before != after) { collections[offset] = after; changedCollections++; } }
            else { collections.Add(after); changedCollections++; }
        }
        result = result with { Collections = collections };
        return new(result, new(addedEntries, addedSenses, skipped, addedExamples, addedTranslations, changedCollections, selection));
    }
    private static VocabularySense NewSense(VocabularyEntryInput input, SourceDate now, bool withoutContext) =>
        new(Guid.NewGuid(), input.Meaning, now, now) { MeaningLanguage = input.MeaningLanguage,
            MeaningOrigin = input.Meaning.Length == 0 ? VocabularyMeaningOrigin.Captured : VocabularyMeaningOrigin.User,
            Note = input.Note, Tags = input.Tags, CreatedWithoutContext = withoutContext ? true : null };
}


