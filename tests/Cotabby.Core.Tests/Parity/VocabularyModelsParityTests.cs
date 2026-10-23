using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Mirror fixture construction only; all projections/equality/merge use production values.</summary>
public sealed class VocabularyModelsParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-models-v1.json"))).RootElement.Clone();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray()
        .Select(row => new object[] { row.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacModelsOwnOptionalValuesProjectionsAndReviewMerge(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText());
        var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{id}: {actual}");
    }

    private static object Observe(JsonElement row)
    {
        switch (row.GetProperty("operation").GetString())
        {
            case "defaults":
                var document = new VocabularyDocument(); var sense = Sense(1); var plan = Plan(2); var part = Part(3, ReviewContentSource.Custom);
                var snapshot = new VocabularySnapshot(ulong.MaxValue, document);
                return new { version = document.SchemaVersion, entries = document.Entries.Count, templatesAbsent = document.ReviewTemplates is null,
                    senseOrigin = Code(sense.MeaningOrigin), note = sense.Note, tags = sense.Tags.Count, examples = sense.Examples.Count,
                    standalone = sense.IsSavedWithoutContext, standaloneMeanings = sense.StandaloneMeanings.Count,
                    alternateMeanings = sense.AlternateMeanings.Count, aliases = sense.MergedFrom.Count, explanations = sense.Explanations.Count,
                    reviewAbsent = sense.Review is null, cardsAbsent = sense.ReviewCards is null, contentAbsent = sense.Content is null,
                    recognition = plan.Recognition, gap = plan.Gap, includeNotes = plan.IncludeNotes, includeExplanation = plan.IncludeExplanation,
                    includeContentAbsent = plan.IncludeSenseContent is null, layoutAbsent = plan.Layout is null, answerAbsent = plan.Answer is null,
                    audioKindsAbsent = plan.AudioKinds is null, audioReceiptsAbsent = plan.AudioReceipts is null,
                    overrides = plan.FieldOverrides.Count, partBlockAbsent = part.BlockId is null, caption = part.ShowCaption,
                    partContent = part.Content.Count, revision = snapshot.Revision.ToString(CultureInfo.InvariantCulture) };
            case "optional": return Optional(row.GetProperty("field").GetString()!);
            case "collections":
                var a = Plan(1) with { FieldOverrides = new Dictionary<string, string> { ["cafe\u0301"] = "cafe\u0301", ["other"] = "literal" },
                    AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression, ReviewAudioKind.Sentence } };
                var b = Plan(1) with { FieldOverrides = new Dictionary<string, string> { ["other"] = "literal", ["café"] = "café" },
                    AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Sentence, ReviewAudioKind.Expression } };
                ReviewCardPart[] front = [Part(10, ReviewContentSource.Expression), Part(11, ReviewContentSource.Meaning)];
                return new { planEqual = a == b, orderedLayoutEqual = new ReviewLayout(front) == new ReviewLayout(front.Reverse().ToArray()),
                    canonicalTemplateEqual = new ReviewLayoutTemplate(Id(3), "cafe\u0301", new()) == new ReviewLayoutTemplate(Id(3), "café", new()),
                    canonicalTagsEqual = (Sense(2) with { Tags = ["cafe\u0301"] }) == (Sense(2) with { Tags = ["café"] }) };
            case "entry":
                var first = Record(40, "first wins"); var changed = Record(40, "later duplicate");
                var x = Sense(10) with { Explanations = [first], Examples = [Usage(60, [changed, Record(41, "second")])] };
                var y = Sense(11) with { Explanations = [Record(42, "third")], Examples = [Usage(61, [first])] };
                var entry = Entry([x, y, x]);
                return new { usages = entry.Examples.Select(example => Text(example.Id)), records = entry.Explanations!.Select(record => Text(record.Id)),
                    meanings = entry.Explanations!.Select(record => record.Result.Meaning), nestedCount = entry.Senses[0].Examples[0].Explanations.Count,
                    emptyRecordsAbsent = Entry([]).Explanations is null };
            case "slots":
                var original = Sense(1) with { Review = new(Plan(10, ReviewContentSource.Expression), preservedPlans: [Plan(11, ReviewContentSource.Notes)]),
                    ReviewCards = [new(Plan(20, ReviewContentSource.Meaning), preservedPlans: [Plan(21, ReviewContentSource.Translation)])] };
                string slot = row.GetProperty("slot").GetString()!;
                var replacement = new VocabularyReview(Plan(slot == "replace" ? 20 : 30, ReviewContentSource.Custom));
                var value = original.WithReview(replacement, slot == "legacy" ? null : Id(slot == "replace" ? 20 : 30));
                return new { legacy = value.Review is null ? null : Text(value.Review.Plan.Id),
                    cards = (value.ReviewCards ?? []).Select(review => Text(review.Plan.Id)),
                    layouts = value.ReviewLayouts.Select(layout => Code(layout.Parts[0].Source)),
                    selected = value.ReviewValue(slot == "legacy" ? null : replacement.Plan.Id) is { } selected ? Text(selected.Plan.Id) : null,
                    originalCardCount = original.ReviewCards!.Count };
            case "layout":
                var layout = new ReviewLayout([Part(10, ReviewContentSource.ExpressionAudio, [Block(100)]), Part(11, ReviewContentSource.SentenceAudio)],
                    [Part(12, ReviewContentSource.ExpressionAudio, [Block(101)])]);
                return new { parts = layout.Parts.Select(part => Text(part.Id)), content = layout.CustomContent.Select(block => Text(block.Id)),
                    audio = layout.AudioKinds.Select(Code).Order(StringComparer.Ordinal), fields = layout.AudioKinds.Select(kind => Code(kind.Field())).Order(StringComparer.Ordinal) };
            case "merge": return Merge(row.GetProperty("mode").GetString()!);
            default: throw new InvalidOperationException("Unknown fixture operation.");
        }
    }

    private static object Optional(string field)
    {
        var a = Sense(1); var b = a; var x = Plan(2); var y = x; var d = new VocabularyDocument(); var e = d;
        switch (field)
        {
            case "content": b = b with { Content = [] }; break;
            case "reviewCards": b = b with { ReviewCards = [] }; break;
            case "reviewTemplates": e = e with { ReviewTemplates = [] }; break;
            case "answer": y = y with { Answer = "" }; break;
            case "name": y = y with { Name = "" }; break;
            case "deck": y = y with { Deck = "" }; break;
            case "layout": y = y with { Layout = new() }; break;
            case "includeSenseContent": y = y with { IncludeSenseContent = false }; break;
            case "audioKinds": y = y with { AudioKinds = new HashSet<ReviewAudioKind>() }; break;
            case "audioReceipts": y = y with { AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt>() }; break;
            default: throw new InvalidOperationException("Unknown optional fixture.");
        }
        return new { equal = a == b && x == y && d == e };
    }

    private static object Merge(string mode)
    {
        bool nilNote = mode.StartsWith("nullNote", StringComparison.Ordinal);
        var a = Binding(20, 200, nilNote ? null : 1,
            mode == "canonicalScope" ? "loopback-cafe\u0301" : "loopback", mode == "canonicalScope" ? "cafe\u0301" : "profile");
        bool sameOrigin = mode is "sameOrigin" or "nullNoteSameOrigin";
        var b = Binding(30, sameOrigin ? 200 : 300, nilNote ? null : mode is "sameOrigin" or "independent" or "duplicateSource" or "duplicateSourceIdentity" ? 2 : 1,
            mode == "otherEndpoint" ? "other" : mode == "canonicalScope" ? "loopback-café" : "loopback",
            mode == "otherProfile" ? "other" : mode == "canonicalScope" ? "café" : "profile");
        VocabularyReview? target = mode is "targetNil" or "bothNil" ? null : new(Plan(1000),
            mode is "noBindings" or "duplicateSource" or "duplicateSourceIdentity" ? [] : [a], a.Id, [Plan(1001)]);
        VocabularyReview? source = mode is "sourceNil" or "bothNil" ? null : new(Plan(2000),
            mode == "noBindings" ? [] : mode == "duplicateSource" ? [b, b] : mode == "duplicateSourceIdentity" ? [b, Binding(31, 301, 2)] : [b], b.Id, [Plan(2001)]);
        if (AnkiReviewPolicy.Merged(target, source) is not { } merged) return new { absent = true };
        return new { absent = false, plan = Text(merged.Plan.Id), bindings = merged.Bindings.Select(binding => Text(binding.Id)),
            origins = merged.Bindings.Select(binding => Text(binding.OriginSenseId)), preserved = merged.PreservedPlans.Select(plan => Text(plan.Id)),
            active = merged.ActiveBindingId is { } active ? Text(active) : null, needsChoice = merged.NeedsBindingChoice };
    }

    private static Guid Id(int value) => Guid.Parse("00000000-0000-0000-0000-" + value.ToString("x12", CultureInfo.InvariantCulture));
    private static string Text(Guid value) => value.ToString("D");
    private static string Code<T>(T value) => JsonSerializer.SerializeToElement(value).GetString()!;
    private static VocabularyContentBlock Block(int value) => new(Id(value), new VocabularyBlockContent.Text(new("literal")));
    private static ReviewCardPart Part(int value, ReviewContentSource source, IReadOnlyList<VocabularyContentBlock>? content = null) => new(Id(value), source, content: content);
    private static ReviewLayout Layout(ReviewContentSource source) => new([Part(900, source)]);
    private static ReviewPlan Plan(int value, ReviewContentSource? source = null) => new(Id(value)) { Layout = source is { } kind ? Layout(kind) : null };
    private static AnkiNoteBinding Binding(int value, int origin, long? note, string endpoint = "loopback", string profile = "profile") =>
        new(Id(value), Id(origin), endpoint, profile, note, "model", "deck", "Identity",
            new Dictionary<ReviewField, string> { [ReviewField.Identity] = "Identity" }, new Dictionary<string, string> { ["Identity"] = "retained" });
    private static VocabularySense Sense(int value) => new(Id(value), "meaning", Now, Now);
    private static LearningExplanationRecord Record(int value, string meaning) => new(Id(value),
        new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "bank", [new(0, 4)]),
        LearningLanguage.Chinese, new(meaning, "Explanation"), "fixture", Now);
    private static VocabularyExample Usage(int value, IReadOnlyList<LearningExplanationRecord> records) => new(Id(value), "The bank.", "",
        [new(4, 4)], VocabularyExampleCompleteness.Sentence, "", Now, explanations: records);
    private static VocabularyEntry Entry(IReadOnlyList<VocabularySense> senses) => new(Id(1), "bank", "bank", LearningLanguage.English,
        VocabularyEntryKind.Word, senses, Now, Now);
}
