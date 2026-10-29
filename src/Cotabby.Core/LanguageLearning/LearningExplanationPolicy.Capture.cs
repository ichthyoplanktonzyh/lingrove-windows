using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.LanguageLearning;

public static partial class LearningExplanationPolicy
{
    /// <summary>
    /// Resolve a saved owner's current value, never an old sense array position. An explicitly
    /// supplied sense wins; otherwise example UUID or standalone ownership selects the first Mac
    /// candidate. This projection is not authority to commit a destination or resurrect deletion.
    /// </summary>
    public static VocabularyCapture Capture(VocabularyEntry entry, VocabularyExample? example, VocabularySense? sense = null)
    {
        var owner = sense ?? entry.Senses.FirstOrDefault(candidate => example is not null
            ? candidate.Examples.Any(usage => usage.Id == example.Id) : candidate.IsSavedWithoutContext);
        return new(entry.Expression, entry.DictionaryForm, entry.Language, entry.Kind,
            example?.Meaning ?? owner?.Meaning ?? "", example?.MeaningLanguage ?? owner?.MeaningLanguage,
            example, SenseId: owner?.Id);
    }

    /// <summary>
    /// Exact Mac capture-to-input projection for future saved usages. Explanation evidence borrows
    /// captured text/ranges and receipt, excluding usage UUID, translations, app provenance and
    /// authored meaning. It creates no provider, edits no record, and never recaptures an editor.
    /// Validation remains a separate whole-input gate; standalone evidence is explicitly None.
    /// </summary>
    public static LearningExplanationInput Input(VocabularyCapture capture) => new(capture.Expression, capture.DictionaryForm,
        capture.Language, capture.Kind, capture.Example?.Text ?? capture.Expression,
        capture.Example?.Ranges ?? [new VocabularyTextRange(0, capture.Expression.Length)],
        capture.Example is { } example ? example.Completeness == VocabularyExampleCompleteness.Sentence
            ? LearningExplanationContext.Sentence : LearningExplanationContext.Fragment : LearningExplanationContext.None,
        capture.Example is null ? null : VocabularyCapturePolicy.ExampleKey(capture.Example));
}
