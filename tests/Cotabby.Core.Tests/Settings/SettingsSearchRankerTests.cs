using System.Text.Json;
using Cotabby.Core.Settings;

namespace Cotabby.Core.Tests.Settings;

/// <summary>Same committed-source corpus as the Swift verifier and the real Windows ICU view
/// tests. This stand-in supplies Unicode full-fold primitives only, not scoring or accent rules.</summary>
public sealed class SettingsSearchRankerTests
{
    private sealed class Folding : ISettingsSearchFolding
    {
        public string Fold(string value) => SettingsSearchNormalization.Fold(value, CaseFold);
        private static string CaseFold(string value) => value switch
        { "ß" => "ss", "ﬃ" => "ffi", "ς" => "σ", _ => value.ToLowerInvariant() };
    }
    [Fact]
    public void ScoresAndStableNavigationMatchTheCommittedMacCorpus()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "settings-search-3b-v1.json")));
        foreach (var row in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            var items = row.GetProperty("items").EnumerateArray().Select(item => new SettingsSearchItem<string>(
                item.GetProperty("id").GetString()!, item.GetProperty("title").GetString()!,
                item.GetProperty("keywords").EnumerateArray().Select(value => value.GetString()!),
                item.GetProperty("group").GetString()!, item.GetProperty("summary").GetString()!)).ToArray();
            var actual = SettingsSearchRanker.Matches(row.GetProperty("query").GetString()!, items, new Folding());
            var expected = row.GetProperty("expected").EnumerateArray().Select(value => (Id: value.GetProperty("id").GetString(), Score: value.GetProperty("score").GetDouble())).ToArray();
            Assert.True(expected.SequenceEqual(actual.Select(value => ((string?)value.Value, value.Score))), row.GetProperty("id").GetString());
        }
    }
    [Fact]
    public void FoundationProjectionPreservesScriptMarksWidthAndCanonicalIdentity()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "settings-search-3b-v1.json")));
        foreach (var row in json.RootElement.GetProperty("folds").EnumerateArray())
        {
            string expected = row.GetProperty("expected").GetString()!.Normalize();
            string actual = new Folding().Fold(row.GetProperty("text").GetString()!);
            Assert.True(expected == actual, row.GetProperty("id").GetString() + ": " + actual);
        }
    }
    [Fact]
    public void KeywordsAreDetachedAndMalformedTextHasNoSwiftCounterpart()
    {
        var words = new[] { "original" }; var item = new SettingsSearchItem<int>(1, "Title", words, "", ""); words[0] = "changed";
        Assert.Equal("original", Assert.Single(item.Keywords));
        Assert.Throws<ArgumentException>(() => new Folding().Fold("\ud800"));
    }
}
