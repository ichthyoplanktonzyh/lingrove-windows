using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class VocabularyFrozenDocumentCodecTests
{
    private static readonly DateTimeOffset Now = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static R.VocabularyFrozenDocument Fixture()
    {
        var entry = new R.VocabularyEntry(Id(1), "hello", "hello", null, VocabularyEntryKind.Word,
            [new R.VocabularySense(Id(2), "greeting", Now, Now) { CreatedWithoutContext = true }], Now, Now);
        var source = new R.VocabularySourceSnapshot(Id(3), "hello", R.VocabularySourceKind.Imported, "org.test", Now);
        var claim = new R.VocabularyInterpretation(Id(5), Id(4), Now) { SenseId = Id(2), Status = R.VocabularyInterpretationStatus.Accepted,
            Translation = "你好", TranslationLanguage = LearningLanguage.Chinese };
        var node = new R.VocabularyMeaningNode(Id(6), [new("Greeting") { Id = Id(7), Language = LearningLanguage.English }])
            { CreatedAt = Now, UpdatedAt = Now };
        var category = new R.VocabularyCategory(Id(9), R.VocabularyCategoryAxis.Content, [new("Category") { Id = Id(10) }]);
        var resources = new R.VocabularyDocument { Catalog = [entry], Sources = [source], Occurrences = [new(Id(4), Id(3), [new(0,5)])], Interpretations = [claim],
            Collections = [new R.VocabularyCollectionItem(Id(2), Now) { InterpretationIds = [Id(5)] }],
            Semantics = new() { Nodes = [node], Mappings = [new(node.Id, Id(2)) { Id = Id(8), EvidenceIds = [Id(5)] }] } };
        return new(resources, new() { Categories = [category], Memberships = [new(Id(11), node.Id, category.Id)] });
    }
    private static R.VocabularyFrozenDocument FullFixture()
    {
        var value = Fixture(); var r = value.Resources; var entry = r.Catalog[0]; var sense = entry.Senses[0];
        var input = new R.LearningExplanationInput("hello", "hello", null, VocabularyEntryKind.Word, "hello", [new(0,5)], R.LearningExplanationContext.Unknown,
            "c4a7706c333e599abcbeb7c0fb75d8fdeb9ccb662ea94ba0fcc814ad1dccbe22");
        var record = new R.LearningExplanationRecord(Id(12), input, LearningLanguage.Chinese, new("你好", "问候语。"), "local", Now);
        var generalInput = new R.LearningExplanationInput("hello", "hello", null, VocabularyEntryKind.Word, "hello", [new(0,5)], R.LearningExplanationContext.None,
            "sense:" + sense.Id.ToString("D").ToUpperInvariant());
        sense = sense with { StandaloneMeanings = [new("你好", LearningLanguage.Chinese)], AlternateMeanings = [new("salut", LearningLanguage.French)],
            Explanations = [new(Id(35), generalInput, LearningLanguage.Chinese, new("你好", "问候语。"), "local", Now)] };
        var claim = r.Interpretations[0] with { Explanations = [record], TranslationVariants = [new VocabularyMeaning("你好", LearningLanguage.Chinese)] };
        var content = new VocabularyContentBlock(Id(13), new VocabularyBlockContent.Image(new(new string('a',64), 2, 3, 20), "图"));
        var layout = new ReviewLayout([new ReviewCardPart(Id(14), ReviewContentSource.Custom, content: [new(Id(15), new VocabularyBlockContent.Text(new("card")))])], []);
        var receipt = new ReviewAudioReceipt(new string('a',64), new string('b',64), "http://localhost:8765", "Default", "lingrove-" + new string('c',64) + ".wav");
        var plan = new ReviewPlan(Id(16)) { Layout = layout, ExampleId = Id(5), AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression },
            AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> { [ReviewAudioKind.Expression] = receipt }, FieldOverrides = new Dictionary<string,string> { ["Front"] = "Note" } };
        var binding = new AnkiNoteBinding(Id(17), Id(2), "http://localhost:8765", "Default", 42, "Model", "Deck", "LingroveID",
            new Dictionary<ReviewField,string> { [ReviewField.Identity] = "LingroveID" }, new Dictionary<string,string> { ["LingroveID"] = Id(2).ToString("D") }, Now);
        var review = new VocabularyReview(plan, [binding], binding.Id);
        var collection = r.Collections[0] with { Note = "note", Tags = ["tag"], Content = [content], Review = review, ReviewCards = [] };
        var role = new R.VocabularyMeaningRole("object") { Id = Id(20) };
        var node = r.Semantics.Nodes[0] with { Roles = [role] };
        var other = new R.VocabularyMeaningNode(Id(21), [new("Other") { Id = Id(22) }]) { CreatedAt = Now, UpdatedAt = Now };
        var slot = new R.VocabularyPatternSlot("object") { Id = Id(23) };
        var pattern = new R.VocabularyUsagePattern(Id(2), "hello [object]") { Id = Id(24), Slots = [slot], Collocations = ["hello friend"] };
        var mapping = r.Semantics.Mappings[0] with { PatternId = pattern.Id, RoleBindings = [new(role.Id, slot.Id) { Id = Id(25) }],
            Provenance = new() { Origin = R.VocabularySemanticOrigin.Model, Status = R.VocabularySemanticStatus.Proposed, Source = "test", Confidence = 0.7 } };
        var cue = new R.VocabularyRetrievalCue("问候") { Id = Id(26), Language = LearningLanguage.Chinese };
        var semantics = r.Semantics with { Nodes = [node, other], Patterns = [pattern], Mappings = [mapping], Relations = [new(node.Id,other.Id) { Id = Id(27) }],
            Cues = [cue], CueTargets = [new(cue.Id,node.Id) { Id = Id(28) }] };
        var child = new R.VocabularyCategory(Id(29), R.VocabularyCategoryAxis.Content, [new("Child") { Id = Id(30) }]);
        var directory = value.Classification with { Frameworks = [new(Id(34), "framework", 1)], Categories = [value.Classification.Categories[0] with { FrameworkId = Id(34), Key = "root" }, child],
            Parents = [new(Id(31), child.Id, Id(9))], Overrides = [new(Id(9)) { Labels = [new("Local") { Id = Id(32) }], Definition = "", IsHidden = true }] };
        return value with { Resources = r with { Catalog = [entry with { Senses = [sense] }], Sources = [.. r.Sources, new(Id(36), "uncollected", R.VocabularySourceKind.Imported, null, Now)], Interpretations = [claim], Collections = [collection], Semantics = semantics,
            ReviewTemplates = [new(Id(33), "Template", new())] }, Classification = directory };
    }
    private const string SourceV7 = """
        {"schemaVersion":7,
         "catalog":[{"id":"00000000-0000-0000-0000-000000000001","expression":"hi","dictionaryForm":"hi","kind":"word",
           "senses":[{"id":"00000000-0000-0000-0000-000000000002","meaning":"hello","meaningOrigin":"captured","note":"","tags":[],"examples":[],"isSavedWithoutContext":false,"standaloneMeanings":[],"explanations":[],"alternateMeanings":[],"mergedFrom":[],"createdAt":0,"updatedAt":0}],"createdAt":0,"updatedAt":0}],
         "sources":[{"id":"00000000-0000-0000-0000-000000000003","text":"hi","kind":"imported","encounteredAt":0}],
         "occurrences":[{"id":"00000000-0000-0000-0000-000000000004","sourceID":"00000000-0000-0000-0000-000000000003","ranges":[{"location":0,"length":2}]}],
         "interpretations":[{"id":"00000000-0000-0000-0000-000000000005","occurrenceID":"00000000-0000-0000-0000-000000000004","status":"candidate","meaning":"","meaningOrigin":"captured","translation":"","translationVariants":[],"completeness":"unknown","provenance":{"origin":"user","status":"confirmed","source":""},"explanations":[],"createdAt":0}],
         "collections":[],"semantics":{"nodes":[],"patterns":[],"mappings":[],"relations":[],"cues":[],"cueTargets":[]}}
        """;
    [Fact]
    public void V8RejectsWrongAxisShapeReviewMapShapeAndUnrepresentableIntegerRanges()
    {
        var original = R.VocabularyFrozenDocumentCodec.Encode(FullFixture());
        var wrongAxis = JsonNode.Parse(original)!; wrongAxis["classification"]!["categories"]![0]!["axis"] = "Content";
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(wrongAxis.ToJsonString())));
        var wrongMap = JsonNode.Parse(original)!; wrongMap["collections"]![0]!["review"]!["bindings"]![0]!["mapping"] = new JsonObject { ["LingroveID"] = "LingroveID" };
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(wrongMap.ToJsonString())));
        var hugeRange = JsonNode.Parse(original)!; hugeRange["occurrences"]![0]!["ranges"]![0]!["location"] = 2_147_483_648L;
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(hugeRange.ToJsonString())));
    }
    [Fact]
    public void DecodeOwnsItsValuesAndAcceptsFoundationUtf16Input()
    {
        var bytes = Encoding.UTF8.GetBytes(SourceV7); var decoded = R.VocabularyFrozenDocumentCodec.Decode(bytes);
        Array.Fill(bytes, (byte)0); Assert.Equal("hi", decoded.Resources.Catalog[0].Expression);
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(SourceV7)).ToArray();
        Assert.Equal(decoded, R.VocabularyFrozenDocumentCodec.Decode(utf16));
        Assert.Throws<NotSupportedException>(() => ((IList<R.VocabularyEntry>)decoded.Resources.Catalog).Clear());
    }
    [Fact]
    public void BoundedCollectionAndDepthChecksRunBeforeGraphMaterialization()
    {
        var document = JsonNode.Parse(R.VocabularyFrozenDocumentCodec.Encode(FullFixture()))!;
        var roles = new JsonArray(); for (int i = 0; i < 65; i++) roles.Add((JsonNode?)null);
        document["semantics"]!["nodes"]![0]!["roles"] = roles;
        var error = Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        Assert.Equal(VocabularyFailure.InvalidContent, error.Failure);
        var deep = SourceV7.Replace("\"schemaVersion\":7", "\"extra\":" + new string('[', 513) + "0" + new string(']', 513) + ",\"schemaVersion\":7");
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(deep)));
    }
    [Fact]
    public void EncodingValidatesOwnershipAndRejectsUnrepresentableDates()
    {
        var value = Fixture();
        var bad = value with { Resources = value.Resources with { Sources = [] } };
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Encode(bad));
        var source = new R.VocabularySourceSnapshot(Id(3), "hello", R.VocabularySourceKind.Imported, null, DateTimeOffset.MaxValue);
        var date = value with { Resources = value.Resources with { Sources = [source] } };
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Encode(date));
    }
    [Fact]
    public void SourceShapedV7UpgradesWithoutChangingEvidenceOrSynthesizingClassification()
    {
        var source = Encoding.UTF8.GetBytes(SourceV7); var value = R.VocabularyFrozenDocumentCodec.Decode(source);
        Assert.Equal(7, value.Resources.SchemaVersion); Assert.Null(value.Resources.Catalog[0].Language);
        Assert.Equal(Id(3), Assert.Single(value.Resources.Sources).Id); Assert.Equal(Id(5), Assert.Single(value.Resources.Interpretations).Id);
        Assert.Equal(R.VocabularyInterpretationStatus.Candidate, value.Resources.Interpretations[0].Status);
        Assert.Empty(value.Classification.Categories); Assert.Empty(value.Resources.Collections);
        var encoded = JsonNode.Parse(R.VocabularyFrozenDocumentCodec.Encode(value))!;
        Assert.Equal(8, (int)encoded["schemaVersion"]!); Assert.NotNull(encoded["classification"]);
        var ignored = JsonNode.Parse(SourceV7)!; ignored["classification"] = "not a v8 directory";
        Assert.Equal(value, R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(ignored.ToJsonString())));
    }
    [Fact]
    public void WireVersionsRequiredFieldsEnumsAndAdmissionFailClosed()
    {
        static VocabularyFailure Failure(string text) => Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(text))).Failure;
        Assert.Equal(VocabularyFailure.UnsupportedVersion, Failure(SourceV7.Replace("\"schemaVersion\":7", "\"schemaVersion\":9")));
        Assert.Equal(VocabularyFailure.InvalidArchive, Failure(SourceV7.Replace("\"schemaVersion\":7", "\"schemaVersion\":8")));
        Assert.Equal(VocabularyFailure.InvalidArchive, Failure(SourceV7.Replace("\"sources\":[", "\"Sources\":[")));
        Assert.Equal(VocabularyFailure.InvalidArchive, Failure(SourceV7.Replace("\"completeness\":\"unknown\"", "\"completeness\":\"UNKNOWN\"")));
        Assert.Equal(VocabularyFailure.InvalidArchive, Failure(SourceV7.Replace("\"completeness\":\"unknown\"", "\"completeness\":2")));
        Assert.Equal(VocabularyFailure.InvalidArchive, Failure(SourceV7.Replace("\"collections\":[]", "\"collections\":null")));
        Assert.Equal(VocabularyFailure.InvalidArchive, Failure(SourceV7.Replace("\"sourceID\":\"00000000-0000-0000-0000-000000000003\"", "\"sourceID\":\"00000000-0000-0000-0000-000000000999\"")));
        var duplicate = SourceV7.Replace("\"schemaVersion\":7", "\"schemaVersion\":7,\"schemaVersion\":8");
        Assert.Empty(R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(duplicate)).Classification.Categories);
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(new byte[] { 0xff }));
    }
    [Fact]
    public void NonRepresentableWireDatesFailWithoutRoundingOrClamping()
    {
        var fractional = SourceV7.Replace("\"encounteredAt\":0", "\"encounteredAt\":0.00000001");
        var outOfRange = SourceV7.Replace("\"encounteredAt\":0", "\"encounteredAt\":99999999999999");
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(fractional)));
        Assert.ThrowsAny<VocabularyException>(() => R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(outOfRange)));
        var representable = SourceV7.Replace("\"encounteredAt\":0", "\"encounteredAt\":0.0000001");
        Assert.Equal(new DateTimeOffset(2001,1,1,0,0,0,TimeSpan.Zero).AddTicks(1), R.VocabularyFrozenDocumentCodec.Decode(Encoding.UTF8.GetBytes(representable)).Resources.Sources[0].EncounteredAt);
    }
    [Fact]
    public void CompleteGraphRoundTripsSourceShapedReviewContentAndMeaningLeaves()
    {
        var source = FullFixture(); var bytes = R.VocabularyFrozenDocumentCodec.Encode(source);
        using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
        var collection = root.GetProperty("collections")[0]; var review = collection.GetProperty("review");
        var binding = review.GetProperty("bindings")[0]; var mapping = binding.GetProperty("mapping");
        Assert.Equal(JsonValueKind.Array, mapping.ValueKind); Assert.Equal("LingroveID", mapping[0].GetString());
        Assert.Equal("LingroveID", mapping[1].GetString());
        var receipts = review.GetProperty("plan").GetProperty("audioReceipts"); Assert.Equal("expression", receipts[0].GetString());
        var image = collection.GetProperty("content")[0].GetProperty("content").GetProperty("image"); Assert.Equal("图", image.GetProperty("caption").GetString());
        Assert.Equal(20, image.GetProperty("_0").GetProperty("byteCount").GetInt64());
        var explanation = root.GetProperty("interpretations")[0].GetProperty("explanations")[0];
        Assert.False(explanation.GetProperty("input").TryGetProperty("language", out _)); Assert.Equal("unknown", explanation.GetProperty("input").GetProperty("contextKind").GetString());
        Assert.Equal("zh-Hans", explanation.GetProperty("language").GetString()); Assert.Equal(JsonValueKind.Number, explanation.GetProperty("createdAt").ValueKind);
        Assert.True(root.GetProperty("semantics").GetProperty("patterns")[0].TryGetProperty("senseID", out _));
        var overlay = root.GetProperty("classification").GetProperty("overrides")[0]; Assert.True(overlay.TryGetProperty("categoryID", out _)); Assert.False(overlay.TryGetProperty("id", out _));
        Assert.Equal(source, R.VocabularyFrozenDocumentCodec.Decode(bytes));
    }
    [Fact]
    public void FlatV8PreservesIndependentEvidenceAndSourceNames()
    {
        var source = Fixture(); var bytes = R.VocabularyFrozenDocumentCodec.Encode(source);
        using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
        Assert.Equal(8, root.GetProperty("schemaVersion").GetInt32()); Assert.True(root.TryGetProperty("catalog", out var catalog));
        Assert.False(root.TryGetProperty("resources", out _)); Assert.False(root.TryGetProperty("entries", out _));
        Assert.False(catalog[0].TryGetProperty("examples", out _)); Assert.False(catalog[0].TryGetProperty("language", out _));
        Assert.Equal("org.test", root.GetProperty("sources")[0].GetProperty("appBundleIdentifier").GetString());
        Assert.Equal(Id(4).ToString("D").ToUpperInvariant(), root.GetProperty("interpretations")[0].GetProperty("occurrenceID").GetString());
        Assert.Equal("unknown", root.GetProperty("interpretations")[0].GetProperty("completeness").GetString());
        Assert.True(root.GetProperty("collections")[0].TryGetProperty("interpretationIDs", out _));
        Assert.True(root.GetProperty("semantics").GetProperty("mappings")[0].TryGetProperty("evidenceIDs", out _));
        Assert.False(root.GetProperty("classification").GetProperty("categories")[0].TryGetProperty("title", out _));
        Assert.Equal(source, R.VocabularyFrozenDocumentCodec.Decode(bytes));
    }
}
