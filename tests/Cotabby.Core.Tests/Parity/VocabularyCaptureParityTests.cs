using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

public sealed class VocabularyCaptureParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-capture-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray()
        .Select(row => new object[] { row.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacCaptureOwnsNormalizedEntryAndExactUsageKeysAndAdmission(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var capture = Capture(row.GetProperty("capture")); var expected = row.GetProperty("expected");
        string normalized = VocabularyCapturePolicy.Normalized(capture.Expression);
        byte[] bytes = Encoding.UTF8.GetBytes(normalized);
        Assert.Equal(expected.GetProperty("normalizedBytes").GetInt32(), bytes.Length);
        Assert.Equal(expected.GetProperty("normalizedSHA256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        if (expected.GetProperty("normalized").ValueKind == JsonValueKind.String)
            Assert.Equal(expected.GetProperty("normalized").GetString(), normalized);
        Assert.Equal(expected.GetProperty("entryKey").GetString(), VocabularyCapturePolicy.EntryKey(capture));
        Assert.Equal(expected.GetProperty("exampleKey").GetString(), VocabularyCapturePolicy.ExampleKey(capture.Example));
        Assert.Equal(expected.GetProperty("captureKey").GetString(), VocabularyCapturePolicy.CaptureKey(capture));
        var input = LearningExplanationPolicy.Input(capture);
        Assert.Equal(expected.GetProperty("explanationValid").GetBoolean(), LearningExplanationPolicy.IsValid(input));
        Assert.Equal(expected.GetProperty("explanationReceipt").ValueKind == JsonValueKind.Null ? null
            : expected.GetProperty("explanationReceipt").GetString(), input.OccurrenceIdentity);
        var projection = expected.GetProperty("input");
        AssertLiteral(projection.GetProperty("target"), input.Target);
        AssertLiteral(projection.GetProperty("dictionaryForm"), input.DictionaryForm);
        AssertLiteral(projection.GetProperty("context"), input.Context);
        Assert.Equal(projection.GetProperty("language").GetString(), input.Language.Code());
        Assert.Equal(JsonSerializer.Deserialize<VocabularyEntryKind>(projection.GetProperty("kind").GetRawText()), input.Kind);
        Assert.Equal(JsonSerializer.Deserialize<LearningExplanationContext>(projection.GetProperty("contextKind").GetRawText()), input.ContextKind);
        Assert.Equal(projection.GetProperty("ranges").EnumerateArray().Select(range => new VocabularyTextRange(
            range.GetProperty("location").GetInt32(), range.GetProperty("length").GetInt32())), input.Ranges);
        if (expected.GetProperty("valid").GetBoolean()) VocabularyCapturePolicy.Validate(capture);
        else Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Validate(capture));
    }

    private static VocabularyCapture Capture(JsonElement value) => new(Text(value.GetProperty("expression")),
        value.GetProperty("dictionaryForm").GetString()!, LearningLanguages.FromCode(value.GetProperty("language").GetString()!)!.Value,
        Enum.Parse<VocabularyEntryKind>(value.GetProperty("kind").GetString()!, true), String(value, "meaning") ?? "", Language(value, "meaningLanguage"),
        value.TryGetProperty("example", out var usage) ? Example(usage) : null,
        SenseId: value.TryGetProperty("senseID", out var sense) ? sense.GetGuid() : null);

    private static VocabularyExample Example(JsonElement value) => new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Text(value.GetProperty("text")), String(value, "translation") ?? "",
        value.GetProperty("ranges").EnumerateArray().Select(range => new VocabularyTextRange(
            range.GetProperty("location").GetInt32(), range.GetProperty("length").GetInt32())).ToArray(),
        Enum.Parse<VocabularyExampleCompleteness>(value.GetProperty("completeness").GetString()!, true),
        String(value, "meaning") ?? "", DateTimeOffset.UnixEpoch, Language(value, "translationLanguage"),
        meaningLanguage: Language(value, "meaningLanguage"),
        meaningOrigin: value.TryGetProperty("meaningOrigin", out var origin) ? Enum.Parse<VocabularyMeaningOrigin>(origin.GetString()!, true) : VocabularyMeaningOrigin.Captured,
        sourceApplicationId: String(value, "app"));

    private static LearningLanguage? Language(JsonElement value, string name) => String(value, name) is { } code ? LearningLanguages.FromCode(code) : null;
    private static string? String(JsonElement value, string name) => value.TryGetProperty(name, out var text) ? text.GetString() : null;
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()!
        : string.Concat(Enumerable.Repeat(value.GetProperty("repeating").GetString()!, value.GetProperty("count").GetInt32()));
    private static void AssertLiteral(JsonElement expected, string actual)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(actual);
        Assert.Equal(expected.GetProperty("utf8Bytes").GetInt32(), bytes.Length);
        Assert.Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        if (expected.TryGetProperty("text", out var text)) Assert.Equal(text.GetString(), actual);
    }
}
