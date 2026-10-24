using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Tests.Parity;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

/// <summary>
/// Current-source outputs, captured before C# implementation. The old v43 reset observations stay
/// archived separately; these tests exercise the new range-only callbacks and their text boundary.
/// Neither fixture runner derives its expected values from the Windows implementation.
/// </summary>
public sealed class CurrentVocabularyEditorParityTests
{
    private const string Current = "cc468b89f5c360cc7210fac4f1a8b52197870bf2";
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-editor-current-v2.json"))).RootElement.Clone();
    private static Guid Id(int value) => Guid.Parse("00000000-0000-0000-0000-" + value.ToString("x12"));
    private static string? Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.GetString() : null;
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(2, Corpus.GetProperty("version").GetInt32()); Assert.Equal(Current, Corpus.GetProperty("macReference").GetString());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("previousMacReference").GetString());
        Assert.Equal("en_US", Corpus.GetProperty("referenceLocale").GetString());
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(182, rows.Length);
        Assert.Equal(182, rows.Select(row => Text(row, "id")).Distinct().Count());
        Assert.Equal("cbfc4567ef7f19dfca03b72011488a4314d81c5f026afae7c4fa383a0290e608", Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(row => Text(row, "id")!).Order(StringComparer.Ordinal))))));
        Assert.Equal(155, rows.Count(row => Text(row, "operation") == "ranges")); Assert.Equal(14, rows.Count(row => Text(row, "operation") == "valid"));
        Assert.Equal(8, rows.Count(row => Text(row, "operation") == "change")); Assert.Equal(5, rows.Count(row => Text(row, "operation") == "expression"));
        Assert.Single(rows, row => Text(row, "provenanceKind") == "original-test-adaptation");
        Assert.Equal(181, rows.Count(row => Text(row, "provenanceKind") == "source-policy-extension"));
        foreach (var row in rows)
        {
            string operation = Text(row, "operation")!;
            Assert.Equal(operation is "ranges" or "valid" ? "Cotabby/Support/Vocabulary/VocabularyPolicy.swift" : "Cotabby/UI/Vocabulary/VocabularyEntryEditor.swift", Text(row, "macSource"));
            if (Text(row, "provenanceKind") == "original-test-adaptation")
                Assert.Equal("CotabbyTests/Support/Vocabulary/VocabularyPolicyTests.swift:reconciledRangesReanchorExpressionAfterExampleTextEdit", Text(row, "sourceTest"));
            else Assert.Null(Text(row, "sourceTest"));
            yield return [Text(row, "id")!];
        }
    }
    [Theory, MemberData(nameof(Cases))]
    public void CurrentMacSourceSuppliesTheEditorOutcome(string id)
    {
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => Text(row, "id") == id);
        var actual = Observe(row); Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static JsonNode? Payload(VocabularyExample example) => JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Usage(example)));
    private static JsonNode? Observe(JsonElement row)
    {
        var entry = VocabularyProjectionParityTests.Document().Entries[0]; string operation = Text(row, "operation")!;
        if (operation == "ranges")
        {
            string text = Text(row, "text")!; var ranges = VocabularyPolicy.ReconciledRanges(text, Text(row, "expression")!, Text(row, "form")!);
            return JsonSerializer.SerializeToNode(new { ranges = ranges.Select(range => new { location = range.Location, length = range.Length, text = text.Substring(range.Location, range.Length) }) });
        }
        if (operation == "valid")
        {
            var example = Example(Text(row, "text")!, [new(row.GetProperty("location").GetInt32(), row.GetProperty("length").GetInt32())]);
            bool valid; try { VocabularyCapturePolicy.Validate(Capture(example)); valid = true; }
            catch (VocabularyContentValidationException) { valid = false; }
            return JsonSerializer.SerializeToNode(new { valid });
        }
        entry = entry with { Expression = Text(row, "expression") ?? entry.Expression, DictionaryForm = Text(row, "form") ?? entry.DictionaryForm };
        if (operation == "change")
        {
            var example = entry.Senses[0].Examples[0] with { Text = Text(row, "before") ?? entry.Senses[0].Examples[0].Text,
                TranslationLanguage = LearningLanguage.Chinese, TranslationVariants = [new("variant", LearningLanguage.French)],
                MeaningLanguage = LearningLanguage.English, MeaningOrigin = VocabularyMeaningOrigin.User, SourceApplicationId = "source.application" };
            return new JsonObject { ["example"] = Payload(VocabularyEntryEditingPolicy.ChangeOriginalText(example, Text(row, "text")!, entry.Expression, entry.DictionaryForm)) };
        }
        entry = entry with { Expression = "bank", Senses = [entry.Senses[0] with { Examples = entry.Senses[0].Examples.Append(
            new VocabularyExample(Id(4), "river bank", "second", [new(6, 4)], VocabularyExampleCompleteness.Fragment, "second meaning", DateTimeOffset.UnixEpoch)).ToArray() },
            new VocabularySense(Id(5), "other", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { Examples = [
                new(Id(6), "river bank", "other", [new(0, 5)], VocabularyExampleCompleteness.Sentence, "other meaning", DateTimeOffset.UnixEpoch)] }] };
        var updated = VocabularyEntryEditingPolicy.ChangeExpression(entry, Id(2), Text(row, "expression")!);
        return new JsonObject { ["expression"] = updated.Expression, ["form"] = updated.DictionaryForm,
            ["senses"] = new JsonArray(updated.Senses.Select(sense => (JsonNode)new JsonObject { ["id"] = sense.Id.ToString("D"),
                ["examples"] = new JsonArray(sense.Examples.Select(Payload).ToArray()) }).ToArray()) };
    }
    private static VocabularyExample Example(string text, IReadOnlyList<VocabularyTextRange> ranges) => new(Id(3), text, "", ranges,
        VocabularyExampleCompleteness.Fragment, "", DateTimeOffset.UnixEpoch);
    private static VocabularyCapture Capture(VocabularyExample example) => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, Example: example);

    [Fact]
    public void ReanchoredSourceSpansAreAdmittedByCaptureAndTheWholeArchive()
    {
        foreach (var row in Corpus.GetProperty("cases").EnumerateArray().Where(row => Text(row, "operation") == "ranges"))
        {
            string text = Text(row, "text")!; var spans = VocabularyPolicy.ReconciledRanges(text, Text(row, "expression")!, Text(row, "form")!);
            if (spans.Count == 0) continue;
            var example = Example(text, spans); VocabularyCapturePolicy.Validate(Capture(example));
            var now = DateTimeOffset.UnixEpoch;
            VocabularyArchiveValidation.Validate(new() { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
                [new VocabularySense(Id(2), "", now, now) { Examples = [example] }], now, now)] });
        }
    }
    [Fact]
    public void MalformedDotNetTextFailsClosedWithoutErasingAuthoredAnnotations()
    {
        var before = VocabularyProjectionParityTests.Document().Entries[0].Examples[0];
        var updated = VocabularyEntryEditingPolicy.ChangeOriginalText(before, "bad\ud800", "bank");
        Assert.Empty(updated.Ranges); Assert.Equal(before.Translation, updated.Translation); Assert.Equal(before.Meaning, updated.Meaning);
        Assert.Empty(VocabularyPolicy.ReconciledRanges("bank", "bad\ud800"));
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Validate(Capture(updated)));
    }
    [Fact]
    public void ExhaustiveScalarObservationRetainsItsCaptureAndGeneratorIdentity()
    {
        using var source = typeof(VocabularyPolicy).Assembly.GetManifestResourceStream("Cotabby.Core.Resources.Foundation.editor-text-data-v1.json")!;
        using var bytes = new MemoryStream(); source.CopyTo(bytes);
        Assert.Equal("f74ae78a4ec4439e54c7e10776299e7a53aa98820548c8f15db788a7687a07a6", Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var document = JsonDocument.Parse(bytes.ToArray()); var data = document.RootElement;
        Assert.Equal(Current, Text(data, "macReference")); Assert.Equal("en_US", Text(data, "captureLocale"));
        Assert.Equal("Version 26.6.2 (Build 25G83)", Text(data, "operatingSystem")); Assert.Equal(5026.6, data.GetProperty("coreFoundationVersion").GetDouble());
        Assert.Equal(1_112_064, data.GetProperty("scalarCount").GetInt32()); Assert.Equal(1944, data.GetProperty("folds").GetArrayLength());
        Assert.Equal(280, data.GetProperty("forwardCompatibility").GetArrayLength()); Assert.Equal("17.0.0", Text(data, "unicodeVersion"));
    }
}
