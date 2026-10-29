using System.Text.Json;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class FrozenVocabularySchemaCompatibilityTests
{
    [Fact]
    public void WindowsSchemaMatchesSharedManifestAndMigration()
    {
        using var db = VocabularySQLiteConnection.OpenMemory();
        FrozenVocabularySQLiteDatabase.CreateSchema(db);
        Dictionary<string, string> Schema() => db.Rows("SELECT type,name,sql FROM sqlite_master WHERE type IN ('table','index','trigger','view') AND substr(name,1,7) <> 'sqlite_'")
            .ToDictionary(row => ((VocabularySQLiteConnection.Value.Text)row[0]).Data + ":" + ((VocabularySQLiteConnection.Value.Text)row[1]).Data,
                row => ((VocabularySQLiteConnection.Value.Text)row[2]).Data, StringComparer.Ordinal);
        var five = Schema();
        foreach (var table in new[] { "category_overrides", "category_memberships", "category_parents", "categories", "classification_frameworks" })
            db.Execute("DROP TABLE " + table);
        db.Execute("PRAGMA user_version=4");
        var four = Schema();
        var actual = new { macReference = "35b7fbe5c788c5524936ade1dfb7c89a3a285b04", schemas = new { schema4 = four, schema5 = five } };
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        Assert.NotNull(root);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "fixtures", "parity", "vocabulary-sqlite-frozen-schema-v1.json")));
        using var observed = JsonDocument.Parse(JsonSerializer.Serialize(actual));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, observed.RootElement));
    }
}

