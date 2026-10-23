using System.Globalization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Tests.Shared;

/// <summary>Fixed input graph shared by the original Mac projection and notebook-session probes.
/// Only input construction is shared; each corpus retains independently captured Swift outputs.</summary>
internal static class VocabularyReferenceGraph
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    internal static VocabularyDocument Document()
    {
        var usage = new VocabularyExample(Id(3), "The bank.", "原句", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "context", Now);
        var sense = new VocabularySense(Id(2), "core", Now, Now) { Note = "note", Tags = ["Work"], IsSavedWithoutContext = true };
        usage = usage with { Explanations = [Record(100, new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, Example: usage))] };
        sense = sense with { Examples = [usage], Explanations = [Record(100, new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word))], Content = [Block(20)] };
        var layout = new ReviewLayout([new(Id(50), ReviewContentSource.Custom, content: [Block(60)])]);
        var binding = new AnkiNoteBinding(Id(40), Id(99), "http://localhost", "profile", 1, "model", "deck", "ID",
            new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = Id(99).ToString("D") });
        var review = new VocabularyReview(new(Id(30)) { Layout = layout }, [binding], binding.Id, [new(Id(31))]);
        sense = sense with { Review = review, ReviewCards = [review] };
        return new() { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense], Now, Now)], ReviewTemplates = [new(Id(70), "Template", layout)] };
    }
    internal static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12", CultureInfo.InvariantCulture));
    private static VocabularyContentBlock Block(int n) => new(Id(n), new VocabularyBlockContent.Text(new("literal")));
    private static LearningExplanationRecord Record(int n, VocabularyCapture capture) => new(Id(n), LearningExplanationPolicy.Input(capture), LearningLanguage.Chinese, new("meaning", "explanation"), "fixture", Now);
}
