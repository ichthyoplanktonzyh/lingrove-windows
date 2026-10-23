using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>Original deterministic sense/usage-to-review preparation. The caller supplies one
/// immutable saved entry, sense and editable plan; no dictionary, model, Store or Anki is read.
/// Exact UTF-16 ranges and escaped material flow out to preview and future send boundaries.</summary>
public static class ReviewCardPolicy
{
    public static ReviewPlan InitialPlan(VocabularySense sense) => sense.Review?.Plan ?? ReviewPlan.Create() with
    { ExampleId = sense.Examples.FirstOrDefault()?.Id, IncludeSenseContent = true };

    public static ReviewCardContent Content(VocabularyEntry entry, VocabularySense sense, ReviewPlan plan)
    {
        if (plan.Layout is not null) return ReviewLayoutPolicy.Content(entry, sense, plan);
        var example = sense.Examples.FirstOrDefault(value => value.Id == plan.ExampleId);
        if (plan.ExampleId is not null && example is null) throw new AnkiReviewException("Choose a review example again.");
        string contextual = LearningExplanationText.Trim(example?.Meaning ?? "");
        string fallback = contextual.Length == 0 ? sense.Meaning : contextual;
        string preferred = sense.MeaningOrigin == VocabularyMeaningOrigin.User || example is null ? sense.Meaning : fallback;
        string answer = LearningExplanationText.Trim(plan.Answer ?? preferred);
        string sentence = example?.Text ?? ""; var ranges = example?.Ranges ?? [];
        var gap = Gap(sentence, ranges, entry.Kind);
        var blocks = plan.IncludeSenseContent == true ? sense.Content ?? [] : [];
        VocabularyContentPolicy.Validate(blocks);
        if (answer.Length == 0 && !VocabularyContentPolicy.Meaningful(blocks)) throw new AnkiReviewException("Add a review answer before sending.");
        if ((!plan.Recognition && !plan.Gap) || plan.Gap && gap is null) throw new AnkiReviewException("Choose valid expression positions for a gap card.");
        var records = example?.Explanations ?? sense.Explanations;
        string explanation = plan.IncludeExplanation ? records.LastOrDefault()?.Result.Explanation ?? "" : "";
        return new(entry.Expression, answer, sentence, ranges, example?.Translation ?? "", explanation, plan.IncludeNotes ? sense.Note : "",
            gap?.Prompt ?? "", gap?.Answer ?? "", plan.Recognition, plan.Gap) { SenseContent = blocks };
    }

    /// <summary>Stored occurrences preserve discontinuous/repeated expressions. Validate complete
    /// composed sequences and disjoint spans before replacing backwards so no earlier span shifts.</summary>
    public static (string Prompt, string Answer)? Gap(string text, IReadOnlyList<VocabularyTextRange> ranges, VocabularyEntryKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new VocabularyContentValidationException();
        var ordered = ranges.OrderBy(range => range.Location).ToArray(); var boundaries = FoundationTextRanges.Boundaries(text);
        if (kind == VocabularyEntryKind.Sentence || ordered.Length == 0
            || ordered.Any(range => !FoundationTextRanges.IsValid(range.Location, range.Length, text, boundaries))
            || ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.Location + pair.First.Length > pair.Second.Location)
            || ordered.Sum(range => (long)range.Length) >= text.Length) return null;
        string answer = string.Join(" … ", ordered.Select(range => text.Substring(range.Location, range.Length)));
        var prompt = new StringBuilder(text);
        foreach (var range in ordered.Reverse()) prompt.Remove(range.Location, range.Length).Insert(range.Location, "___");
        return (prompt.ToString(), answer);
    }
    public static string Html(string text) => ReviewHtmlText.Escape(text);
    public static string SentenceHtml(ReviewCardContent content)
    {
        var ranges = content.Ranges.OrderBy(range => range.Location).ToArray(); var boundaries = FoundationTextRanges.Boundaries(content.Sentence);
        if (ranges.Any(range => !FoundationTextRanges.IsValid(range.Location, range.Length, content.Sentence, boundaries))
            || ranges.Zip(ranges.Skip(1)).Any(pair => pair.First.Location + pair.First.Length > pair.Second.Location)) return Html(content.Sentence);
        var output = new StringBuilder(); int cursor = 0;
        foreach (var range in ranges)
        {
            output.Append(Html(content.Sentence.Substring(cursor, range.Location - cursor)))
                .Append("<b>").Append(Html(content.Sentence.Substring(range.Location, range.Length))).Append("</b>");
            cursor = range.Location + range.Length;
        }
        return output.Append(Html(content.Sentence[cursor..])).ToString();
    }
    public static IReadOnlyDictionary<ReviewField, string> Values(ReviewCardContent content, Guid identity) => CollectionValues.Map(new Dictionary<ReviewField, string>
    {
        [ReviewField.Identity] = identity.ToString("D"), [ReviewField.Expression] = Html(content.Expression),
        [ReviewField.Meaning] = Html(content.Answer) + VocabularyContentPolicy.RenderOccurrencesHtml(content.SenseContent),
        [ReviewField.Sentence] = SentenceHtml(content), [ReviewField.Translation] = Html(content.Translation),
        [ReviewField.Explanation] = Html(content.Explanation), [ReviewField.Notes] = Html(content.Notes),
        [ReviewField.Recognition] = content.Recognition ? "1" : "", [ReviewField.GapPrompt] = content.Gap ? Html(content.GapPrompt) : "",
        [ReviewField.GapAnswer] = content.Gap ? Html(content.GapAnswer) : "",
    });
}
