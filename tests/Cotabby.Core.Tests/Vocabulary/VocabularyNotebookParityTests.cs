using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Tests.Parity;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

/// <summary>Expected browsing outcomes execute the original Mac computed properties and policy.</summary>
public sealed class VocabularyNotebookParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-notebook-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    private static Guid Id(int value) => Guid.Parse("00000000-0000-0000-0000-" + value.ToString("x12"));
    private static string? String(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedMacNotebookSuppliesSearchFilteringTitlesAndStableSelection(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(Observe(row));
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{id}: {actual}");
    }
    public static IEnumerable<object[]> NativeTextCases() => Corpus.GetProperty("cases").EnumerateArray()
        .Where(row => row.GetProperty("operation").GetString() == "matches" && row.GetProperty("windowsTextParity").GetBoolean())
        .Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(NativeTextCases))]
    public void ActualPlatformSearchMatchesTheExplicitNativeSubset(string id)
    {
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()),
            JsonSerializer.SerializeToNode(Observe(row, new WindowsVocabularyTextSearch()))), id);
    }
    private sealed class RecordedTextFacts(JsonElement facts) : IVocabularyTextSearching
    {
        public bool Contains(string field, string term)
        {
            var matches = facts.EnumerateArray().Where(value => value.GetProperty("field").GetString() == field && value.GetProperty("term").GetString() == term)
                .Select(value => value.GetProperty("contains").GetBoolean()).Distinct().ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Missing or inconsistent actual Mac text fact.");
            return matches[0];
        }
    }
    private static object Observe(JsonElement row, IVocabularyTextSearching? textSearch = null)
    {
        textSearch ??= new RecordedTextFacts(row.GetProperty("facts"));
        var doc = VocabularyProjectionParityTests.Document(); var entry = doc.Entries[0]; var sense = entry.Senses[0];
        string mode = row.GetProperty("operation").GetString()!;
        if (mode == "highlight")
        {
            string text = String(row, "text") ?? new string('x', 12000) + "bank";
            var ranges = row.GetProperty("ranges").EnumerateArray().Select(value => new VocabularyTextRange(value[0].GetInt32(), value[1].GetInt32())).ToArray();
            var example = new VocabularyExample(Id(3), text, "", ranges, VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch);
            return new { runs = VocabularyNotebookPolicy.UsageRuns(example).Select(run => new { text = run.Text, target = run.IsTarget }) };
        }
        if (mode == "matches")
        {
            string value = String(row, "value") ?? "Needle";
            switch (String(row, "field"))
            {
                case "expression": entry = entry with { Expression = value }; break;
                case "dictionaryForm": entry = entry with { DictionaryForm = value }; break;
                case "meaning": sense = sense with { Meaning = value }; break;
                case "note": sense = sense with { Note = value }; break;
                case "tag": sense = sense with { Tags = [value] }; break;
                case "standalone": sense = sense with { StandaloneMeanings = [new(value, null)] }; break;
                case "alternate": sense = sense with { AlternateMeanings = [new(value, null)] }; break;
                case "text": sense = sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new(value)))] }; break;
                case "caption": sense = sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(new(new string('a', 64), 1, 1, 1), value))] }; break;
                case "usageText": sense = sense with { Examples = [sense.Examples[0] with { Text = value }] }; break;
                case "translation": sense = sense with { Examples = [sense.Examples[0] with { Translation = value }] }; break;
                case "usageMeaning": sense = sense with { Examples = [sense.Examples[0] with { Meaning = value }] }; break;
                case "variant": sense = sense with { Examples = [sense.Examples[0] with { TranslationVariants = [new(value, null)] }] }; break;
                case "explanation": sense = sense with { Explanations = [sense.Explanations[0] with { Result = new(value, value) }] }; break;
                default: throw new InvalidOperationException("Unknown fixed field");
            }
            return new { matches = VocabularyNotebookPolicy.Matches(entry with { Senses = [sense] }, String(row, "query") ?? "Needle", textSearch) };
        }
        if (mode == "title")
        {
            sense = sense with { Meaning = "", Examples = [sense.Examples[0] with { Meaning = "" }] };
            sense = String(row, "shape") switch
            {
                "meaning" => sense with { Meaning = "  literal  " },
                "text" => sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("  literal  ")))] },
                "usage" => sense with { Content = [], Examples = [sense.Examples[0] with { Meaning = "usage" }] },
                "image" => sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(new(new string('a', 64), 1, 1, 1), ""))] },
                "empty" => sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new(" \n\t")))] },
                _ => throw new InvalidOperationException("Unknown fixed title"),
            };
            return new { title = VocabularyNotebookPolicy.DisplayTitle(sense) };
        }
        sense = sense with { MergedFrom = [Id(99)], Tags = [String(row, "firstTag") ?? "Work"] };
        entry = entry with { Senses = [sense, new(Id(4), "other", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { Tags = ["Home"] }] };
        var second = new VocabularySense(Id(12), "coffee", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { Tags = ["Work"] };
        doc = doc with { Entries = [entry, new(Id(10), "café", "café", LearningLanguage.French, VocabularyEntryKind.Word, [second], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(10))] };
        if (mode == "selection")
        {
            Guid? target = row.GetProperty("sense").ValueKind == JsonValueKind.Null ? null : Id(row.GetProperty("sense").GetInt32());
            var selected = VocabularyNotebookPolicy.ResolveSelection(doc, target); return new { entry = selected?.EntryId.ToString("D"), sense = selected?.SenseId.ToString("D") };
        }
        var visible = VocabularyNotebookPolicy.VisibleEntries(doc, textSearch, String(row, "query") ?? "", String(row, "language") is { } code ? LearningLanguages.FromCode(code) : null,
            String(row, "tag") ?? "", !row.TryGetProperty("newestFirst", out var newest) || newest.GetBoolean());
        return new { entries = visible.Select(value => value.Id.ToString("D")), senses = visible.Select(value => value.Senses.Select(item => item.Id.ToString("D"))), originalSenses = doc.Entries[0].Senses.Count };
    }
}
