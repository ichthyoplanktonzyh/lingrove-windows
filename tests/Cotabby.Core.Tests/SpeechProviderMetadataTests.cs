using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;

namespace Cotabby.Core.Tests;

/// <summary>Actual fixed provider metadata and tag compatibility compared with the original
/// policies. No voice inventory, collation, language detector, file probe or runtime participates.</summary>
public sealed class SpeechProviderMetadataTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-metadata-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(196, rows.Length);
        Assert.Equal("a5ed8d5f808adf72d726a38a6f87ea43a13bd01e11c5091f292bd9218457d5e1", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public void OriginalCompanionInventoryAndLanguageMatch(string id, JsonElement row)
    {
        var actual = new Dictionary<string, object?> { ["decoders"] = null, ["known"] = null, ["mismatch"] = null, ["displayName"] = null, ["code"] = null, ["match"] = null };
        string Text(string key) => row.GetProperty(key).GetString()!;
        LearningLanguage Language() => JsonSerializer.Deserialize<LearningLanguage>(row.GetProperty("language").GetRawText());
        switch (Text("kind"))
        {
            case "pair":
                actual["decoders"] = SpeechModelPairingPolicy.DecoderFilenames(Text("model")); actual["known"] = SpeechModelPairingPolicy.IsKnownDecoder(Text("decoder"));
                actual["mismatch"] = SpeechModelPairingPolicy.IsKnownMismatch(Text("model"), Text("decoder")); actual["displayName"] = SpeechModelPairingPolicy.DisplayName(Text("filename")); break;
            case "local": actual["code"] = LocalSpeechLanguage.Code(Language()); break;
            case "match": actual["match"] = SpeechVoicePolicy.MatchLanguage(Text("tag"), Language()) is { } match ? (int)match : null; break;
            default: throw new InvalidOperationException("Unknown speech metadata action");
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var observed = JsonSerializer.SerializeToNode(actual);
        Assert.True(JsonNode.DeepEquals(expected, observed), id + "\nExpected: " + expected + "\nActual: " + observed);
    }
    [Fact]
    public void PublishedCompanionListCannotMutateTheSharedPackageMetadata()
    {
        const string model = "Qwen3-TTS-12Hz-0.6B-Base.Q4_K_M.gguf";
        var names = SpeechModelPairingPolicy.DecoderFilenames(model); var expected = names.ToArray();
        Assert.False(names is string[]); var mutableInterface = Assert.IsAssignableFrom<IList<string>>(names);
        Assert.Throws<NotSupportedException>(() => mutableInterface[0] = "changed.gguf");
        Assert.Equal(expected, SpeechModelPairingPolicy.DecoderFilenames(model));
        Assert.False(SpeechModelPairingPolicy.IsKnownDecoder("changed.gguf")); Assert.False(SpeechModelPairingPolicy.IsKnownMismatch(model, "changed.gguf"));
    }
}
