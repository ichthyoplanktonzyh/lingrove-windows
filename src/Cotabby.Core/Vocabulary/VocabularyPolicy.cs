using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Vocabulary;

/// <summary>Uncommitted save result; a future archive validates and atomically publishes Document.</summary>
public sealed record VocabularySaveResult(VocabularyDocument Document, Guid EntryId);

/// <summary>
/// Mac capture save and explanation-attachment rules over immutable drafts. Callers provide current
/// notebook values; policies resolve current ownership and return replacements, with no file, model
/// or live editor access. Exceptions publish nothing. A result is not a durable transaction receipt.
/// </summary>
public static class VocabularyPolicy
{
    public static bool Contains(VocabularyCapture capture, IReadOnlyList<VocabularyEntry> entries)
    {
        var entry = entries.FirstOrDefault(entry => VocabularyCapturePolicy.EntryKey(entry) == VocabularyCapturePolicy.EntryKey(capture));
        return entry is not null && VocabularySensePolicy.Destination(capture, entry) is not null;
    }

    public static VocabularySaveResult Save(VocabularyCapture capture, VocabularyDocument document, DateTimeOffset now)
    {
        VocabularyCapturePolicy.Validate(capture);
        string key = VocabularyCapturePolicy.EntryKey(capture);
        if (capture.Example is { } example && document.Entries.Any(entry => VocabularyCapturePolicy.EntryKey(entry) != key
            && entry.Examples.Any(usage => usage.Id == example.Id))) capture = capture with { Example = example.CopiedWithNewId() };
        var entries = document.Entries.ToList();
        int index = entries.FindIndex(entry => VocabularyCapturePolicy.EntryKey(entry) == key);
        if (index < 0)
        {
            if (capture.SenseId is not null) throw new VocabularyException(VocabularyFailure.InvalidContent);
            entries.Add(new(Guid.NewGuid(), capture.Expression, capture.DictionaryForm, capture.Language, capture.Kind, [], now, now));
            index = entries.Count - 1;
        }
        entries[index] = VocabularySensePolicy.Save(capture, entries[index], now);
        return new(document with { Entries = entries }, entries[index].Id);
    }

    public static bool Accepts(LearningExplanationRecord record, VocabularyEntry entry) => entry.Senses.Any(sense =>
        sense.IsSavedWithoutContext && LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, null, sense)) == record.Input
        || sense.Examples.Any(example => LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, example, sense)) == record.Input));

    public static VocabularyEntry StoreExplanation(LearningExplanationRecord record, VocabularyEntry entry)
    {
        if (!Enum.IsDefined(record.Language) || !LearningExplanationPolicy.IsValid(record.Input) || !LearningExplanationPolicy.IsValid(record.Result)) return entry;
        var senses = entry.Senses.ToArray();
        for (int index = 0; index < senses.Length; index++)
        {
            var original = entry.Senses[index]; var next = original;
            if (original.IsSavedWithoutContext && LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, null, original)) == record.Input)
            {
                next = next with { Explanations = Append(record, original.Explanations) };
                // A first general result may fill an empty summary. Refresh never redefines a
                // stable nonempty core meaning, even when its earlier wording was AI-authored.
                if (original.Meaning.Length == 0 && original.MeaningOrigin != VocabularyMeaningOrigin.User)
                    next = next with { Meaning = record.Result.Meaning, MeaningLanguage = record.Language, MeaningOrigin = VocabularyMeaningOrigin.Ai };
            }
            var usages = original.Examples.ToArray();
            for (int usage = 0; usage < usages.Length; usage++)
            {
                var before = original.Examples[usage];
                if (LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, before, original)) != record.Input) continue;
                var updated = before with { Explanations = Append(record, before.Explanations) };
                if (before.MeaningOrigin != VocabularyMeaningOrigin.User)
                    updated = updated with { Meaning = record.Result.Meaning, MeaningLanguage = record.Language, MeaningOrigin = VocabularyMeaningOrigin.Ai };
                usages[usage] = updated;
            }
            // Mac attachment does not change created/updated timestamps or authored translations.
            senses[index] = next with { Examples = usages };
        }
        return entry with { Senses = senses };
    }

    private static IReadOnlyList<LearningExplanationRecord> Append(LearningExplanationRecord record, IReadOnlyList<LearningExplanationRecord> records) =>
        records.Where(value => value.Input != record.Input || value.Language != record.Language).Append(record).TakeLast(24).ToArray();
}
