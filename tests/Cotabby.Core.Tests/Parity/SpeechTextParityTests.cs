using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;

namespace Cotabby.Core.Tests.Parity;

public sealed class SpeechTextParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-text-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(r => new object[] { Text(r, "id") });
    [Theory, MemberData(nameof(Cases))]
    public void OriginalSpeechTextAndReceiptFreshness(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(47, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        Assert.Equal("539253fa260ba1d8896a73a8020f548eb95a402145dd0fca4f61cec9c3002103", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Corpus.GetProperty("cases").EnumerateArray().Select(r => Text(r, "id")).Order(StringComparer.Ordinal))))));
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => Text(r, "id") == id);
        object? actual;
        if (Text(row, "kind", "sanitize") == "current") actual = AnkiReviewPolicy.CurrentAudio(Plan(row), Content(row));
        else if (Text(row, "kind", "sanitize") == "unit")
        {
            var words = row.TryGetProperty("words", out var items) ? items.EnumerateArray().Select(v => new LearningDictionaryWord(v.GetString()!, new(0, 0))).ToArray() : [];
            actual = SpeechTextPolicy.SpokenText(new(0, Input(row), Text(row, "query", "lemma"), Enum.Parse<LearningUnitKind>(Text(row, "unitKind", "word"), true), [], words));
        }
        else actual = SpeechTextPolicy.SanitizedText(Input(row));
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var observed = JsonSerializer.SerializeToNode(actual);
        Assert.True(JsonNode.DeepEquals(expected, observed), id + "\nExpected: " + expected + "\nActual: " + observed);
    }
    private static string Text(JsonElement row, string key, string fallback = "") => row.TryGetProperty(key, out var value) ? value.GetString()! : fallback;
    private static string Input(JsonElement row) => row.TryGetProperty("repeatCount", out var count) ? string.Concat(Enumerable.Repeat(Text(row, "repeatText", "a"), count.GetInt32())) : Text(row, "text");
    private static ReviewPlan Plan(JsonElement row) => new(Guid.Empty)
    {
        AudioKinds = row.TryGetProperty("audioKinds", out var kinds) ? kinds.EnumerateArray().Select(v => Enum.Parse<ReviewAudioKind>(v.GetString()!, true)).ToHashSet() : null,
        AudioReceipts = row.TryGetProperty("receipts", out var receipts) ? receipts.EnumerateObject().ToDictionary(p => Enum.Parse<ReviewAudioKind>(p.Name, true),
            p => new ReviewAudioReceipt("ignored fingerprint", p.Value.GetString()!, "http://localhost", "profile", "audio.wav")) : null,
    };
    private static ReviewCardContent Content(JsonElement row) => new(Input(row), "answer", Text(row, "sentence"), [], "", "", "", "", "", true, false);
    [Fact]
    public void MalformedNetStringsAndUndefinedKindsCannotBecomeSpeechOrCurrentAudio()
    {
        Assert.Null(SpeechTextPolicy.SanitizedText(null)); Assert.Null(SpeechTextPolicy.SanitizedText("\ud800"));
        Assert.Null(SpeechTextPolicy.SpokenText(new(0, "text", "query", (LearningUnitKind)999, [], [])));
        Assert.Null(SpeechTextPolicy.SpokenText(new(0, "text", "query", LearningUnitKind.Pattern, [], [new(null!, new(0, 0))])));
        using var empty = JsonDocument.Parse("{}");
        Assert.False(AnkiReviewPolicy.CurrentAudio(new(Guid.Empty) { AudioKinds = new HashSet<ReviewAudioKind> { (ReviewAudioKind)999 } }, Content(empty.RootElement)));
    }
    [Fact]
    public void FreshnessUsesSanitizedTextNotAnswerProviderOrFileIdentity()
    {
        var content = new ReviewCardContent(" hello\t world ", "answer", "sentence", [], "", "", "", "", "", true, false);
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hello world")));
        var plan = new ReviewPlan(Guid.Empty) { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression },
            AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> { [ReviewAudioKind.Expression] = new("other fingerprint", hash, "http://127.0.0.1:8765", "other profile", "missing.wav") } };
        Assert.True(AnkiReviewPolicy.CurrentAudio(plan, content));
        var changedAnswer = new ReviewCardContent(content.Expression, "changed", content.Sentence, [], "", "", "", "", "", true, false);
        Assert.True(AnkiReviewPolicy.CurrentAudio(plan, changedAnswer));
        var changedText = new ReviewCardContent("different", content.Answer, content.Sentence, [], "", "", "", "", "", true, false);
        Assert.False(AnkiReviewPolicy.CurrentAudio(plan, changedText));
    }
}
