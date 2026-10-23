using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;

namespace Cotabby.Core.Tests;

/// <summary>Original complete catalog ranking/selection with the source-observed comparison
/// dependency injected explicitly. This proves policy tiers, not Windows locale collation.</summary>
public sealed class SpeechVoiceSelectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-voices-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString()); Assert.Equal("en_US", d.RootElement.GetProperty("comparisonLocale").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(112, rows.Length);
        Assert.Equal("18e462748c2c712e83b14559eb0188548f9e5e3883e8f12e95d7628a57878cfb", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public void OriginalRankingAndExplicitOrAutomaticChoice(string id, JsonElement row)
    {
        var expected = row.GetProperty("expected");
        // The Swift verifier compiles the nine original-test cases' literal source catalog;
        // this runner consumes its captured catalog. Other rows supply synthetic catalogs.
        // Captured comparisons stand in only for localizedStandardCompare.
        var input = row.TryGetProperty("catalog", out var catalogValue) ? catalogValue : expected.GetProperty("catalog");
        var catalog = input.EnumerateArray().Select(Voice).ToArray();
        var comparisons = new Dictionary<(string, string), int>();
        foreach (var p in expected.GetProperty("comparisons").EnumerateArray())
            comparisons.TryAdd((p.GetProperty("lhs").GetString()!, p.GetProperty("rhs").GetString()!), p.GetProperty("result").GetInt32());
        var comparer = Comparer<string>.Create((lhs, rhs) => comparisons[(lhs, rhs)]);
        var language = JsonSerializer.Deserialize<LearningLanguage>(row.GetProperty("language").GetRawText());
        var ordered = SpeechVoicePolicy.Voices(catalog, language, comparer);
        var selected = SpeechVoicePolicy.Select(catalog, language, row.GetProperty("preferred").GetString(), comparer);
        var actual = JsonSerializer.SerializeToNode(new { catalog = catalog.Select(Observe), ordered = ordered.Select(Observe), selected = selected is null ? null : Observe(selected) });
        var expectedPolicy = JsonNode.Parse(expected.GetRawText())!.AsObject(); expectedPolicy.Remove("comparisons");
        Assert.True(JsonNode.DeepEquals(expectedPolicy, actual), id + "\nExpected: " + expectedPolicy + "\nActual: " + actual);
    }
    [Fact]
    public void CatalogValuesPreserveCanonicalEqualityAndAllIndependentMetadata()
    {
        var a = new SpeechVoice("cafe\u0301", "name\u0301", "e\u0301n", SpeechVoiceQuality.Premium, true, false);
        var b = new SpeechVoice("café", "namé", "én", SpeechVoiceQuality.Premium, true, false);
        Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode()); Assert.Equal("cafe\u0301", a.Id);
        Assert.NotEqual(a, b with { IsNovelty = true }); Assert.NotEqual(a, b with { IsSystemPreferred = false });
        Assert.NotEqual(a, b with { Quality = SpeechVoiceQuality.Standard }); Assert.NotEqual(a, b with { Language = "en" });
    }
    [Fact]
    public void PublishedSelectionOwnsItsSnapshotAndOnlyUsesCollationForTies()
    {
        var premium = new SpeechVoice("z", "premium", "en", SpeechVoiceQuality.Premium);
        var standard = new SpeechVoice("a", "standard", "en", SpeechVoiceQuality.Standard);
        SpeechVoice[] catalog = [standard, premium];
        var comparer = Comparer<string>.Create((_, _) => throw new InvalidOperationException("Collation is irrelevant before quality ties."));
        var result = SpeechVoicePolicy.Voices(catalog, LearningLanguage.English, comparer); catalog[0] = premium;
        Assert.Equal(new[] { premium, standard }, result); Assert.Throws<NotSupportedException>(() => ((IList<SpeechVoice>)result)[0] = standard);
    }
    private static SpeechVoice Voice(JsonElement v) => new(v.GetProperty("id").GetString()!, v.GetProperty("name").GetString()!, v.GetProperty("language").GetString()!, (SpeechVoiceQuality)v.GetProperty("quality").GetInt32(), v.GetProperty("isSystemPreferred").GetBoolean(), v.GetProperty("isNovelty").GetBoolean());
    private static object Observe(SpeechVoice v) => new { id = v.Id, name = v.Name, language = v.Language, quality = (int)v.Quality, isSystemPreferred = v.IsSystemPreferred, isNovelty = v.IsNovelty };
}
