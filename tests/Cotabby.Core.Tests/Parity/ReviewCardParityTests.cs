using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Inputs construct ordinary saved values; every expected result was captured by
/// executing complete original Mac policies, not a C# replacement oracle. No UI, media bytes,
/// speech runtime, Store mutation or Anki server participates in this pure preparation suite.</summary>
public sealed class ReviewCardParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "review-card-v1.json"))).RootElement.Clone();
    private static readonly JsonSerializerOptions WireOptions = new() { MaxDepth = 512 };
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalReviewPreparationRetainsSourceResolutionEscapingTemplatesAndJsonTypes(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(175, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => Text(value, "id", "") == id);
        Assert.Contains(Text(row, "macSource", ""), new[] { "Cotabby/Support/Review/ReviewCardPolicy.swift", "Cotabby/Support/Review/ReviewLayoutPolicy.swift", "Cotabby/Support/Review/AnkiNoteTemplate.swift", "Cotabby/Models/Review/AnkiWireModels.swift" });
        Assert.Contains(Text(row, "provenanceKind", ""), new[] { "source-policy-extension", "regression-adaptation" });
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{id}\nExpected: {expected}\nActual: {actual}");
    }
    private static string Text(JsonElement row, string key, string fallback) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;
    private static bool Flag(JsonElement row, string key, bool fallback) => row.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static bool? OptionalFlag(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static VocabularyTextRange[] Ranges(JsonElement row) => row.TryGetProperty("ranges", out var ranges)
        ? ranges.EnumerateArray().Select(value => new VocabularyTextRange(value[0].GetInt32(), value[1].GetInt32())).ToArray() : [new(4, 4)];
    private static VocabularyContentBlock[] Blocks(string mode, int start = 201)
    {
        var plain = new VocabularyContentBlock(Id(start), new VocabularyBlockContent.Text(new("Authored <hint>&", VocabularyTextStyle.Heading, true, true)));
        var image = new VocabularyContentBlock(Id(start + 1), new VocabularyBlockContent.Image(new(new string('a', 64), 20, 30, 50), "Answer <bank>&"));
        return mode switch
        {
            "none" => [], "text" => [plain], "image" => [image], "whitespace" => [new(Id(start), new VocabularyBlockContent.Text(new(" \u200b\n")))],
            "duplicate" => [plain, plain], "invalid" => [new(Id(start), new VocabularyBlockContent.Image(new("../unsafe", 20, 30, 50)))], _ => [plain, image],
        };
    }
    private static LearningExplanationRecord Record(int n, string prose) => new(Id(n), new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
        "The bank.", [new(4, 4)]), LearningLanguage.Chinese, new("meaning", prose), "fixture", Now);
    private static LearningExplanationRecord[] Records(JsonElement row, string key, string fallback, int n) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Null
        ? [] : [Record(n, "previous"), Record(n + 1, Text(row, key, fallback))];
    private static ReviewCardPart Part(JsonElement row, int n) => new(Id(row.TryGetProperty("id", out var value) ? value.GetInt32() : n),
        Enum.Parse<ReviewContentSource>(row.GetProperty("source").GetString()!, true), row.TryGetProperty("block", out var block) ? Id(block.GetInt32()) : null,
        Flag(row, "caption", false), row.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? Blocks(content.GetString()!, 1000 + n * 2) : []);
    private static ReviewLayout Layout(JsonElement row) => new(
        row.TryGetProperty("front", out var front) ? front.EnumerateArray().Select((part, index) => Part(part, 100 + index)).ToArray() : [new(Id(100), ReviewContentSource.Expression)],
        row.TryGetProperty("back", out var back) ? back.EnumerateArray().Select((part, index) => Part(part, 200 + index)).ToArray() : [new(Id(200), ReviewContentSource.Meaning)]);
    private static (VocabularyEntry Entry, VocabularySense Sense, ReviewPlan Plan) Seed(JsonElement row)
    {
        var usage = new VocabularyExample(Id(10), Text(row, "sentence", "The bank."), Text(row, "translation", "translation <&>"), Ranges(row),
            VocabularyExampleCompleteness.Sentence, Text(row, "usageMeaning", " contextual answer "), Now,
            explanations: Records(row, "usageExplanation", "USAGE last", 60));
        var sense = new VocabularySense(Id(2), Text(row, "senseMeaning", " core & <meaning> "), Now, Now)
        {
            MeaningOrigin = Enum.Parse<VocabularyMeaningOrigin>(Text(row, "origin", "captured"), true), Note = Text(row, "note", "note\n<&>"),
            Examples = Flag(row, "usagePresent", true) ? [usage] : [], Explanations = Records(row, "senseExplanation", "SENSE last", 50), Content = Blocks(Text(row, "senseBlocks", "both")),
        };
        if (Flag(row, "existingReview", false)) sense = sense with { Review = new(new(Id(77)) { Answer = "saved", Recognition = false, Gap = true }) };
        var entry = new VocabularyEntry(Id(1), Text(row, "expression", "bank <&>"), "bank", LearningLanguage.English,
            Enum.Parse<VocabularyEntryKind>(Text(row, "kind", "word"), true), [sense], Now, Now);
        var plan = new ReviewPlan(Id(80))
        {
            ExampleId = Text(row, "example", "first") == "none" ? null : Id(Text(row, "example", "first") == "stale" ? 11 : 10),
            Answer = row.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String ? answer.GetString() : null,
            Recognition = Flag(row, "recognition", true), Gap = Flag(row, "gap", false), IncludeNotes = Flag(row, "includeNotes", false),
            IncludeExplanation = Flag(row, "includeExplanation", false), IncludeSenseContent = OptionalFlag(row, "includeContent"),
            Layout = Flag(row, "flexible", false) ? Layout(row) : null,
        };
        return (entry, sense, plan);
    }
    private static object BlockValue(VocabularyContentBlock block) => block.Content switch
    {
        VocabularyBlockContent.Text text => new { id = block.Id.ToString("D"), kind = "text", text = text.Value.Text, style = text.Value.Style.ToString().ToLowerInvariant(), bold = text.Value.Bold, italic = text.Value.Italic },
        VocabularyBlockContent.Image image => new { id = block.Id.ToString("D"), kind = "image", filename = image.Reference.Filename, caption = image.Caption },
        _ => throw new InvalidOperationException(),
    };
    private static object[]? Parts(IReadOnlyList<ReviewResolvedPart>? values) => values?.Select(part => part switch
    {
        ReviewResolvedPart.Text text => (object)new { kind = "text", value = (object)text.Value },
        ReviewResolvedPart.Blocks blocks => new { kind = "blocks", value = (object)blocks.Values.Select(BlockValue).ToArray() },
        ReviewResolvedPart.Audio audio => new { kind = "audio", value = (object)audio.Kind.ToString().ToLowerInvariant() },
        _ => throw new InvalidOperationException(),
    }).ToArray();
    private static object BlockPayload(VocabularyContentBlock block) => block.Content switch
    {
        VocabularyBlockContent.Text text => new { kind = "text", text = text.Value.Text, style = text.Value.Style.ToString().ToLowerInvariant(), bold = text.Value.Bold, italic = text.Value.Italic },
        VocabularyBlockContent.Image image => new { kind = "image", filename = image.Reference.Filename, caption = image.Caption, digest = image.Reference.Digest, width = image.Reference.Width, height = image.Reference.Height, byteCount = image.Reference.ByteCount },
        _ => throw new InvalidOperationException(),
    };
    private static object ContentValue(ReviewCardContent c) => new { expression = c.Expression, answer = c.Answer, sentence = c.Sentence,
        ranges = c.Ranges.Select(range => new[] { range.Location, range.Length }), translation = c.Translation, explanation = c.Explanation, notes = c.Notes,
        gapPrompt = c.GapPrompt, gapAnswer = c.GapAnswer, recognition = c.Recognition, gap = c.Gap, blocks = c.SenseContent.Select(BlockValue), front = Parts(c.Front), back = Parts(c.Back) };
    private static string Field(ReviewField field) => field == ReviewField.Identity ? "LingroveID" : field.ToString();
    private static object Observe(JsonElement row)
    {
        try { return ObserveCore(row); }
        catch (VocabularyException error) { return new { valid = false, error = char.ToLowerInvariant(error.Failure.ToString()[0]) + error.Failure.ToString()[1..] }; }
        catch (AnkiReviewException error) { return new { valid = false, error = error.Message }; }
    }
    private static object ObserveCore(JsonElement row)
    {
        string operation = row.GetProperty("operation").GetString()!;
        if (operation == "wire") return Wire(row);
        if (operation == "wireEqual") return new { equal = JsonSerializer.Deserialize<AnkiValue>(row.GetProperty("a").GetString()!, WireOptions) == JsonSerializer.Deserialize<AnkiValue>(row.GetProperty("b").GetString()!, WireOptions) };
        if (operation == "gap") { var gap = ReviewCardPolicy.Gap(row.GetProperty("sentence").GetString()!, Ranges(row), Enum.Parse<VocabularyEntryKind>(Text(row, "kind", "word"), true)); return new { prompt = gap?.Prompt, answer = gap?.Answer }; }
        if (operation == "template")
        {
            var fields = row.TryGetProperty("fields", out var data) ? data.EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString()!) : [];
            return new { html = AnkiNoteTemplate.Preview(fields, Flag(row, "gap", false), Flag(row, "back", false), Text(row, "css", AnkiNoteTemplate.Css), Flag(row, "legacy", false)) };
        }
        if (operation == "constants") return new { css = AnkiNoteTemplate.Css, legacyCSS = AnkiNoteTemplate.LegacyCss, templates = AnkiNoteTemplate.Templates, legacyTemplates = AnkiNoteTemplate.LegacyTemplates,
            flexibleCSS = ReviewLayoutPolicy.Css, flexibleTemplates = ReviewLayoutPolicy.Templates, model = ReviewLayoutPolicy.Model, mapping = ReviewLayoutPolicy.Mapping.ToDictionary(pair => Field(pair.Key), pair => pair.Value) };
        var (entry, sense, plan) = Seed(row);
        if (operation == "initial")
        {
            var a = ReviewCardPolicy.InitialPlan(sense); var b = ReviewCardPolicy.InitialPlan(sense);
            return new { saved = a.Id == Id(77), sameAcrossCalls = a.Id == b.Id, example = a.ExampleId?.ToString("D"), includeContent = a.IncludeSenseContent, answer = a.Answer, recognition = a.Recognition, gap = a.Gap };
        }
        if (operation == "preset") { var preset = ReviewLayoutPolicy.Preset(row.GetProperty("index").GetInt32()); return new { front = preset.Front.Select(part => Source(part.Source)), back = preset.Back.Select(part => Source(part.Source)), unique = preset.Parts.Select(part => part.Id).Distinct().Count() == preset.Parts.Count }; }
        if (operation is "portable" or "applying")
        {
            var original = Layout(row); var result = operation == "portable" ? ReviewLayoutPolicy.Portable(original) : ReviewLayoutPolicy.Applying(original, sense);
            return new { front = result.Front.Select(part => Source(part.Source)), back = result.Back.Select(part => Source(part.Source)),
                newParts = original.Parts.Zip(result.Parts).All(pair => pair.First.Id != pair.Second.Id), unique = result.Parts.Select(part => part.Id).Distinct().Count() == result.Parts.Count,
                newBlocks = original.CustomContent.Zip(result.CustomContent).All(pair => pair.First.Id != pair.Second.Id), references = result.Parts.Select(part => part.BlockId?.ToString("D")),
                captions = result.CustomContent.Select(block => block.Content).OfType<VocabularyBlockContent.Image>().Select(image => image.Caption),
                partCount = result.Parts.Count, blockCount = result.CustomContent.Count, payloads = result.CustomContent.Select(BlockPayload) };
        }
        var content = operation == "sentence" ? new ReviewCardContent(entry.Expression, "", sense.Examples.FirstOrDefault()?.Text ?? Text(row, "sentence", ""), Ranges(row), "", "", "", "", "", true, false)
            : operation == "flexible" ? ReviewLayoutPolicy.Content(entry, sense, plan, OptionalFlag(row, "previewBack")) : ReviewCardPolicy.Content(entry, sense, plan);
        if (operation == "sentence") return new { html = ReviewCardPolicy.SentenceHtml(content) };
        var audio = new Dictionary<ReviewField, string> { [ReviewField.ExpressionAudio] = "[sound:expression.wav]", [ReviewField.SentenceAudio] = "[sound:sentence.wav]" };
        return new { valid = true, content = ContentValue(content), sentenceHTML = ReviewCardPolicy.SentenceHtml(content), values = ReviewCardPolicy.Values(content, Id(2)).ToDictionary(pair => Field(pair.Key), pair => pair.Value),
            frontHTML = content.Front is null ? null : ReviewLayoutPolicy.Html(content.Front, audio), backHTML = content.Back is null ? null : ReviewLayoutPolicy.Html(content.Back, audio),
            frontPreview = content.Front is null ? null : ReviewLayoutPolicy.Preview(content.Front), backPreview = content.Back is null ? null : ReviewLayoutPolicy.Preview(content.Back) };
    }
    private static string Source(ReviewContentSource value) { string name = value.ToString(); return char.ToLowerInvariant(name[0]) + name[1..]; }
    private static object Wire(JsonElement row)
    {
        AnkiValue value;
        if (row.TryGetProperty("numeric", out var numeric)) value = new AnkiValue.Number(numeric.GetString() switch { "nan" => double.NaN, "infinity" => double.PositiveInfinity, _ => double.NegativeInfinity });
        else try { value = JsonSerializer.Deserialize<AnkiValue>(row.GetProperty("json").GetString()!, WireOptions)!; } catch (JsonException) { return new { decoded = false }; }
        string kind = value switch { AnkiValue.Map => "object", AnkiValue.Sequence => "array", AnkiValue.Text => "string", AnkiValue.Number => "number", AnkiValue.Boolean => "bool", _ => "null" };
        string? encoded;
        try { encoded = JsonSerializer.Serialize(value, WireOptions); } catch (Exception error) when (error is JsonException or ArgumentException) { encoded = null; }
        JsonElement? projected = null;
        if (encoded is not null && !Flag(row, "omitValue", false)) { using var document = JsonDocument.Parse(encoded, new() { MaxDepth = 512 }); projected = document.RootElement.Clone(); }
        return new { decoded = true, kind, integer = value.Integer?.ToString(CultureInfo.InvariantCulture), encoded = encoded is not null, value = projected, objectCount = value.Object?.Count, arrayCount = value.Array?.Count };
    }

    [Fact]
    public void ResolvedValuesOwnCollectionsAndPreserveAbsentSidesAndCanonicalEquality()
    {
        var ranges = new List<VocabularyTextRange> { new(0, 1) }; var blocks = Blocks("image").ToList(); var parts = new List<ReviewResolvedPart> { new ReviewResolvedPart.Blocks(blocks) };
        var a = new ReviewCardContent("café", "a", "x", ranges, "", "", "", "", "", true, false) { SenseContent = blocks, Front = parts };
        var b = new ReviewCardContent("cafe\u0301", "a", "x", ranges, "", "", "", "", "", true, false) { SenseContent = blocks, Front = parts };
        ranges.Clear(); blocks.Clear(); parts.Clear(); Assert.Single(a.Ranges); Assert.Single(a.SenseContent); Assert.Single(a.Front!); Assert.Null(a.Back);
        Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode()); Assert.NotEqual(a, a with { Back = [] });
        var dictionary = new Dictionary<string, AnkiValue> { ["a"] = new AnkiValue.Text("café") }; var list = new List<AnkiValue> { new AnkiValue.Map(dictionary) };
        var array = new AnkiValue.Sequence(list); dictionary.Clear(); list.Clear(); Assert.Single(array.Values); Assert.Single(array.Values[0].Object!);
    }
    [Fact]
    public void UndefinedDotNetEnumsCannotBecomeSourceSlotsOrGapKinds()
    {
        Assert.Throws<VocabularyContentValidationException>(() => ReviewCardPolicy.Gap("bank.", [new(0, 4)], (VocabularyEntryKind)999));
        Assert.Throws<VocabularyException>(() => ReviewLayoutPolicy.Validate(new([new(Id(1), (ReviewContentSource)999)])));
    }
    [Fact]
    public void DisplayOccurrencesDoNotRelaxStoredIdentityOrImagePathAdmission()
    {
        var image = Blocks("image")[0];
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyContentPolicy.RenderHtml([image, image]));
        var content = new ReviewCardContent("bank", "answer", "", [], "", "", "", "", "", true, false) { SenseContent = [image, image] };
        string html = ReviewCardPolicy.Values(content, Id(2))[ReviewField.Meaning]; Assert.Equal(2, html.Split("<img", StringSplitOptions.None).Length - 1);
        Assert.Throws<VocabularyContentValidationException>(() => ReviewCardPolicy.Values(content with { SenseContent = Blocks("invalid") }, Id(2)));
    }
}
