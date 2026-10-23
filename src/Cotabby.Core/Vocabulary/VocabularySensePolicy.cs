using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>Uncommitted usage move retains both the replacement entry and actual destination ID.</summary>
public sealed record VocabularyMoveResult(VocabularyEntry Entry, Guid TargetSenseId);

/// <summary>
/// Pure Mac ownership rules for entry → sense → usage. Stable IDs and exact captured usage keys
/// resolve destinations; generated labels/order never infer membership. Immutable replacement
/// adapts Swift inout drafts so failed operations cannot mutate a caller's published snapshot.
/// A future archive owns whole-graph validation, conflict comparison and durable commit.
/// </summary>
public static class VocabularySensePolicy
{
    public static int? Index(Guid id, VocabularyEntry entry)
    {
        for (int index = 0; index < entry.Senses.Count; index++)
            if (entry.Senses[index].Id == id || entry.Senses[index].MergedFrom.Contains(id)) return index;
        return null;
    }
    public static VocabularyExplanationDestination? Destination(VocabularyCapture capture, VocabularyEntry entry)
    {
        var candidates = entry.Senses.Where(sense =>
            (capture.SenseId is null || sense.Id == capture.SenseId || sense.MergedFrom.Contains(capture.SenseId.Value))
            && (capture.Example is { } example ? sense.Examples.Any(usage => Key(usage) == Key(example)) : sense.IsSavedWithoutContext)).ToArray();
        if (candidates.Length != 1) return null;
        var owner = candidates[0];
        var usage = capture.Example is { } target ? owner.Examples.FirstOrDefault(example => Key(example) == Key(target)) : null;
        return new(entry.Id, owner.Id, usage?.Id);
    }

    public static VocabularyEntry Save(VocabularyCapture capture, VocabularyEntry entry, DateTimeOffset now)
    {
        var senses = entry.Senses.ToList(); int index;
        if (capture.SenseId is { } id) index = Index(id, entry) ?? throw Invalid();
        else if (Destination(capture, entry) is { } destination && Index(destination.SenseId, entry) is { } found) index = found;
        else
        {
            senses.Add(new(Guid.NewGuid(), capture.Example is null ? capture.Meaning : "", now, now)
                { MeaningLanguage = capture.Example is null ? capture.MeaningLanguage : null });
            index = senses.Count - 1;
        }
        var sense = senses[index];
        if (capture.Example is { } example)
        {
            if (senses.Where((_, offset) => offset != index).Any(other => other.Examples.Any(usage => Key(usage) == Key(example)))) throw Conflict();
            var examples = sense.Examples.ToList(); int existing = examples.FindIndex(usage => Key(usage) == Key(example));
            if (existing >= 0) examples[existing] = AddTranslation(example.Translation, example.TranslationLanguage, examples[existing]);
            else
            {
                if (entry.Examples.Any(usage => usage.Id == example.Id)) example = example.CopiedWithNewId();
                examples.Add(AddTranslation(example.Translation, example.TranslationLanguage, example));
            }
            sense = sense with { Examples = examples };
        }
        else
        {
            if (senses.Where((_, offset) => offset != index).Any(other => other.IsSavedWithoutContext)) throw Conflict();
            var meaning = new VocabularyMeaning(capture.Meaning, capture.MeaningLanguage);
            sense = sense with { IsSavedWithoutContext = true, StandaloneMeanings = sense.StandaloneMeanings.Contains(meaning)
                ? sense.StandaloneMeanings : sense.StandaloneMeanings.Append(meaning).ToArray() };
        }
        senses[index] = sense with { UpdatedAt = now };
        var result = entry with { Senses = senses, UpdatedAt = now };
        return capture.Explanation is { } record ? VocabularyPolicy.StoreExplanation(record, result) : result;
    }

