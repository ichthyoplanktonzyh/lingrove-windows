using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyPayloadCodecTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 23, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567);
    private static void Invalid(string json, VocabularyDatabaseTable table) => Assert.Equal(VocabularyFailure.InvalidArchive,
        Assert.Throws<VocabularyException>(() => VocabularyPayloadCodec.Decode(Encoding.UTF8.GetBytes(json), table)).Failure);

    [Fact]
    public void AllTypedRowsRoundTripRichGraphWithoutLosingOptionalValuesProvenanceOrDates()
    {
        var document = RichDocument(); VocabularyArchiveValidation.Validate(document);
        var rows = VocabularyDatabaseProjection.Rows(document).Values.Select(row => row with
        { Payload = VocabularyPayloadCodec.Decode(VocabularyPayloadCodec.Encode(row.Payload), row.Table) }).ToArray();
        var restored = VocabularyDatabaseProjection.Document(rows, true); VocabularyArchiveValidation.Validate(restored);
        Assert.Equal(document, restored); Assert.Equal(Now.UtcTicks, restored.Entries[0].CreatedAt.UtcTicks);
        Assert.Equal("fixture.app", restored.Entries[0].Examples[0].SourceApplicationId);
        Assert.Equal("", restored.Entries[0].Senses[0].ReviewCards![0].Plan.Answer);
        Assert.Equal("cafe\u0301", restored.Entries[0].Senses[0].Tags[0]);
    }

    [Fact]
    public void MacFieldNamesNumericEpochAndOwnerIndependentPayloadShapeAreExplicit()
    {
        var d = RichDocument(); var usage = d.Entries[0].Examples[0];
        var node = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Usage(usage)))!;
        Assert.Equal("fixture.app", node["appBundleIdentifier"]!.GetValue<string>()); Assert.Null(node["sourceApplicationId"]);
        Assert.Equal(usage.Id.ToString("D").ToUpperInvariant(), node["id"]!.GetValue<string>());
        Assert.Equal(JsonValueKind.Number, node["createdAt"]!.GetValueKind()); Assert.Null(node["payload"]);
        var review = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Review(d.Entries[0].Senses[0].Review!)))!;
        Assert.NotNull(review["activeBindingID"]); Assert.Null(review["activeBindingId"]); Assert.Null(review["activeBinding"]);
        Assert.IsType<JsonArray>(review["bindings"]![0]!["mapping"]); Assert.IsType<JsonArray>(review["plan"]!["audioReceipts"]);
    }

    [Fact]
    public void ContentUnionHasActualSwiftAssociatedValueShapeAndComputedValuesAreAbsent()
    {
        var block = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Literal", VocabularyTextStyle.Heading, true, true)));
        var json = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Block(block)))!;
        Assert.Equal("heading", json["content"]!["text"]!["_0"]!["style"]!.GetValue<string>());
        Assert.Equal(block, ((VocabularyDatabasePayload.Block)VocabularyPayloadCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()), VocabularyDatabaseTable.Blocks)).Value);
        var image = VocabularyContentBlock.Create(new VocabularyBlockContent.Image(new(new string('a', 64), 2, 3, 4), "Caption"));
        var encoded = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Block(image)))!;
        Assert.Equal("Caption", encoded["content"]!["image"]!["caption"]!.GetValue<string>()); Assert.Null(encoded["content"]!["image"]!["_0"]!["filename"]);
        Assert.Equal(image, ((VocabularyDatabasePayload.Block)VocabularyPayloadCodec.Decode(Encoding.UTF8.GetBytes(encoded.ToJsonString()), VocabularyDatabaseTable.Blocks)).Value);
    }

    [Fact]
    public void OptionalFieldsMayBeAbsentButNonoptionalDefaultFieldsCannotSilentlyReset()
    {
        var d = RichDocument(); var sense = d.Entries[0].Senses[0] with { Content = null, ReviewCards = null, Review = null, MeaningLanguage = null };
        var encoded = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Sense(sense)))!;
        Assert.Null(encoded["content"]); Assert.Null(encoded["reviewCards"]); Assert.Null(encoded["meaningLanguage"]);
        var decoded = ((VocabularyDatabasePayload.Sense)VocabularyPayloadCodec.Decode(Encoding.UTF8.GetBytes(encoded.ToJsonString()), VocabularyDatabaseTable.Senses)).Value;
        Assert.Equal(sense, decoded);
        foreach (string key in new[] { "id", "meaning", "examples", "explanations", "note", "tags", "meaningOrigin", "isSavedWithoutContext", "createdAt", "updatedAt" })
        {
            var broken = encoded.DeepClone(); broken.AsObject().Remove(key); Invalid(broken.ToJsonString(), VocabularyDatabaseTable.Senses);
        }
        Invalid("{}", VocabularyDatabaseTable.Entries); Invalid("null", VocabularyDatabaseTable.Reviews);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"text\":{}}")]
    [InlineData("{\"image\":{\"_0\":{}}}")]
    [InlineData("{\"text\":{},\"image\":{}}")]
    public void MalformedAssociatedValuesHaveFixedRecoverableFailure(string content) => Invalid("{\"id\":\"" + Guid.NewGuid() + "\",\"content\":" + content + "}", VocabularyDatabaseTable.Blocks);

    [Fact]
    public void WrongEnumDictionaryShapeAndOddPairsFailWithoutLeakingTheirPayload()
    {
        var binding = RichDocument().Entries[0].Senses[0].Review!.Bindings[0]; var json = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Binding(binding)))!;
        json["mapping"] = new JsonObject { ["LingroveID"] = "ID" }; Invalid(json.ToJsonString(), VocabularyDatabaseTable.Bindings);
        json["mapping"] = new JsonArray("LingroveID"); Invalid(json.ToJsonString(), VocabularyDatabaseTable.Bindings);
        json["mapping"] = new JsonArray("unknown", "ID"); Invalid(json.ToJsonString(), VocabularyDatabaseTable.Bindings);
    }

    [Fact]
    public void InvalidUtf8AndDateRangeDoNotTurnIntoReplacementTextOrNativeErrors()
    {
        byte[] prefix = Encoding.UTF8.GetBytes("{\"future\":\""); byte[] bytes = [.. prefix, 0xff, (byte)'"', (byte)'}'];
        Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(() => VocabularyPayloadCodec.Decode(bytes, VocabularyDatabaseTable.Entries)).Failure);
        var json = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Entry(RichDocument().Entries[0])))!;
        json["createdAt"] = decimal.MaxValue; Invalid(json.ToJsonString(), VocabularyDatabaseTable.Entries);
        json["createdAt"] = "not a date"; Invalid(json.ToJsonString(), VocabularyDatabaseTable.Entries);
    }

    [Fact]
    public void EnumWireTokensAreExactAndImpossibleAssociatedValuesCannotEncode()
    {
        var block = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Literal")));
        var json = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Block(block)))!;
        json["content"]!["text"]!["_0"]!["style"] = "PARAGRAPH"; Invalid(json.ToJsonString(), VocabularyDatabaseTable.Blocks);
        var binding = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Binding(RichDocument().Entries[0].Senses[0].Review!.Bindings[0])))!;
        binding["mapping"] = new JsonArray("lingroveid", "ID"); Invalid(binding.ToJsonString(), VocabularyDatabaseTable.Bindings);
        VocabularyBlockContent[] invalid = [new VocabularyBlockContent.Text(null!), new VocabularyBlockContent.Image(null!),
            new VocabularyBlockContent.Image(new(new string('a', 64), 1, 1, 1), null!), new VocabularyBlockContent.Text(new(new string((char)0xD800, 1)))];
        foreach (var content in invalid)
            Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(() => VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Block(block with { Content = content }))).Failure);
    }

    [Fact]
    public void DateAdapterRetainsDotNetTicksAndRejectsUnrepresentableMacDates()
    {
        var entry = RichDocument().Entries[0];
        foreach (var date in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Now })
        {
            var decoded = (VocabularyDatabasePayload.Entry)VocabularyPayloadCodec.Decode(
                VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Entry(new(entry.Id, entry.Expression,
                    entry.DictionaryForm, entry.Language, entry.Kind, entry.Senses, date, entry.UpdatedAt))), VocabularyDatabaseTable.Entries);
            Assert.Equal(date.UtcTicks, decoded.Value.CreatedAt.UtcTicks);
        }
        var json = JsonNode.Parse(VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Entry(entry)))!;
        // Foundation Date has a wider numeric domain. Keep the .NET limit explicit, never clamp
        // or replace an out-of-range persisted date with the current time.
        json["createdAt"] = -63113904001L; Invalid(json.ToJsonString(), VocabularyDatabaseTable.Entries);
    }

    private static VocabularyDocument RichDocument()
    {
        var usage = new VocabularyExample(Guid.NewGuid(), "The bank.", "银行。", [new(4, 4)], VocabularyExampleCompleteness.Sentence,
            "contextual", Now, translationLanguage: LearningLanguage.Chinese, translationVariants: [new("Banco.", LearningLanguage.Spanish)],
            meaningLanguage: LearningLanguage.Chinese, meaningOrigin: VocabularyMeaningOrigin.User, sourceApplicationId: "fixture.app");
        var sense = new VocabularySense(Guid.NewGuid(), "core", Now, Now) { MeaningLanguage = LearningLanguage.Chinese, MeaningOrigin = VocabularyMeaningOrigin.User,
            Note = "My note", Tags = ["cafe\u0301"], Examples = [usage], IsSavedWithoutContext = true, StandaloneMeanings = [new("bank", null)],
            AlternateMeanings = [new("older", LearningLanguage.Spanish)], MergedFrom = [Guid.NewGuid()], Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Literal")))] };
        var layout = new ReviewLayout([ReviewCardPart.Create(ReviewContentSource.Meaning)], [ReviewCardPart.Create(ReviewContentSource.Custom) with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Back")))] }]);
        var plan = ReviewPlan.Create() with { Layout = layout, Name = "", Deck = "deck", Answer = "", ExampleId = usage.Id, Gap = true, IncludeNotes = true,
            IncludeExplanation = true, IncludeSenseContent = false, AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression, ReviewAudioKind.Sentence },
            AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> { [ReviewAudioKind.Expression] = new(new string('a',64), new string('b',64), "http://localhost", "profile", "lingrove-" + new string('a',64) + ".wav") },
            FieldOverrides = new Dictionary<string, string> { ["Front"] = "literal" } };
        var binding = AnkiNoteBinding.Create(sense.Id, "http://localhost:8765", "profile", long.MaxValue, "model", "deck", "ID",
            new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID", [ReviewField.Expression] = "Front" },
            new Dictionary<string, string> { ["ID"] = sense.Id.ToString("D"), ["Front"] = "literal" }) with { SyncedAt = Now };
        var review = new VocabularyReview(plan, [binding], binding.Id, [ReviewPlan.Create()]);
        sense = sense with { Review = review, ReviewCards = [review] };
        return new() { Entries = [new(Guid.NewGuid(), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense], Now, Now)],
            ReviewTemplates = [ReviewLayoutTemplate.Create("Template", layout)] };
    }
}
