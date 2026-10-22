using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningLexiconResourceTests
{
    public static string ResourcePath => Path.Combine(AppContext.BaseDirectory, "Resources", "LearningLexicon", "learning-expressions.json");

    [Fact]
    public void ActualPinnedResourceRetainsCountsFormsSensesAndAttribution()
    {
        var bytes = File.ReadAllBytes(ResourcePath);
        Assert.Equal(11_543_088, bytes.Length);
        Assert.Equal("ff66f9835d9cf8f586d0618c09ad3e2b091269bc15bcdac1502660dd0e4c7984",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var resource = new BundledLearningLexiconResourceSource(ResourcePath).Load();
        Assert.Equal("kaikki-2026-10-03-v1", resource.Version);
        Assert.Equal("https://creativecommons.org/licenses/by-sa/4.0/", resource.LicenseUrl);
        Assert.Contains("Wiktionary contributors", resource.Attribution);
        Assert.Equal(34_397, resource.Entries.Count);
        Assert.Equal(13, resource.Entries.Select(entry => entry.Language).Distinct().Count());
        Assert.Equal(9_704, resource.Entries.Count(entry => entry.Language == "zh"));
        var entry = Assert.Single(resource.Entries, entry => entry.Id == "1b618b011fddd4a5");
        Assert.Equal("look forward to", entry.Headword);
        Assert.Contains("looking forward to", entry.Forms);
        Assert.NotEmpty(Assert.Single(entry.Senses).Definitions);
        Assert.StartsWith("https://en.wiktionary.org/", entry.SourceUrl);
        Assert.Null(entry.Phonetics);
        Assert.Null(entry.Examples);
        Assert.Equal(resource.Entries.Count, resource.Entries.Select(entry => entry.Id).Distinct().Count());
        Assert.True(File.Exists(Path.ChangeExtension(ResourcePath, "NOTICE.md")));
        using var provenance = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(ResourcePath, "provenance.json")));
        Assert.Equal(resource.Version, provenance.RootElement.GetProperty("version").GetString());
        Assert.Equal(95, provenance.RootElement.GetProperty("sources").GetArrayLength());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"version\":null,\"attribution\":\"a\",\"licenseURL\":\"l\",\"entries\":[]}")]
    [InlineData("{\"version\":\"v\",\"attribution\":\"a\",\"licenseURL\":\"l\",\"entries\":null}")]
    [InlineData("{\"version\":\"v\",\"attribution\":\"a\",\"licenseURL\":\"l\",\"entries\":[null]}")]
    [InlineData("{\"version\":\"\\ud800\",\"attribution\":\"a\",\"licenseURL\":\"l\",\"entries\":[]}")]
    public void MalformedOrMissingRequiredValuesHaveFixedUnreadableFailure(string json)
    {
        var failure = Assert.Throws<LearningLexiconException>(() => LearningLexiconResourceDecoder.Decode(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(LearningLexiconFailure.UnreadableResource, failure.Failure);
        Assert.DoesNotContain(json.Length > 0 ? json : "JsonException", failure.Message);
    }

    [Fact]
    public void OptionalSourceFieldsAndUnknownFieldsFollowMacDecoderShape()
    {
        const string json = """
        {"version":"v","attribution":"a","licenseURL":"l","unknown":true,"entries":[
        {"id":"id","language":"en","headword":"café","forms":["form"],
        "senses":[{"partOfSpeech":"noun","definitions":["meaning"],"labels":["label"]}],
        "sourceURL":"source","phonetics":["sound"],"examples":["example"],
        "sourceName":"name","licenseURL":"entry-license","licenseName":"license"}]}
        """;
        var entry = Assert.Single(LearningLexiconResourceDecoder.Decode(Encoding.UTF8.GetBytes(json)).Entries);
        Assert.Equal("cafe\u0301", entry.Headword);
        Assert.Equal(["sound"], entry.Phonetics);
        Assert.Equal(["example"], entry.Examples);
        Assert.Equal("name", entry.SourceName);
        Assert.Equal("entry-license", entry.LicenseUrl);
        Assert.Equal("license", entry.LicenseName);
    }

    [Fact]
    public void MissingUnreadableAndOversizedFilesStayDistinctAndDoNotExposePaths()
    {
        Assert.Equal(LearningLexiconFailure.MissingResource,
            Assert.Throws<LearningLexiconException>(() => new BundledLearningLexiconResourceSource(null).Load()).Failure);
        string path = Path.Combine(Path.GetTempPath(), "lingrove-lexicon-" + Guid.NewGuid().ToString("N"));
        var source = new BundledLearningLexiconResourceSource(path);
        var missing = Assert.Throws<LearningLexiconException>(() => source.Load());
        Assert.Equal(LearningLexiconFailure.UnreadableResource, missing.Failure);
        Assert.DoesNotContain(path, missing.ToString());
        try
        {
            using (var file = File.Create(path)) file.SetLength((long)LearningLexiconResource.MaximumBytes + 1);
            Assert.Equal(LearningLexiconFailure.UnreadableResource,
                Assert.Throws<LearningLexiconException>(() => source.Load()).Failure);
            // The failed attempt closed its handle, and a subsequent valid file may be read.
            File.WriteAllText(path, "{\"version\":\"v\",\"attribution\":\"a\",\"licenseURL\":\"l\",\"entries\":[]}");
            Assert.Empty(source.Load().Entries);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnOmittedBundleAssetHasTheSameUnavailableStateAsMacBundleUrlResolution()
    {
        var source = BundledLearningLexiconResourceSource.FromBundleDirectory(
            Path.Combine(Path.GetTempPath(), "lingrove-no-bundle-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(LearningLexiconFailure.MissingResource,
            Assert.Throws<LearningLexiconException>(() => source.Load()).Failure);
        Assert.Equal("kaikki-2026-10-03-v1",
            BundledLearningLexiconResourceSource.FromBundleDirectory(AppContext.BaseDirectory).Load().Version);
    }
}
