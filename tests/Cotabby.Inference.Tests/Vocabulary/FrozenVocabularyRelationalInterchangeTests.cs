using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;
using V = Cotabby.Win32.Vocabulary.VocabularySQLiteConnection.Value;
namespace Cotabby.Inference.Tests.Vocabulary;

/// <summary>Real projection and named SQLite file against shared source-shaped row probes.</summary>
public sealed class FrozenVocabularyRelationalInterchangeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-frozen-relational-" + Guid.NewGuid().ToString("N"));
    public FrozenVocabularyRelationalInterchangeTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    private static string Workspace()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        return Assert.IsType<DirectoryInfo>(root).FullName;
    }
    private static JsonObject Row(string table, string id, string? parent, long position, IEnumerable<string?> extra, byte[] payload) => new()
    {
        ["table"] = table, ["id"] = id, ["parent"] = parent, ["position"] = position,
        ["extra"] = JsonSerializer.SerializeToNode(extra), ["payload"] = JsonNode.Parse(payload)
    };
    private static JsonArray Projection(F.VocabularyDocument document) => new(F.VocabularyDatabaseProjection.Rows(document).Values
        .OrderBy(x => x.Table.Name(), StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal)
        .Select(row => (JsonNode?)Row(row.Table.Name(), row.Id, row.ParentId, row.Position, row.Extra, F.VocabularyDatabasePayloadCodec.Encode(row))).ToArray());
    private static JsonArray Stored(VocabularySQLiteConnection connection)
    {
        var rows = new List<JsonObject>();
        foreach (var table in Enum.GetValues<F.VocabularyDatabaseTable>())
        {
            string[] columns = ["id", "parent_id", "position", "payload", .. table.ExtraColumns()];
            foreach (var values in connection.Rows("SELECT " + string.Join(',', columns) + " FROM " + table.Name()))
            {
                var id = Assert.IsType<V.Text>(values[0]).Data; var parent = (values[1] as V.Text)?.Data;
                var position = Assert.IsType<V.Integer>(values[2]).Data; var bytes = Assert.IsType<V.Blob>(values[3]).Data;
                Assert.All(values.Skip(4), value => Assert.True(value is V.Text or V.Null));
                rows.Add(Row(table.Name(), id, parent, position, values.Skip(4).Select(value => (value as V.Text)?.Data), bytes));
            }
        }
        return new(rows.OrderBy(row => (string?)row["table"], StringComparer.Ordinal).ThenBy(row => (string?)row["id"], StringComparer.Ordinal).Select(row => (JsonNode?)row).ToArray());
    }
    private static void EqualJson(JsonElement expected, JsonNode actual)
    {
        using var parsed = JsonDocument.Parse(actual.ToJsonString()); Assert.True(JsonElement.DeepEquals(expected, parsed.RootElement));
    }
    private static long[] Bits(F.VocabularyDocument doc)
    {
        var entry = doc.Catalog[0]; var sense = entry.Senses[0]; var claim = doc.Interpretations[0];
        return new[] { entry.CreatedAt, entry.UpdatedAt, sense.CreatedAt, sense.UpdatedAt, sense.Explanations[0].CreatedAt,
            doc.Sources[0].EncounteredAt, doc.Sources[1].EncounteredAt, claim.CreatedAt, claim.Explanations[0].CreatedAt,
            doc.Collections[0].SavedAt, doc.Collections[0].Review!.Bindings[0].SyncedAt!.Value,
            doc.Semantics.Nodes[0].CreatedAt, doc.Semantics.Nodes[0].UpdatedAt, doc.Semantics.Nodes[1].CreatedAt, doc.Semantics.Nodes[1].UpdatedAt }
            .Select(value => BitConverter.DoubleToInt64Bits(value.ReferenceSeconds)).ToArray();
    }
    [Fact]
    public void SharedCasesMatchProjectionAndPopulatedReopenedSQLite()
    {
        string workspace = Workspace();
        using var documents = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "fixtures/parity/vocabulary-document-frozen-v1.json")));
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "fixtures/parity/vocabulary-relational-frozen-v1.json")));
        Assert.Equal("35b7fbe5c788c5524936ade1dfb7c89a3a285b04", corpus.RootElement.GetProperty("macReference").GetString());
        Assert.Equal("windows-projection-source-shaped", corpus.RootElement.GetProperty("provenance").GetString());
        var cases = corpus.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        foreach (var item in documents.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name = item.GetProperty("id").GetString()!; var expected = cases[name].GetProperty("rows");
            var original = F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("input").GetRawText()));
            if (name == "full-v8") Assert.Equal(29, expected.EnumerateArray().Select(x => x.GetProperty("table").GetString()).Distinct().Count());
            EqualJson(expected, Projection(original));
            string path = Path.Combine(_root, name + ".sqlite");
            using (var writer = FrozenVocabularySQLiteDatabase.OpenFile(path)) Assert.Equal(1UL, writer.Write(original, new(), 0));
            using (var reader = FrozenVocabularySQLiteDatabase.OpenFile(path))
            {
                var snapshot = reader.Read(); Assert.Equal(1UL, snapshot.Revision); Assert.Equal(original, snapshot.Document);
                Assert.Equal(Bits(original), Bits(snapshot.Document)); EqualJson(expected, Projection(snapshot.Document));
            }
            using (var connection = VocabularySQLiteConnection.OpenNamed(path)) EqualJson(expected, Stored(connection));
            if (name == "upgrade-v7")
            {
                using (var connection = VocabularySQLiteConnection.OpenNamed(path))
                {
                    foreach (var table in new[] { "category_overrides", "category_memberships", "category_parents", "categories", "classification_frameworks" })
                        connection.Execute("DROP TABLE " + table);
                    connection.Execute("PRAGMA user_version=4");
                }
                using (var migrated = FrozenVocabularySQLiteDatabase.OpenFile(path))
                {
                    var snapshot = migrated.Read(); Assert.Equal(1UL, snapshot.Revision);
                    Assert.Equal(original, snapshot.Document); Assert.Equal(Bits(original), Bits(snapshot.Document));
                }
                using var connectionAfterMigration = VocabularySQLiteConnection.OpenNamed(path);
                Assert.Equal(5, connectionAfterMigration.Scalar("PRAGMA user_version"));
                EqualJson(expected, Stored(connectionAfterMigration));
            }
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
