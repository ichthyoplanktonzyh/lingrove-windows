using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Source-derived detached reading captures; callers supply actual platform word and
/// sentence ranges and retain receipt authority separately from this pure projection.</summary>
public static class ReadingVocabularyCaptureFactory
{
    public static VocabularyCapture Translation(ReadingSelection selection, LearningLanguage? source, LearningLanguage target,
        string translation, int wordCount, IReadOnlyList<LearningTextRange> sentenceRanges, DateTimeOffset now)
    {
        ReadingSelectionPolicy.ValidateReceipt(selection);
        if (wordCount < 0 || string.IsNullOrWhiteSpace(translation)) throw new ArgumentException("Translation is unavailable.");
        string text = selection.Text;
        var last = new GraphemeString(text).LastOrDefault().ToString();
        var kind = wordCount == 1 ? VocabularyEntryKind.Word : last is "." or "!" or "?" or "。" or "！" or "？"
            ? VocabularyEntryKind.Sentence : VocabularyEntryKind.Expression;
        VocabularyExample? example = null;
        if (kind == VocabularyEntryKind.Sentence)
        {
            var boundaries = FoundationTextRanges.Boundaries(text); int previousEnd = 0;
            foreach (var range in sentenceRanges)
            {
                if (range.Location < previousEnd || !FoundationTextRanges.IsValid(range.Location, range.Length, text, boundaries))
                    throw new ArgumentException("Sentence evidence is unavailable.");
                previousEnd = range.Location + range.Length;
            }
            bool complete = sentenceRanges.Any(range => range.Location == 0 && range.Length == text.Length);
            example = new VocabularyExample(Guid.NewGuid(), text, translation, [new(0, text.Length)],
                complete ? VocabularyExampleCompleteness.Sentence : VocabularyExampleCompleteness.Fragment, "", now,
                translationLanguage: target);
        }
        return new(text, text, source, kind, translation, target, example);
    }
    public static VocabularyCapture Lookup(ReadingLookupInput input, ReadingLookupResult result, LearningLanguage meaningLanguage) =>
        new(input.Target, input.Target, input.Source, input.IsWord ? VocabularyEntryKind.Word : VocabularyEntryKind.Expression,
            result.Meaning, meaningLanguage) { AllowsExplanationEnrichment = false };
}
