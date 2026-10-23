using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Fixed input construction mirrors the Swift driver; all behavior uses production policies.</summary>
public sealed class VocabularySenseParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-sense-v1.json"))).RootElement.Clone();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch, Later = Now.AddSeconds(1);
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray()
        .Select(row => new object[] { row.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsSaveSenseAndExplanationMutationResults(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(Observe(row.GetProperty("operation").GetString()!));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }

    private static object Observe(string mode)
    {
        try
        {
            if (mode.StartsWith("save-", StringComparison.Ordinal))
            {
                var c = Capture(Usage()); var d = new VocabularyDocument();
                if (mode is "save-new" or "save-attach")
                {
                    if (mode == "save-attach") c = c with { Explanation = Record(100, c) };
                    var saved = VocabularyPolicy.Save(c, d, Later); d = saved.Document;
                    return new { returnedMatches = saved.EntryId == d.Entries[0].Id, contains = VocabularyPolicy.Contains(c, d.Entries), entries = d.Entries.Select(View) };
                }
                if (mode == "save-standalone") c = Capture() with { Meaning = "first", MeaningLanguage = LearningLanguage.Chinese };
                d = VocabularyPolicy.Save(c, d, Now).Document;
                switch (mode)
                {
                    case "save-duplicate": c = c with { Example = c.Example! with { Translation = "Banco.", TranslationLanguage = LearningLanguage.Spanish } }; break;
                    case "save-context": c = c with { Example = Usage(11, "A bank opened.", 2) }; break;
                    case "save-cross": c = new("other", "other", LearningLanguage.English, VocabularyEntryKind.Word, Example: Usage()); break;
                    case "save-occurrence":
                        c = c with { Example = Usage(10, "bank and bank.", 0) }; d = VocabularyPolicy.Save(c, new(), Now).Document;
                        c = c with { Example = c.Example! with { Ranges = [new(9, 4)] } }; break;
                    case "save-standalone":
                        d = d with { Entries = [d.Entries[0] with { Senses = [d.Entries[0].Senses[0] with { Meaning = "User summary", MeaningOrigin = VocabularyMeaningOrigin.User }] }] };
                        c = c with { Meaning = "second", MeaningLanguage = LearningLanguage.Spanish }; break;
                    case "save-missing": c = c with { SenseId = Id(999) }; break;
                    case "save-conflict":
                        d = d with { Entries = [d.Entries[0] with { Senses = d.Entries[0].Senses.Append(Sense(3)).ToArray() }] };
                        c = c with { SenseId = Id(3) }; break;
                    case "save-standalone-conflict":
                        d = d with { Entries = [d.Entries[0] with { Senses = [d.Entries[0].Senses[0] with { IsSavedWithoutContext = true }, Sense(3)] }] };
                        c = Capture() with { SenseId = Id(3) }; break;
                    default: throw new InvalidOperationException("Unknown save fixture.");
                }
                Guid firstEntry = d.Entries[0].Id, firstSense = d.Entries[0].Senses[0].Id;
                var result = VocabularyPolicy.Save(c, d, Later); d = result.Document;
                return new { sameEntry = result.EntryId == firstEntry, returnedEntryMatches = d.Entries.FirstOrDefault(entry => entry.Id == result.EntryId)?.Expression == c.Expression,
                    sameFirstSense = d.Entries[0].Senses[0].Id == firstSense,
                    usageIDsUnique = d.Entries.SelectMany(entry => entry.Examples).Select(usage => usage.Id).Distinct().Count() == d.Entries.Sum(entry => entry.Examples.Count),
                    entries = d.Entries.Select(View) };
            }
            var a = Sense(2, [Usage()]); var b = Sense(3); var e = Entry([]);
            if (mode is "destination-alias" or "destination-ambiguous")
            {
                a = a with { MergedFrom = [Id(99)] }; e = Entry([a]); var c = Capture(Usage());
                if (mode == "destination-alias") c = c with { SenseId = Id(99) };
                else { b = b with { Examples = [Usage(11)] }; e = e with { Senses = [a, b] }; }
                var dest = VocabularySensePolicy.Destination(c, e);
                return new { contains = VocabularyPolicy.Contains(c, [e]), sense = dest is { } d ? Label(d.SenseId) : null,
                    usage = dest?.ExampleId is { } u ? Label(u) : null, aliasIndex = VocabularySensePolicy.Index(Id(99), e) };
            }
            if (mode.StartsWith("merge-", StringComparison.Ordinal))
            {
                a = a with { Note = "Source note" }; b = b with { Note = "Target note" };
                switch (mode)
                {
                    case "merge-full":
                        a = a with { Meaning = "Source", MeaningLanguage = LearningLanguage.Chinese, Tags = ["f", "café"],
                            AlternateMeanings = [new("Older source", null)], MergedFrom = [Id(99)], IsSavedWithoutContext = true,
                            StandaloneMeanings = [new("standalone", LearningLanguage.Spanish)], Content = [Block(200, "target"), Block(201, "source")],
                            Review = new(new(Id(300))), ReviewCards = [new(new(Id(302)))], Explanations = [Record(100, Capture(), "source record")] };
                        b = b with { Meaning = "Target", MeaningOrigin = VocabularyMeaningOrigin.User, Examples = [Usage(11, "A bank opened.", 2)],
                            Tags = ["😀", "cafe\u0301", "\ue000"], Content = [Block(200, "target")], Review = new(new(Id(301))), ReviewCards = [] }; break;
                    case "merge-adopt": a = a with { Meaning = "Adopt", MeaningLanguage = LearningLanguage.Spanish, MeaningOrigin = VocabularyMeaningOrigin.User }; break;
                    case "merge-empty": a = a with { Examples = [], Note = "" }; b = b with { Note = "" }; break;
                    case "merge-duplicate": b = b with { Examples = [Usage(11)] }; break;
                    case "merge-alias": a = a with { MergedFrom = [Id(99)] }; break;
                    default: throw new InvalidOperationException("Unknown merge fixture.");
                }
                e = VocabularySensePolicy.Merge(mode == "merge-alias" ? Id(99) : a.Id, b.Id, Entry([a, b]), Later);
                return new { entry = View(e) };
            }
            if (mode.StartsWith("move-", StringComparison.Ordinal))
            {
                a = a with { Note = "Keep source", Review = new(new(Id(300))), MergedFrom = [Id(99)] }; b = b with { MergedFrom = [Id(98)] };
                if (mode == "move-duplicate") b = b with { Examples = [Usage(11)] };
                var moved = VocabularySensePolicy.Move(mode == "move-missing" ? Id(999) : Id(10), mode == "move-source-alias" ? Id(99) : a.Id,
                    mode == "move-new" ? null : mode == "move-target-alias" ? Id(98) : mode == "move-same" ? a.Id : b.Id, Entry([a, b]), Later);
                return new { target = Label(moved.TargetSenseId), targetOwnsUsage = moved.Entry.Senses.FirstOrDefault(sense => sense.Id == moved.TargetSenseId)?.Examples.Any(usage => usage.Id == Id(10)) == true,
                    entry = View(moved.Entry) };
            }
            if (mode.StartsWith("record-", StringComparison.Ordinal))
            {
                var c = Capture(Usage()); var r = Record(100, Capture(Usage()));
                switch (mode)
                {
                    case "record-moved": e = VocabularySensePolicy.Move(Id(10), a.Id, b.Id, Entry([a, b]), Later).Entry; break;
                    case "record-deleted": e = Entry([a with { Examples = [] }]); break;
                    case "record-edited": e = Entry([a with { Examples = [a.Examples[0] with { Text = "The bank changed." }] }]); break;
                    case "record-user": e = Entry([a with { Examples = [a.Examples[0] with { Meaning = "User wording", MeaningOrigin = VocabularyMeaningOrigin.User }] }]); break;
                    case "record-general": case "record-nonempty": case "record-userblank":
                        c = Capture(); r = Record(100, c, "first"); a = a with { Examples = [], IsSavedWithoutContext = true };
                        if (mode == "record-nonempty") a = a with { Meaning = "Captured core" };
                        if (mode == "record-userblank") a = a with { MeaningOrigin = VocabularyMeaningOrigin.User };
                        e = VocabularyPolicy.StoreExplanation(r, Entry([a])); r = Record(101, c, "second"); break;
                    case "record-bounded": e = Entry([a with { Examples = [a.Examples[0] with { Explanations = Enumerable.Range(0, 24).Select(index => Record(200 + index, c, "old " + index, LearningLanguage.Spanish)).ToArray() }] }]); break;
                    case "record-invalid": e = Entry([a]); r = r with { Result = new("", "") }; break;
                    default: throw new InvalidOperationException("Unknown explanation fixture.");
                }
                bool accepts = VocabularyPolicy.Accepts(r, e); e = VocabularyPolicy.StoreExplanation(r, e);
                if (mode == "record-bounded") e = VocabularyPolicy.StoreExplanation(Record(101, c, "refresh"), e);
                return new { accepts, entry = View(e) };
            }
            throw new InvalidOperationException("Unknown sense fixture.");
        }
        catch (VocabularyException error)
        {
            return new { error = error.Failure switch { VocabularyFailure.InvalidContent => "invalidContent",
                VocabularyFailure.EditConflict => "editConflict", _ => throw new InvalidOperationException("Unexpected fixture failure.") } };
        }
    }

    private static object View(VocabularyEntry value) => new
    {
        id = Label(value.Id), expression = value.Expression, created = value.CreatedAt.ToUnixTimeSeconds(), updated = value.UpdatedAt.ToUnixTimeSeconds(),
        senses = value.Senses.Select(s => new { id = Label(s.Id), meaning = s.Meaning, language = Code(s.MeaningLanguage), origin = Code(s.MeaningOrigin),
            note = s.Note, tags = s.Tags, standalone = s.IsSavedWithoutContext,
            variants = s.StandaloneMeanings.Select(v => new { text = v.Text, language = Code(v.Language) }), alternate = s.AlternateMeanings.Select(v => v.Text), aliases = s.MergedFrom.Select(Label),
            content = s.Content?.Select(block => Label(block.Id)), review = s.Review is null ? null : Label(s.Review.Plan.Id),
            preserved = s.Review?.PreservedPlans.Select(plan => Label(plan.Id)) ?? [], cards = s.ReviewCards?.Select(card => Label(card.Plan.Id)),
            created = s.CreatedAt.ToUnixTimeSeconds(), updated = s.UpdatedAt.ToUnixTimeSeconds(), records = Records(s.Explanations),
            usages = s.Examples.Select(u => new { id = Label(u.Id), text = u.Text, ranges = u.Ranges.Select(r => new[] { r.Location, r.Length }),
                translation = u.Translation, translationLanguage = Code(u.TranslationLanguage), variants = u.TranslationVariants.Select(v => new { text = v.Text, language = Code(v.Language) }),
                meaning = u.Meaning, language = Code(u.MeaningLanguage), origin = Code(u.MeaningOrigin), app = u.SourceApplicationId, created = u.CreatedAt.ToUnixTimeSeconds(), records = Records(u.Explanations) }) })
    };
    private static object Records(IReadOnlyList<LearningExplanationRecord> values) => values.Select(record => new { id = Label(record.Id), meaning = record.Result.Meaning, language = Code(record.Language) });
    private static string? Code<T>(T value) => JsonSerializer.SerializeToElement(value).ValueKind == JsonValueKind.Null ? null : JsonSerializer.SerializeToElement(value).GetString();
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12", CultureInfo.InvariantCulture));
    private static string Label(Guid value) { string text = value.ToString("D"); return text.StartsWith("00000000-0000-0000-0000-", StringComparison.Ordinal) ? text : "new"; }
    private static VocabularyExample Usage(int n = 10, string text = "The bank.", int offset = 4) => new(Id(n), text, "银行。", [new(offset, 4)],
        VocabularyExampleCompleteness.Sentence, "context meaning", Now, translationLanguage: LearningLanguage.Chinese, sourceApplicationId: "fixture.app");
    private static VocabularyCapture Capture(VocabularyExample? example = null) => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, Example: example);
    private static VocabularySense Sense(int n, IReadOnlyList<VocabularyExample>? examples = null) => new(Id(n), "", Now, Now) { Examples = examples ?? [] };
    private static VocabularyEntry Entry(IReadOnlyList<VocabularySense> senses) => new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, senses, Now, Now);
    private static VocabularyContentBlock Block(int n, string text) => new(Id(n), new VocabularyBlockContent.Text(new(text)));
    private static LearningExplanationRecord Record(int n, VocabularyCapture capture, string meaning = "generated", LearningLanguage language = LearningLanguage.Chinese) =>
        new(Id(n), LearningExplanationPolicy.Input(capture), language, new(meaning, "Explanation"), "fixture", Now);
}
