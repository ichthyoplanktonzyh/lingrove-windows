using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>
/// Expected values come only from actual pinned Mac policy execution. Prompt prose and UTF-8/token
/// budgets are exact; JSON payloads compare semantically because Swift does not promise key order.
/// </summary>
public sealed class LearningExplanationParityTests
{
    public static IEnumerable<object[]> CaseIds() => Cases().Select(row => new object[] { row.GetProperty("id").GetString()! });
    private static JsonElement[] Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "parity", "learning-explanation-v1.json")));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", document.RootElement.GetProperty("macReference").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(row => row.Clone()).ToArray();
    }
    [Theory]
    [MemberData(nameof(CaseIds))]
    public void ActualMacExplanationRulesRemainTheAuthority(string id)
    {
        var row = Cases().Single(row => row.GetProperty("id").GetString() == id);
        var expected = row.GetProperty("expected");
        string mode = row.GetProperty("mode").GetString()!;
        object? value = null;
        LearningExplanationException? failure = null;
        try
        {
            value = mode switch
            {
                "input" => LearningExplanationPolicy.IsValid(Input(row.GetProperty("input"))),
                "result" => LearningExplanationPolicy.IsValid(new LearningExplanationResult(Text(row.GetProperty("meaning")), Text(row.GetProperty("explanation")))),
                "parse" => LearningExplanationPolicy.Parse(Text(row.GetProperty("raw"))),
                "prompt" => LearningExplanationPolicy.Prompt(Input(row.GetProperty("input")), Language(row.GetProperty("responseLanguage")),
                    row.GetProperty("total").GetInt32(), row.GetProperty("output").GetInt32()),
                _ => throw new InvalidOperationException("Unknown fixture mode"),
            };
        }
        catch (LearningExplanationException error) { failure = error; }
        if (expected.TryGetProperty("error", out var category))
        {
            Assert.NotNull(failure);
            Assert.Equal(category.GetString(), failure.Failure == LearningExplanationFailure.InvalidContent ? "invalidContent" : "message");
            Assert.Equal(expected.GetProperty("message").GetString(), failure.Message);
            return;
        }
        Assert.Null(failure);
        if (mode is "input" or "result") Assert.Equal(expected.GetProperty("valid").GetBoolean(), Assert.IsType<bool>(value));
        else if (mode == "parse")
        {
            var result = Assert.IsType<LearningExplanationResult>(value);
            Assert.Equal(expected.GetProperty("meaning").GetString(), result.Meaning);
            Assert.Equal(expected.GetProperty("explanation").GetString(), result.Explanation);
        }
        else
        {
            string prompt = Assert.IsType<string>(value);
            const string marker = "Study material:\n";
            int start = prompt.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length;
            int end = prompt.LastIndexOf('\n');
            Assert.Equal(expected.GetProperty("prefix").GetString(), prompt[..start]);
            Assert.Equal(expected.GetProperty("suffix").GetString(), prompt[end..]);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("payload").GetRawText()), JsonNode.Parse(prompt[start..end])), id);
            Assert.Equal(expected.GetProperty("bytes").GetInt32(), Encoding.UTF8.GetByteCount(prompt));
            Assert.Equal(expected.GetProperty("tokens").GetInt32(), ModelContextBudget.EstimatedTokens(prompt));
            Assert.Equal(expected.GetProperty("allowance").GetInt32(), ModelContextBudget.InputAllowance(
                row.GetProperty("total").GetInt32(), row.GetProperty("output").GetInt32(), estimated: true));
        }
    }
    private static LearningExplanationInput Input(JsonElement input) => new(Text(input.GetProperty("target")), Text(input.GetProperty("dictionaryForm")),
        Language(input.GetProperty("language")), Enum.Parse<VocabularyEntryKind>(input.GetProperty("kind").GetString()!, ignoreCase: true),
        Text(input.GetProperty("context")), input.GetProperty("ranges").EnumerateArray().Select(range =>
            new VocabularyTextRange(range.GetProperty("location").GetInt32(), range.GetProperty("length").GetInt32())).ToArray(),
        input.TryGetProperty("contextKind", out var context) ? Enum.Parse<LearningExplanationContext>(context.GetString()!, true) : LearningExplanationContext.Fragment,
        input.TryGetProperty("occurrenceIdentity", out var identity) ? identity.GetString() : null);
    private static LearningLanguage Language(JsonElement value) => LearningLanguages.FromCode(value.GetString()!)!.Value;
    private static string Text(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
        if (value.TryGetProperty("concat", out var parts)) return string.Concat(parts.EnumerateArray().Select(Text));
        var repeated = value.GetProperty("repeat");
        return string.Concat(Enumerable.Repeat(Text(repeated.GetProperty("text")), repeated.GetProperty("count").GetInt32()));
    }
}
