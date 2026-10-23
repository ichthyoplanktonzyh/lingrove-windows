using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

public sealed class VocabularyExportOfferParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-export-offer-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalMacNotebookOwnsOfferOrderAndInitialScope(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/UI/Vocabulary/VocabularyNotebookView.swift", row.GetProperty("macSource").GetString());
        var now = DateTimeOffset.UnixEpoch; bool tie = row.TryGetProperty("tie", out var t) && t.GetBoolean();
        var document = new VocabularyDocument { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            [new VocabularySense(Id(2), "core", now, now) { Tags = ["Work"] }, new VocabularySense(Id(4), "second", now, now) { Tags = ["Home"] }], now, now),
            new(Id(10), "café", "café", LearningLanguage.French, VocabularyEntryKind.Word,
                [new VocabularySense(Id(12), "coffee", now, now) { Tags = ["Work"] }], now, tie ? now : now.AddSeconds(10))] };
        string query = row.TryGetProperty("query", out var q) ? q.GetString()! : "", tag = row.TryGetProperty("tag", out var tagValue) ? tagValue.GetString()! : "";
        LearningLanguage? language = row.TryGetProperty("language", out var l) ? LearningLanguages.All.Single(value => value.Code() == l.GetString()) : null;
        bool newest = !row.TryGetProperty("newestFirst", out var n) || n.GetBoolean();
        var batch = row.TryGetProperty("batch", out var b) ? b.EnumerateArray().Select(Pair).ToHashSet() : [];
        VocabularySelection? single = row.TryGetProperty("single", out var one) ? Pair(one) : null;
        VocabularyExportScope? scope = row.TryGetProperty("scope", out var s) ? Enum.Parse<VocabularyExportScope>(s.GetString()!, true) : null;
        // These fixtures use ASCII literal queries only. They do not claim OS linguistic search
        // equivalence; that separate contract has its own actual-platform notebook suite.
        var visible = VocabularyNotebookPolicy.VisibleEntries(document, new LiteralSearch(), query, language, tag, newest);
        var offered = VocabularyExportOfferPolicy.Request(document.Entries, visible, batch, newest, query, language, tag, scope, single);
        var actual = JsonSerializer.SerializeToNode(new { initialScope = offered.InitialScope.ToString().ToLowerInvariant(),
            all = Describe(offered.All), filtered = Describe(offered.Filtered), selected = Describe(offered.Selected) });
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static VocabularySelection Pair(JsonElement value) => new(Id(value[0].GetInt32()), Id(value[1].GetInt32()));
    private static string[][] Describe(IEnumerable<VocabularySelection> values) => values.Select(value => new[] { value.EntryId.ToString("D"), value.SenseId.ToString("D") }).ToArray();
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12", CultureInfo.InvariantCulture));
    private sealed class LiteralSearch : IVocabularyTextSearching { public bool Contains(string value, string query) => value.Contains(query, StringComparison.OrdinalIgnoreCase); }
}
