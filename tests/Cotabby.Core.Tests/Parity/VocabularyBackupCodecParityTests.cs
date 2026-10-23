using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Actual source-produced input and decoder observations cover the full notebook wire
/// graph. Comparison is semantic JSON, because Mac's ordinary encoder leaves object order free.</summary>
public sealed class VocabularyBackupCodecParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-backup-codec-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalCodableOwnsWholeDocumentEnvelopeAndBase64Acceptance(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(53, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Models/Vocabulary/" + (id.StartsWith("document-", StringComparison.Ordinal) ? "VocabularyModels.swift" : "VocabularyContentModels.swift"), row.GetProperty("macSource").GetString());
        JsonNode? actual;
        try
        {
            var input = Convert.FromBase64String(row.GetProperty("inputBase64").GetString()!);
            byte[] output = id.StartsWith("document-", StringComparison.Ordinal)
                ? VocabularyBackupCodec.EncodeDocument(VocabularyBackupCodec.DecodeDocument(input))
                : VocabularyBackupCodec.Encode(VocabularyBackupCodec.Decode(input));
            actual = new JsonObject { ["valid"] = true, ["json"] = JsonNode.Parse(output) };
        }
        catch (VocabularyException error) when (error.Failure == VocabularyFailure.InvalidArchive)
        { actual = new JsonObject { ["valid"] = false }; }
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
}
