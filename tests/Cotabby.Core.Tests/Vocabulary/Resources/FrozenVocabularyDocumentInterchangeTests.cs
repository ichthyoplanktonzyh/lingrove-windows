using System.Text;
using System.Text.Json;
using F = Cotabby.Core.Vocabulary.Frozen;
namespace Cotabby.Core.Tests.Vocabulary.Resources;

/// <summary>Shared Windows-generated, source-shaped fixtures; native execution is separate.</summary>
public sealed class FrozenVocabularyDocumentInterchangeTests
{
    private static long[] Bits(F.VocabularyDocument doc)
    {
        var entry = doc.Catalog[0]; var sense = entry.Senses[0]; var claim = doc.Interpretations[0];
        return new[] { entry.CreatedAt, entry.UpdatedAt, sense.CreatedAt, sense.UpdatedAt, sense.Explanations[0].CreatedAt,
            doc.Sources[0].EncounteredAt, doc.Sources[1].EncounteredAt, claim.CreatedAt, claim.Explanations[0].CreatedAt,
            doc.Collections[0].SavedAt, doc.Collections[0].Review!.Bindings[0].SyncedAt!.Value,
            doc.Semantics.Nodes[0].CreatedAt, doc.Semantics.Nodes[0].UpdatedAt, doc.Semantics.Nodes[1].CreatedAt, doc.Semantics.Nodes[1].UpdatedAt }
            .Select(value => BitConverter.DoubleToInt64Bits(value.ReferenceSeconds)).ToArray();
    }
    private static long[] Bits(JsonElement doc)
    {
        var entry = doc.GetProperty("catalog")[0]; var sense = entry.GetProperty("senses")[0]; var claim = doc.GetProperty("interpretations")[0];
        var nodes = doc.GetProperty("semantics").GetProperty("nodes"); var sources = doc.GetProperty("sources"); var collection = doc.GetProperty("collections")[0];
        return new[] { entry.GetProperty("createdAt"), entry.GetProperty("updatedAt"), sense.GetProperty("createdAt"), sense.GetProperty("updatedAt"),
            sense.GetProperty("explanations")[0].GetProperty("createdAt"), sources[0].GetProperty("encounteredAt"), sources[1].GetProperty("encounteredAt"),
            claim.GetProperty("createdAt"), claim.GetProperty("explanations")[0].GetProperty("createdAt"), collection.GetProperty("savedAt"),
            collection.GetProperty("review").GetProperty("bindings")[0].GetProperty("syncedAt"), nodes[0].GetProperty("createdAt"), nodes[0].GetProperty("updatedAt"),
            nodes[1].GetProperty("createdAt"), nodes[1].GetProperty("updatedAt") }.Select(value => BitConverter.DoubleToInt64Bits(value.GetDouble())).ToArray();
    }
    [Fact]
    public void SharedCasesPreserveGraphWireShapeAndAllTimestampOwners()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        Assert.NotNull(root);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "fixtures", "parity", "vocabulary-document-frozen-v1.json")));
        Assert.Equal("35b7fbe5c788c5524936ade1dfb7c89a3a285b04", fixture.RootElement.GetProperty("macReference").GetString());
        Assert.Equal("windows-generated-source-shaped", fixture.RootElement.GetProperty("provenance").GetString());
        foreach (var test in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = test.GetProperty("input"); var expected = test.GetProperty("expected");
            var decoded = F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(input.GetRawText()));
            string id = test.GetProperty("id").GetString()!;
            Assert.Equal(8, decoded.SchemaVersion);
            if (id == "upgrade-v7") Assert.Empty(decoded.Classification.Frameworks);
            else Assert.Equal(2147483648L, decoded.Classification.Frameworks[0].Version);
            var collection = input.GetProperty("collections")[0];
            var review = collection.GetProperty("review");
            Assert.Equal(JsonValueKind.Array, review.GetProperty("bindings")[0].GetProperty("mapping").ValueKind);
            Assert.Equal(JsonValueKind.Array, review.GetProperty("plan").GetProperty("audioReceipts").ValueKind);
            if (id == "optional-absence")
            {
                Assert.Null(decoded.ReviewTemplates); Assert.Null(decoded.Collections[0].Content);
                Assert.Null(decoded.Collections[0].ReviewCards);
            }
            var encoded = F.VocabularyDocumentCodec.EncodeAt35b(decoded);
            using var actual = JsonDocument.Parse(encoded);
            Assert.True(JsonElement.DeepEquals(expected, actual.RootElement), test.GetProperty("id").GetString());
            var originalBits = Bits(input); Assert.Equal(originalBits, Bits(decoded));
            Assert.Equal(originalBits, Bits(F.VocabularyDocumentCodec.Decode(encoded)));
        }
    }
}
