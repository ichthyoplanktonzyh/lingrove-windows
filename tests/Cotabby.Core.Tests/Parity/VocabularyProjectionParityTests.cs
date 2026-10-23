using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Only fixed input/observation construction is mirrored; production projection owns all behavior.</summary>
public sealed class VocabularyProjectionParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-projection-v1.json"))).RootElement.Clone();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacOwnsRelationalHeadersAndReconstruction(string id)
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
            if (mode == "metadata") return new { tables = Enum.GetValues<VocabularyDatabaseTable>().Select(table => new { name = table.Name(), parent = table.Parent()?.Name(), extra = table.ExtraColumns() }) };
            var d = Document();
            switch (mode)
            {
                case "emptyAbsent": d = new(); break;
                case "emptyPresent": d = new() { ReviewTemplates = [] }; break;
                case "contentAbsent": d = WithSense(d, d.Entries[0].Senses[0] with { Content = null }); break;
                case "contentEmpty": d = WithSense(d, d.Entries[0].Senses[0] with { Content = [] }); break;
                case "cardsAbsent": d = WithSense(d, d.Entries[0].Senses[0] with { ReviewCards = null }); break;
                case "cardsEmpty": d = WithSense(d, d.Entries[0].Senses[0] with { ReviewCards = [] }); break;
            }
            var rows = VocabularyDatabaseProjection.Rows(d).Values.ToList(); int entryIndex = rows.FindIndex(row => row.Table == VocabularyDatabaseTable.Entries);
            int Index(VocabularyDatabaseTable table) => rows.FindIndex(row => row.Table == table);
            switch (mode)
            {
                case "valid": case "emptyAbsent": case "emptyPresent": case "contentAbsent": case "contentEmpty": case "cardsAbsent": case "cardsEmpty": break;
                case "duplicate": rows.Add(rows[0]); break;
                case "negativePosition": rows[entryIndex] = rows[entryIndex] with { Position = -1 }; break;
                case "positionGap": rows[entryIndex] = rows[entryIndex] with { Position = 1 }; break;
                case "wrongEntryParent": rows[entryIndex] = rows[entryIndex] with { ParentId = "orphan" }; break;
                case "wrongEntryID": rows[entryIndex] = rows[entryIndex] with { Id = Text(Id(999)) }; break;
                case "wrongExtra": rows[entryIndex] = rows[entryIndex] with { Extra = ["tampered", "en", "bank"] }; break;
                case "wrongPayload": rows[entryIndex] = rows[entryIndex] with { Payload = new VocabularyDatabasePayload.Usage(d.Entries[0].Examples[0]) }; break;
                case "smuggledEntry": rows[entryIndex] = rows[entryIndex] with { Payload = new VocabularyDatabasePayload.Entry(d.Entries[0]) }; break;
                case "orphanUsage": int usageIndex = Index(VocabularyDatabaseTable.Usages); rows[usageIndex] = rows[usageIndex] with { ParentId = "orphan" }; break;
                case "smuggledSense": int senseIndex = Index(VocabularyDatabaseTable.Senses); rows[senseIndex] = rows[senseIndex] with { Payload = new VocabularyDatabasePayload.Sense(d.Entries[0].Senses[0]) }; break;
                case "hiddenBlocks": case "hiddenCards":
                    int n = Index(VocabularyDatabaseTable.Senses); var s = ((VocabularyDatabasePayload.Sense)rows[n].Payload).Value;
                    s = mode == "hiddenBlocks" ? s with { Content = null } : s with { ReviewCards = null };
                    rows[n] = rows[n] with { Payload = new VocabularyDatabasePayload.Sense(s) }; break;
                case "smuggledBindings": int reviewIndex = Index(VocabularyDatabaseTable.Reviews); rows[reviewIndex] = rows[reviewIndex] with { Payload = new VocabularyDatabasePayload.Review(d.Entries[0].Senses[0].Review!) }; break;
                case "wrongReviewID": int badReview = Index(VocabularyDatabaseTable.Reviews); rows[badReview] = rows[badReview] with { Id = "unknown" }; break;
                case "reviewOrder": for (int i = 0; i < rows.Count; i++) if (rows[i].Table == VocabularyDatabaseTable.Reviews) rows[i] = rows[i] with { Position = 1 - rows[i].Position }; break;
                case "removeUsage": rows.RemoveAll(row => row.Table is VocabularyDatabaseTable.Usages or VocabularyDatabaseTable.UsageExplanations); break;
                case "removeBinding": rows.RemoveAll(row => row.Table == VocabularyDatabaseTable.Bindings && row.ParentId?.EndsWith(":legacy", StringComparison.Ordinal) == true); break;
                case "falseTemplateFlag": break;
                default: throw new InvalidOperationException("Unknown projection fixture.");
            }
            var result = VocabularyDatabaseProjection.Document(rows, mode != "falseTemplateFlag" && d.ReviewTemplates is not null);
            var projected = VocabularyDatabaseProjection.Rows(result).Values.OrderBy(row => row.Table.Name(), StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal);
            return new { rows = projected.Select(row => new { table = row.Table.Name(), id = row.Id, parent = row.ParentId, position = row.Position, extra = row.Extra }),
                templatesPresent = result.ReviewTemplates is not null, entryCount = result.Entries.Count, usageCount = result.Entries.Sum(entry => entry.Examples.Count),
                legacyBindings = result.Entries.FirstOrDefault()?.Senses.FirstOrDefault()?.Review?.Bindings.Count ?? 0, equalOriginal = result == d };
        }
        catch (VocabularyException error) when (error.Failure == VocabularyFailure.InvalidArchive) { return new { error = "invalidArchive" }; }
    }
    internal static VocabularyDocument Document()
    {
        var usage = new VocabularyExample(Id(3), "The bank.", "原句", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "context", Now);
        var sense = new VocabularySense(Id(2), "core", Now, Now) { Note = "note", Tags = ["Work"], IsSavedWithoutContext = true };
        usage = usage with { Explanations = [Record(100, new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, Example: usage))] };
        sense = sense with { Examples = [usage], Explanations = [Record(100, new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word))], Content = [Block(20)] };
        var layout = new ReviewLayout([new(Id(50), ReviewContentSource.Custom, content: [Block(60)])]);
        var binding = new AnkiNoteBinding(Id(40), Id(99), "http://localhost", "profile", 1, "model", "deck", "ID",
            new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = Id(99).ToString("D") });
        var review = new VocabularyReview(new(Id(30)) { Layout = layout }, [binding], binding.Id, [new(Id(31))]);
        sense = sense with { Review = review, ReviewCards = [review] };
        return new() { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense], Now, Now)], ReviewTemplates = [new(Id(70), "Template", layout)] };
    }
    private static VocabularyDocument WithSense(VocabularyDocument d, VocabularySense s) => d with { Entries = [d.Entries[0] with { Senses = [s] }] };
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12", CultureInfo.InvariantCulture));
    private static string Text(Guid value) => value.ToString("D").ToUpperInvariant();
    private static VocabularyContentBlock Block(int n) => new(Id(n), new VocabularyBlockContent.Text(new("literal")));
    private static LearningExplanationRecord Record(int n, VocabularyCapture capture) => new(Id(n), LearningExplanationPolicy.Input(capture), LearningLanguage.Chinese, new("meaning", "explanation"), "fixture", Now);
}
