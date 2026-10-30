using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Review;

/// <summary>35b review material directly from the canonical joined graph, with no timestamp or
/// language conversion. Common HTML, fields, layout and gap functions remain shared.</summary>
public static class CanonicalReviewCardPolicy
{
    public static ReviewPlan InitialPlan(F.VocabularySense sense) => sense.Review?.Plan ?? ReviewPlan.Create() with { ExampleId = sense.Examples.FirstOrDefault()?.Id, IncludeSenseContent = true };
    public static ReviewCardContent Content(F.VocabularyEntry entry, F.VocabularySense sense, ReviewPlan plan, bool? previewBack = null)
    {
        if (plan.Layout is not null) return LayoutContent(entry, sense, plan, previewBack);
        var example = sense.Examples.FirstOrDefault(value => value.Id == plan.ExampleId);
        if (plan.ExampleId is not null && example is null) throw new AnkiReviewException(AnkiReviewFailure.MissingExample);
        string contextual = LearningExplanationText.Trim(example?.Meaning ?? "");
        string fallback = contextual.Length == 0 ? sense.Meaning : contextual;
        string preferred = sense.MeaningOrigin == VocabularyMeaningOrigin.User || example is null ? sense.Meaning : fallback;
        string answer = LearningExplanationText.Trim(plan.Answer ?? preferred);
        string sentence = example?.Text ?? ""; var ranges = example?.Ranges ?? [];
        var gap = ReviewCardPolicy.Gap(sentence, ranges, entry.Kind); var blocks = plan.IncludeSenseContent == true ? sense.Content ?? [] : [];
        VocabularyContentPolicy.Validate(blocks);
        if (answer.Length == 0 && !VocabularyContentPolicy.Meaningful(blocks)) throw new AnkiReviewException(AnkiReviewFailure.MissingAnswer);
        if ((!plan.Recognition && !plan.Gap) || plan.Gap && gap is null) throw new AnkiReviewException(AnkiReviewFailure.InvalidGap);
        var records = example?.Explanations ?? sense.Explanations;
        return new(entry.Expression, answer, sentence, ranges, example?.Translation ?? "", plan.IncludeExplanation ? records.LastOrDefault()?.Result.Explanation ?? "" : "",
            plan.IncludeNotes ? sense.Note : "", gap?.Prompt ?? "", gap?.Answer ?? "", plan.Recognition, plan.Gap) { SenseContent = blocks };
    }
    public static ReviewLayout Applying(ReviewLayout layout, F.VocabularySense sense)
    {
        var copy = ReviewLayoutPolicy.Portable(layout);
        ReviewCardPart Bind(ReviewCardPart part) => part.Source is ReviewContentSource.SenseText or ReviewContentSource.SenseImage
            ? part with { BlockId = sense.Content?.FirstOrDefault(block => ReviewLayoutPolicy.Matches(block, part.Source))?.Id } : part;
        return new(copy.Front.Select(Bind).ToArray(), copy.Back.Select(Bind).ToArray());
    }
    private static ReviewCardContent LayoutContent(F.VocabularyEntry entry, F.VocabularySense sense, ReviewPlan plan, bool? previewBack)
    {
        var layout = plan.Layout ?? throw new VocabularyContentValidationException(); ReviewLayoutPolicy.Validate(layout);
        var example = sense.Examples.FirstOrDefault(value => value.Id == plan.ExampleId);
        ReviewResolvedPart Resolve(ReviewCardPart part)
        {
            ReviewResolvedPart Text(string? value) => value is not null && LearningExplanationText.Trim(value).Length != 0
                ? new ReviewResolvedPart.Text(value) : throw new AnkiReviewException(AnkiReviewFailure.MissingCardContent, part.Source);
            VocabularyContentBlock Caption(VocabularyContentBlock block) => block.Content is VocabularyBlockContent.Image image && !part.ShowCaption ? block with { Content = image with { Caption = "" } } : block;
            switch (part.Source)
            {
                case ReviewContentSource.Expression: return Text(entry.Expression);
                case ReviewContentSource.Meaning: string core = LearningExplanationText.Trim(sense.Meaning); return Text(core.Length == 0 ? example?.Meaning : core);
                case ReviewContentSource.Sentence: return Text(example?.Text);
                case ReviewContentSource.Translation: return Text(example?.Translation);
                case ReviewContentSource.ContextualMeaning: return Text(example?.Meaning);
                case ReviewContentSource.Explanation: var records = plan.ExampleId is null ? sense.Explanations : example?.Explanations; return Text(records?.LastOrDefault()?.Result.Explanation);
                case ReviewContentSource.Notes: return Text(sense.Note);
                case ReviewContentSource.GapSentence: return Text(example is null ? null : ReviewCardPolicy.Gap(example.Text, example.Ranges, entry.Kind)?.Prompt);
                case ReviewContentSource.ExpressionAudio: return new ReviewResolvedPart.Audio(ReviewAudioKind.Expression);
                case ReviewContentSource.SentenceAudio: _ = Text(example?.Text); return new ReviewResolvedPart.Audio(ReviewAudioKind.Sentence);
                case ReviewContentSource.Custom: return VocabularyContentPolicy.Meaningful(part.Content) ? new ReviewResolvedPart.Blocks(part.Content.Select(Caption).ToArray()) : Text(null);
                case ReviewContentSource.SenseText: case ReviewContentSource.SenseImage:
                    var block = sense.Content?.FirstOrDefault(value => value.Id == part.BlockId && ReviewLayoutPolicy.Matches(value, part.Source));
                    return block is not null ? new ReviewResolvedPart.Blocks([Caption(block)]) : Text(null);
                default: throw new VocabularyContentValidationException();
            }
        }
        if (previewBack is null && (layout.Front.Count == 0 || layout.Back.Count == 0)) throw new AnkiReviewException(AnkiReviewFailure.EmptyCardSides);
        var front = previewBack == true ? [] : layout.Front.Select(Resolve).ToArray(); var back = previewBack == false ? [] : layout.Back.Select(Resolve).ToArray();
        var blocks = front.Concat(back).OfType<ReviewResolvedPart.Blocks>().SelectMany(part => part.Values).ToArray();
        return new(entry.Expression, sense.Meaning, example?.Text ?? "", [], example?.Translation ?? "", "", "", "", "", true, false) { SenseContent = blocks, Front = front, Back = back };
    }
}