    public static VocabularyEntry Merge(Guid source, Guid target, VocabularyEntry entry, DateTimeOffset now)
    {
        var senses = entry.Senses.ToList(); int from = senses.FindIndex(sense => sense.Id == source), to = senses.FindIndex(sense => sense.Id == target);
        if (from < 0 || to < 0 || from == to) throw Invalid();
        var original = senses[from]; var merged = senses[to];
        if (original.Examples.Concat(merged.Examples).Select(Key).Distinct(StringComparer.Ordinal).Count() != original.Examples.Count + merged.Examples.Count) throw Conflict();
        var alternate = merged.AlternateMeanings.ToList();
        if (merged.Meaning.Length == 0)
            merged = merged with { Meaning = original.Meaning, MeaningLanguage = original.MeaningLanguage, MeaningOrigin = original.MeaningOrigin };
        else if (original.Meaning.Length != 0 && (!CanonicalStringComparer.Instance.Equals(original.Meaning, merged.Meaning) || original.MeaningLanguage != merged.MeaningLanguage))
            alternate.Add(new(original.Meaning, original.MeaningLanguage));
        alternate.AddRange(original.AlternateMeanings);
        string note = merged.Note;
        if (original.Note.Length != 0 && !CanonicalStringComparer.Instance.Equals(original.Note, note))
            note = string.Join("\n\n", new[] { note, original.Note }.Where(value => value.Length != 0));
        var targetContent = merged.Content ?? [];
        merged = merged with
        {
            AlternateMeanings = alternate, Note = note,
            Tags = merged.Tags.Concat(original.Tags).Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance).ToArray(),
            Content = targetContent.Concat((original.Content ?? []).Where(block => !targetContent.Any(value => value.Id == block.Id))).ToArray(),
            Examples = merged.Examples.Concat(original.Examples).ToArray(),
            StandaloneMeanings = merged.StandaloneMeanings.Concat(original.StandaloneMeanings.Where(value => !merged.StandaloneMeanings.Contains(value))).ToArray(),
            IsSavedWithoutContext = merged.IsSavedWithoutContext || original.IsSavedWithoutContext,
            Explanations = merged.Explanations.Concat(original.Explanations.Where(record => !merged.Explanations.Any(value => value.Id == record.Id))).ToArray(),
            MergedFrom = merged.MergedFrom.Concat(new[] { original.Id }).Concat(original.MergedFrom).ToArray(),
            ReviewCards = (merged.ReviewCards ?? []).Concat(original.ReviewCards ?? []).ToArray(),
            Review = AnkiReviewPolicy.Merged(merged.Review, original.Review), UpdatedAt = now,
        };
        senses[to] = merged; senses.RemoveAt(from);
        return entry with { Senses = senses, UpdatedAt = now };
    }

    public static VocabularyMoveResult Move(Guid exampleId, Guid source, Guid? target, VocabularyEntry entry, DateTimeOffset now)
    {
        var senses = entry.Senses.ToList(); int from = senses.FindIndex(sense => sense.Id == source);
        if (from < 0 || senses[from].Examples.FirstOrDefault(example => example.Id == exampleId) is not { } usage) throw Conflict();
        Guid destination;
        if (target is { } id)
        {
            int to = senses.FindIndex(sense => sense.Id == id);
            if (to < 0 || to == from) throw Invalid();
            if (senses[to].Examples.Any(example => Key(example) == Key(usage))) throw Conflict();
            destination = senses[to].Id;
            senses[to] = senses[to] with { Examples = senses[to].Examples.Append(usage).ToArray(), UpdatedAt = now };
        }
        else
        {
            destination = Guid.NewGuid();
            senses.Add(new(destination, "", now, now) { Examples = [usage] });
        }
        // An empty source still owns notes/review/identity. Only explicit deletion removes it.
        senses[from] = senses[from] with { Examples = senses[from].Examples.Where(example => example.Id != exampleId).ToArray(), UpdatedAt = now };
        return new(entry with { Senses = senses, UpdatedAt = now }, destination);
    }

    private static VocabularyExample AddTranslation(string text, LearningLanguage? language, VocabularyExample example)
    {
        if (text.Length == 0) return example;
        var variant = new VocabularyMeaning(text, language);
        var result = example.TranslationVariants.Contains(variant) ? example : example with { TranslationVariants = example.TranslationVariants.Append(variant).ToArray() };
        return example.Translation.Length != 0 ? result : result with { Translation = text, TranslationLanguage = language };
    }
    private static string Key(VocabularyExample example) => VocabularyCapturePolicy.ExampleKey(example);
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
}
