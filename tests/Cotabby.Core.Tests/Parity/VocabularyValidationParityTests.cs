using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Fixed malformed/edge graphs are built here; admission always calls the production boundary.</summary>
public sealed class VocabularyValidationParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-validation-v1.json"))).RootElement.Clone();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsWholeGraphAndNestedAdmission(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static object Observe(JsonElement row)
    {
        string operation = row.GetProperty("operation").GetString()!, mode = row.TryGetProperty("mode", out var m) ? m.GetString()! : "";
        int count = row.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
        if (operation == "endpoint") return new { valid = AnkiArchiveEndpointPolicy.IsValid(row.GetProperty("text").GetString()) };
        if (operation == "filename") return new { valid = VocabularyArchiveValidation.IsValidAudioFilename(row.GetProperty("text").GetString()) };
        try
        {
            switch (operation)
            {
                case "document": VocabularyArchiveValidation.Validate(Document(mode, count)); break;
                case "review": VocabularyArchiveValidation.ValidateReview(Review(mode, count)); break;
                case "layout": ReviewLayoutPolicy.Validate(Layout(mode, count)); break;
                case "templates": VocabularyArchiveValidation.ValidateTemplates(Templates(mode, count)); break;
                default: throw new InvalidOperationException("Unknown validation operation.");
            }
            return new { error = "none" };
        }
        catch (VocabularyException error)
        {
            return new { error = error.Failure switch { VocabularyFailure.InvalidArchive => "invalidArchive", VocabularyFailure.InvalidContent => "invalidContent",
                _ => throw new InvalidOperationException("Unexpected validation failure.") } };
        }
    }
    private static VocabularyDocument Document(string mode, int count)
    {
        var e = Entry(); var s = e.Senses[0]; var u = s.Examples[0];
        switch (mode)
        {
            case "empty": return new();
            case "valid": break;
            case "schema": return new() { SchemaVersion = 4, Entries = [e] };
            case "noSenses": e = e with { Senses = [] }; return new() { Entries = [e] };
            case "expressionBlank": e = e with { Expression = " \n" }; break;
            case "expressionLong": e = e with { Expression = new('a', 262145) }; break;
            case "duplicateKey": return new() { Entries = [e, new(Id(10), e.Expression, e.DictionaryForm, e.Language, e.Kind, [Sense()], Now, Now)] };
            case "aliasEntry": s = s with { MergedFrom = [Id(1)] }; break;
            case "aliasSelf": s = s with { MergedFrom = [Id(2)] }; break;
            case "blockSense": s = s with { Content = [Block(2)] }; break;
            case "blockInvalid": s = s with { Content = [new(Id(10), new VocabularyBlockContent.Text(new(new('a', 262145))))] }; break;
            case "duplicateUsageKey": s = s with { Examples = [u, Usage(4)] }; break;
            case "duplicateUsageID": s = s with { Examples = [u, u with { Text = "A bank opened.", Ranges = [new(2, 4)] }] }; break;
            case "twoStandalone": return new() { Entries = [e with { Senses = [s with { IsSavedWithoutContext = true }, new(Id(4), "", Now, Now) { IsSavedWithoutContext = true }] }] };
            case "usageBlank": s = s with { Examples = [u with { Text = "\u200b", Ranges = [] }] }; break;
            case "usageBOM": s = s with { Examples = [u with { Text = "\ufeff", Ranges = [] }] }; break;
            case "usageLong": s = s with { Examples = [u with { Text = new('a', count), Ranges = [] }] }; break;
            case "emptyRanges": s = s with { Examples = [u with { Ranges = [] }] }; break;
            case "overlap": s = s with { Examples = [u with { Ranges = [new(4, 4), new(4, 4)] }] }; break;
            case "partialGrapheme": s = s with { Examples = [u with { Text = "cafe\u0301", Ranges = [new(3, 1)] }] }; break;
            case "invalidRange": s = s with { Examples = [u with { Ranges = [new(999, 1)] }] }; break;
            case "senseRecords":
                var generalInput = LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(e, null, s));
                s = s with { Explanations = Enumerable.Range(0, count).Select(n => Record(100 + n, generalInput)).ToArray() }; break;
            case "usageRecords": case "staleRecord":
                var input = LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(e, u, s));
                u = u with { Explanations = Enumerable.Range(0, Math.Max(1, count)).Select(n => Record(100 + n, input)).ToArray() };
                if (mode == "staleRecord") u = u with { Text = u.Text + " changed" };
                s = s with { Examples = [u] }; break;
            case "cardNoLayout": s = s with { ReviewCards = [new(new(Id(10)))] }; break;
            case "cardEntryID": s = s with { ReviewCards = [new(new(Id(1)) { Layout = new() })] }; break;
            case "legacyEntryID": s = s with { Review = new(new(Id(1))) }; break;
            case "sharedLegacyCardPlan": var review = new VocabularyReview(new(Id(10)) { Layout = new() }); s = s with { Review = review, ReviewCards = [review] }; break;
            case "templateEntryID": return new() { Entries = [e], ReviewTemplates = [new(Id(1), "Template", new())] };
            default: throw new InvalidOperationException("Unknown document fixture.");
        }
        return new() { Entries = [e with { Senses = [s] }] };
    }
    private static ReviewLayout Layout(string mode, int count) => mode switch
    {
        "empty" => new(),
        "parts" => new(Enumerable.Range(0, count).Select(n => Part(100 + n)).ToArray()),
        "duplicateParts" => new([Part(100)], [Part(100)]),
        "foreignContent" => new([new(Id(100), ReviewContentSource.Expression, content: [Block(200)])]),
        "duplicateBlocks" => new([new(Id(100), ReviewContentSource.Custom, content: [Block(200)]), new(Id(101), ReviewContentSource.Custom, content: [Block(200)])]),
        "aggregateBlocks" => new(Enumerable.Range(0, 2).Select(i => new ReviewCardPart(Id(100 + i), ReviewContentSource.Custom,
            content: Enumerable.Range(0, 33).Select(n => Block(200 + i * 33 + n)).ToArray())).ToArray()),
        "dangling" => new([new(Id(100), ReviewContentSource.SenseImage, Id(999))]),
        _ => throw new InvalidOperationException("Unknown layout fixture."),
    };
    private static IReadOnlyList<ReviewLayoutTemplate> Templates(string mode, int count) => mode switch
    {
        "count" => Enumerable.Range(0, count).Select(n => new ReviewLayoutTemplate(Id(100 + n), "Same name", new())).ToArray(),
        "duplicate" => [new(Id(1), "Template", new()), new(Id(1), "Template", new())],
        "blank" => [new(Id(1), "\u200b", new())], "bom" => [new(Id(1), "\ufeff", new())], "name" => [new(Id(1), new('é', count), new())],
        _ => throw new InvalidOperationException("Unknown template fixture."),
    };
    private static VocabularyReview Review(string mode, int count)
    {
        var value = new VocabularyReview(new(Id(10)));
        return mode switch
        {
            "default" => value,
            "name" => value with { Plan = value.Plan with { Name = new('é', count) } },
            "answer" => value with { Plan = value.Plan with { Answer = new('a', count) } },
            "overrides" => value with { Plan = value.Plan with { FieldOverrides = Enumerable.Range(0, count).ToDictionary(n => "field" + n, _ => "") } },
            "overrideKey" => value with { Plan = value.Plan with { FieldOverrides = new Dictionary<string, string> { [new('a', count)] = "" } } },
            "overrideValue" => value with { Plan = value.Plan with { FieldOverrides = new Dictionary<string, string> { [""] = new('a', count) } } },
            "preserved" => value with { PreservedPlans = Enumerable.Range(0, count).Select(_ => new ReviewPlan(Id(10))).ToArray() },
            "bindings" => value with { Bindings = Enumerable.Range(0, count).Select(n => Binding(100 + n)).ToArray() },
            "duplicateBinding" => value with { Bindings = [Binding(), Binding()] },
            "missingActive" => value with { ActiveBindingId = Id(20) },
            "bindingValid" => value with { Bindings = [Binding()] },
            "noteZero" => value with { Bindings = [Binding(note: 0)] },
            "noteNull" => value with { Bindings = [Binding(note: null)] },
            "endpoint" => value with { Bindings = [Binding(endpoint: "https://localhost")] },
            "identityCase" => value with { Bindings = [Binding() with { Fields = new Dictionary<string, string> { ["ID"] = Id(0xabcdef).ToString("D").ToUpperInvariant() } }] },
            "receipt" or "receiptEmoji" or "receiptCombining" or "receiptShort" => Receipt(value, mode),
            _ => throw new InvalidOperationException("Unknown review fixture."),
        };
    }
    private static VocabularyReview Receipt(VocabularyReview review, string mode)
    {
        string token = mode == "receiptEmoji" ? "😀" : mode == "receiptCombining" ? "e\u0301" : "x";
        string digest = string.Concat(Enumerable.Repeat(token, mode == "receiptShort" ? 63 : 64));
        return review with { Plan = review.Plan with { AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> {
            [ReviewAudioKind.Expression] = new(digest, digest, "http://localhost", " ", "lingrove-" + new string('a', 64) + ".wav") } } };
    }
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12", CultureInfo.InvariantCulture));
    private static VocabularyExample Usage(int n = 3) => new(Id(n), "The bank.", "", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "", Now);
    private static VocabularySense Sense() => new(Id(2), "", Now, Now) { Examples = [Usage()] };
    private static VocabularyEntry Entry() => new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [Sense()], Now, Now);
    private static VocabularyContentBlock Block(int n) => new(Id(n), new VocabularyBlockContent.Text(new("literal")));
    private static ReviewCardPart Part(int n) => new(Id(n), ReviewContentSource.Custom);
    private static AnkiNoteBinding Binding(int n = 20, long? note = 1, string endpoint = "http://localhost:8765/") => new(Id(n), Id(0xabcdef), endpoint, " ", note, " ", "", "ID",
        new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = Id(0xabcdef).ToString("D") });
    private static LearningExplanationRecord Record(int n, LearningExplanationInput input) => new(Id(n), input, LearningLanguage.Chinese, new("meaning", "explanation"), "", Now);
}
