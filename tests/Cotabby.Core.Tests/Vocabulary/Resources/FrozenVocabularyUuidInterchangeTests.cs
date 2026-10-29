using System.Text;
using System.Text.Json;
using F = Cotabby.Core.Vocabulary.Frozen;
namespace Cotabby.Core.Tests.Vocabulary.Resources;
public sealed class FrozenVocabularyUuidInterchangeTests
{
    [Fact]
    public void SharedUuidPayloadProbesUseFrozenTableDispatch()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        Assert.NotNull(root);
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "fixtures", "parity", "vocabulary-sqlite-frozen-uuid-v1.json")));
        Assert.Equal("35b7fbe5c788c5524936ade1dfb7c89a3a285b04", corpus.RootElement.GetProperty("macReference").GetString());
        foreach (var test in corpus.RootElement.GetProperty("cases").EnumerateArray())
        {
            var table = test.GetProperty("table").GetString() switch {
                "collection_interpretations" => F.VocabularyDatabaseTable.CollectionInterpretations,
                "mapping_evidence" => F.VocabularyDatabaseTable.MappingEvidence, _ => throw new InvalidOperationException() };
            var id = Assert.IsType<Guid>(F.VocabularyDatabasePayloadCodec.Decode(table, Encoding.UTF8.GetBytes(test.GetProperty("input").GetString()!)));
            var row = new F.VocabularyDatabaseRecord(table, "id", "owner", 0, id);
            using var encoded = JsonDocument.Parse(F.VocabularyDatabasePayloadCodec.Encode(row));
            Assert.Equal(test.GetProperty("expected").GetString(), encoded.RootElement.GetString());
        }
    }
}
