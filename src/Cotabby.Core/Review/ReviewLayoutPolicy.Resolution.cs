using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>Original finite source dispatch resolves a saved layout into detached card parts.
/// Missing references fail visibly; preview resolves only its requested side. Callers own drafts,
/// the notebook owns source content, and this stateless boundary starts no service or media work.</summary>
public static partial class ReviewLayoutPolicy
{
    public const string Model = "Lingrove Flexible v1";
    public const string Css = AnkiNoteTemplate.Css + "\n.card section { margin: 12px 0; }" + " img { max-width:100%; max-height:480px; object-fit:contain; }";
    public static IReadOnlyDictionary<ReviewField, string> Mapping { get; } = CollectionValues.Map(new Dictionary<ReviewField, string>
    { [ReviewField.Identity] = "LingroveID", [ReviewField.Expression] = "Front", [ReviewField.Meaning] = "Back" });
    public static IReadOnlyDictionary<string, AnkiValue> Templates { get; } = CollectionValues.Map(new Dictionary<string, AnkiValue>
    { ["Card"] = AnkiValue.Strings(new Dictionary<string, string> { ["Front"] = "{{Front}}", ["Back"] = "{{Back}}" }) });
    public static ReviewLayout Preset(int index) => index switch
    {
        1 => new([ReviewCardPart.Create(ReviewContentSource.GapSentence)], [ReviewCardPart.Create(ReviewContentSource.Expression), ReviewCardPart.Create(ReviewContentSource.Meaning)]),
        2 => new([ReviewCardPart.Create(ReviewContentSource.SenseImage)], [ReviewCardPart.Create(ReviewContentSource.Expression), ReviewCardPart.Create(ReviewContentSource.Meaning)]),
        3 => new(),
        _ => new([ReviewCardPart.Create(ReviewContentSource.Expression)], [ReviewCardPart.Create(ReviewContentSource.Meaning)]),
    };
    public static ReviewLayout Portable(ReviewLayout layout)
    {
        static ReviewCardPart Copy(ReviewCardPart part) => part with { Id = Guid.NewGuid(), BlockId = null, Content = part.Content.Select(block => block with { Id = Guid.NewGuid() }).ToArray() };
        return new(layout.Front.Select(Copy).ToArray(), layout.Back.Select(Copy).ToArray());
    }
    public static ReviewLayout Applying(ReviewLayout layout, VocabularySense sense)
    {
        var result = Portable(layout);
        ReviewCardPart Bind(ReviewCardPart part) => part.Source is ReviewContentSource.SenseText or ReviewContentSource.SenseImage
            ? part with { BlockId = sense.Content?.FirstOrDefault(block => Matches(block, part.Source))?.Id } : part;
        return new(result.Front.Select(Bind).ToArray(), result.Back.Select(Bind).ToArray());
    }
    public static bool Matches(VocabularyContentBlock block, ReviewContentSource source) => (source, block.Content) switch
    { (ReviewContentSource.SenseText, VocabularyBlockContent.Text) or (ReviewContentSource.SenseImage, VocabularyBlockContent.Image) => true, _ => false };

    public static ReviewCardContent Content(VocabularyEntry entry, VocabularySense sense, ReviewPlan plan, bool? previewBack = null)
    {
        var layout = plan.Layout ?? throw new VocabularyContentValidationException(); Validate(layout);
        var example = sense.Examples.FirstOrDefault(value => value.Id == plan.ExampleId);
        ReviewResolvedPart Resolve(ReviewCardPart part)
        {
            ReviewResolvedPart Text(string? value) => value is not null && LearningExplanationText.Trim(value).Length != 0
                ? new ReviewResolvedPart.Text(value) : throw new AnkiReviewException("Card content is missing. Choose its source again. · " + part.Source.Label());
            VocabularyContentBlock Caption(VocabularyContentBlock block) => block.Content is VocabularyBlockContent.Image image && !part.ShowCaption
                ? block with { Content = image with { Caption = "" } } : block;
            switch (part.Source)
            {
                case ReviewContentSource.Expression: return Text(entry.Expression);
                case ReviewContentSource.Meaning:
                    string core = LearningExplanationText.Trim(sense.Meaning); return Text(core.Length == 0 ? example?.Meaning : core);
                case ReviewContentSource.Sentence: return Text(example?.Text);
                case ReviewContentSource.Translation: return Text(example?.Translation);
                case ReviewContentSource.ContextualMeaning: return Text(example?.Meaning);
                case ReviewContentSource.Explanation:
                    var records = plan.ExampleId is null ? sense.Explanations : example?.Explanations;
                    return Text(records?.LastOrDefault()?.Result.Explanation);
                case ReviewContentSource.Notes: return Text(sense.Note);
                case ReviewContentSource.GapSentence: return Text(example is null ? null : ReviewCardPolicy.Gap(example.Text, example.Ranges, entry.Kind)?.Prompt);
                case ReviewContentSource.ExpressionAudio: return new ReviewResolvedPart.Audio(ReviewAudioKind.Expression);
                case ReviewContentSource.SentenceAudio: _ = Text(example?.Text); return new ReviewResolvedPart.Audio(ReviewAudioKind.Sentence);
                case ReviewContentSource.Custom:
                    return VocabularyContentPolicy.Meaningful(part.Content) ? new ReviewResolvedPart.Blocks(part.Content.Select(Caption).ToArray()) : Text(null);
                case ReviewContentSource.SenseText: case ReviewContentSource.SenseImage:
                    var block = sense.Content?.FirstOrDefault(value => value.Id == part.BlockId && Matches(value, part.Source));
                    return block is not null ? new ReviewResolvedPart.Blocks([Caption(block)]) : Text(null);
                default: throw new VocabularyContentValidationException();
            }
        }
        if (previewBack is null && (layout.Front.Count == 0 || layout.Back.Count == 0)) throw new AnkiReviewException("Add content to both sides of the card.");
        ReviewResolvedPart[] front = previewBack == true ? [] : layout.Front.Select(Resolve).ToArray();
        ReviewResolvedPart[] back = previewBack == false ? [] : layout.Back.Select(Resolve).ToArray();
        var blocks = front.Concat(back).OfType<ReviewResolvedPart.Blocks>().SelectMany(part => part.Values).ToArray();
        return new(entry.Expression, sense.Meaning, example?.Text ?? "", [], example?.Translation ?? "", "", "", "", "", true, false)
        { SenseContent = blocks, Front = front, Back = back };
    }
    public static string Html(IReadOnlyList<ReviewResolvedPart> parts, IReadOnlyDictionary<ReviewField, string> audio) => string.Concat(parts.Select(part =>
        "<section>" + (part switch
        {
            ReviewResolvedPart.Text text => ReviewCardPolicy.Html(text.Value),
            ReviewResolvedPart.Blocks blocks => VocabularyContentPolicy.RenderOccurrencesHtml(blocks.Values),
            ReviewResolvedPart.Audio value => audio.GetValueOrDefault(value.Kind.Field()) ?? "",
            _ => throw new VocabularyContentValidationException(),
        }) + "</section>"));
    public static string Preview(IReadOnlyList<ReviewResolvedPart> parts)
    {
        var audio = Enum.GetValues<ReviewAudioKind>().ToDictionary(kind => kind.Field(), kind => "🔊 " + ReviewCardPolicy.Html(kind.Label()));
        return "<style>" + Css + "</style><div class='card'>" + Html(parts, audio) + "</div>";
    }
}
