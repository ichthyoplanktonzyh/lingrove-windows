using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.LanguageLearning;

public static partial class LearningExplanationPolicy
{
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
