using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;

namespace Cotabby.Core.Tests;

/// <summary>The complete original resolver receives the same scripted unrestricted scores.
/// Results and exact recognizer samples prove order, bounds, confidence and unsupported outcomes;
/// these cases do not substitute a fake detector for a shipped Windows recognizer.</summary>
public sealed class SpeechLanguageResolverTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-language-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(191, rows.Length);
        Assert.Equal("f36aacd9320d73e760f2f96fd91a4d97d6dfd0b68fb3bc7d7edc96a55ccb4c93", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public void OriginalTextContextAndHintResolution(string id, JsonElement row)
    {
        var responses = row.GetProperty("scores").EnumerateArray().ToArray(); var samples = new List<string>();
        var result = SpeechLanguageResolver.Resolve(row.GetProperty("text").GetString()!, JsonSerializer.Deserialize<LearningLanguage>(row.GetProperty("hint").GetRawText()), sample =>
        {
            int index = samples.Count; samples.Add(sample);
            return index < responses.Length ? responses[index].EnumerateObject().ToDictionary(p => p.Name, p => Score(p.Value.GetString()!)) : new Dictionary<string, double>();
        }, row.GetProperty("context").GetString());
        var actual = JsonSerializer.SerializeToNode(new { language = result?.Code(), samples });
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText());
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\nExpected: " + expected + "\nActual: " + actual);
    }
    [Fact]
    public void RecognitionFailurePropagatesWithoutCallingContextOrRelabellingTheHint()
    {
        var failure = new InvalidOperationException("synthetic recognizer fault"); int calls = 0;
        var caught = Assert.Throws<InvalidOperationException>(() => SpeechLanguageResolver.Resolve("Please read these words.", LearningLanguage.English, _ => { calls++; throw failure; }, "Several context words here."));
        Assert.Same(failure, caught); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("FoundationLetterRanges", "foundationLetterRanges", 750)]
    [InlineData("WordLetterRanges", "wordLetterRanges", 766)]
    public void FrozenUnicodePredicatesRetainEveryObservedSourceRange(string field, string key, int count)
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-language-v1.json")));
        var expected = d.RootElement.GetProperty(key).EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var type = typeof(SpeechLanguageResolver).Assembly.GetType("Cotabby.Core.Speech.SpeechLanguageEvidence", throwOnError: true)!;
        var actual = (int[])type.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal(count * 2, actual.Length); Assert.Equal(expected, actual);
        for (int i = 0; i < actual.Length; i += 2)
        {
            Assert.InRange(actual[i], 0, 0x10FFFF); Assert.InRange(actual[i + 1], actual[i], 0x10FFFF);
            if (i > 0) Assert.True(actual[i] > actual[i - 1] + 1);
        }
        var method = type.GetMethod(field == "FoundationLetterRanges" ? "IsFoundationLetter" : "IsWordLetter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var classify = method.CreateDelegate<Func<int, bool>>(); int range = 0;
        // Walk captured source intervals linearly, independently of the production binary search.
        // This checks every member and every gap, including the surrogate range and final scalar.
        for (int scalar = 0; scalar <= 0x10FFFF; scalar++)
        {
            while (range < expected.Length && expected[range + 1] < scalar) range += 2;
            bool included = range < expected.Length && scalar >= expected[range];
            if (classify(scalar) != included) Assert.Fail($"Source Unicode predicate changed at U+{scalar:X}.");
        }
    }
    private static double Score(string score) => score switch { "nan" => double.NaN, "inf" => double.PositiveInfinity, "-inf" => double.NegativeInfinity, _ => double.Parse(score, CultureInfo.InvariantCulture) };
}
