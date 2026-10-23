using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Value = Cotabby.Win32.Vocabulary.VocabularySQLiteConnection.Value;

namespace Cotabby.App.Tests;

/// <summary>
/// Runs actual production SQLite calls and compares observations from the original Mac classes.
/// Fixed graph inputs reuse native leaf bytes and relational input construction from the preceding
/// codec/projection corpora; SQLite expected results come only from the separate native driver.
/// </summary>
public sealed class VocabularySQLiteParityTests
{
    private static JsonElement Load(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name))).RootElement.Clone();
    private static readonly JsonElement Corpus = Load("vocabulary-sqlite-v1.json");
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    internal static Guid Id(int value) => Guid.Parse("00000000-0000-0000-0000-" + value.ToString("x12"));
    internal static VocabularyDocument Document()
    {
        var codec = Load("vocabulary-codec-v1.json").GetProperty("cases").EnumerateArray().ToArray();
        var projection = Load("vocabulary-projection-v1.json").GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "valid").GetProperty("expected");
        var rows = projection.GetProperty("rows").EnumerateArray().Select(row =>
        {
            var table = Enum.GetValues<VocabularyDatabaseTable>().Single(table => table.Name() == row.GetProperty("table").GetString());
            var leaf = codec.Single(value => value.GetProperty("id").GetString() == "encode-" + table.Name() + "-default");
            return new VocabularyDatabaseRecord(table, row.GetProperty("id").GetString()!, row.GetProperty("parent").GetString(), row.GetProperty("position").GetInt32(),
                VocabularyPayloadCodec.Decode(Convert.FromBase64String(leaf.GetProperty("inputBase64").GetString()!), table),
                row.GetProperty("extra").EnumerateArray().Select(value => value.GetString()!).ToArray());
        }).ToArray();
        return VocabularyDatabaseProjection.Document(rows, true);
    }
    internal static VocabularyDocument Sense(VocabularyDocument document, Func<VocabularySense, VocabularySense> change) => document with
    { Entries = [document.Entries[0] with { Senses = [change(document.Entries[0].Senses[0])] }] };
    private static string Error(VocabularyException error) => char.ToLowerInvariant(error.Failure.ToString()[0]) + error.Failure.ToString()[1..];
    private static byte[] Usage(VocabularySQLiteConnection c) => ((Value.Blob)c.Rows("SELECT payload FROM usages")[0][0]).Data;

    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalMacTransactionsOwnExpectedObservations(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(Observe(row.GetProperty("operation").GetString()!));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static object Observe(string mode)
    {
        var c = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(c);
        using var db = VocabularySQLiteDatabase.Admit(c); var old = Document();
        if (mode == "fresh") return new { revision = db.Read().Revision, entries = db.Read().Document.Entries.Count, schema = c.Scalar("PRAGMA user_version"), applicationID = c.Scalar("PRAGMA application_id") };
        db.Write(old, new(), 0);
        if (mode == "roundtrip") return new { equal = db.Read().Document == old, revision = db.Read().Revision };
        if (mode == "noteEdit")
        {
            byte[] bytes = Usage(c); var next = Sense(old, sense => sense with { Note = "edited" }); db.Write(next, old, 1);
            return new { equal = db.Read().Document == next, written = db.LastWrittenRows, usageUnchanged = Usage(c).SequenceEqual(bytes) };
        }
        if (mode == "noop") return new { revision = db.Write(old, old, 1), written = db.LastWrittenRows };
        if (mode is "moveUsage" or "moveReview")
        {
            var source = old.Entries[0].Senses[0]; var target = new VocabularySense(Id(4), "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            target = mode == "moveUsage" ? target with { Examples = source.Examples } : target with { Review = source.Review, ReviewCards = source.ReviewCards };
            var next = Sense(old, _ => target); db.Write(next, old, 1);
            return new { equal = db.Read().Document == next, revision = db.Read().Revision };
        }
        if (mode == "replaceEntry")
        {
            var e = old.Entries[0]; var next = old with { Entries = [new(Id(10), e.Expression, e.DictionaryForm, e.Language, e.Kind, e.Senses, e.CreatedAt, e.UpdatedAt)] };
            db.Write(next, old, 1); return new { equal = db.Read().Document == next, revision = db.Read().Revision };
        }
        if (mode == "stale")
        {
            var next = Sense(old, sense => sense with { Note = "winner" }); db.Write(next, old, 1);
            var error = Assert.Throws<VocabularyException>(() => db.Write(old, old, 1));
            return new { error = Error(error), preserved = db.Read().Document == next, revision = db.Read().Revision };
        }
        if (mode == "overflow")
        {
            c.Execute("UPDATE state SET revision=?", new Value.Integer(long.MaxValue));
            var error = Assert.Throws<VocabularyException>(() => db.Write(old, old, (ulong)long.MaxValue));
            return new { error = Error(error), preserved = db.Read().Document == old };
        }
        if (mode == "rollback")
        {
            c.Execute("CREATE TRIGGER fail_usage BEFORE UPDATE ON usages BEGIN SELECT RAISE(ABORT,'test'); END");
            var next = Sense(old, sense => sense with { Examples = [sense.Examples[0] with { Translation = "changed" }] });
            next = next with { Entries = [next.Entries[0] with { Expression = "changed" }] };
            var error = Assert.Throws<VocabularyException>(() => db.Write(next, old, 1));
            return new { error = Error(error), preserved = db.Read().Document == old, revision = db.Read().Revision };
        }
        if (mode is "receipt" or "receiptRollback")
        {
            var receipt = new VocabularySQLiteReceipt("key", "fingerprint", [0, 1, 255]); db.Write(old, old, 1, receipt);
            if (mode == "receipt") return new { matched = db.Receipt("key") is { } found && found.Fingerprint == receipt.Fingerprint && found.Result.SequenceEqual(receipt.Result), missing = db.Receipt("missing") is null, revision = db.Read().Revision };
            var next = Sense(old, sense => sense with { Note = "not committed" });
            var error = Assert.Throws<VocabularyException>(() => db.Write(next, old, 2, receipt));
            return new { error = Error(error), preserved = db.Read().Document == old, revision = db.Read().Revision };
        }
        var statements = new Dictionary<string, string>
        {
            ["payloadCorrupt"] = "UPDATE usages SET payload=x'7b'", ["payloadText"] = "UPDATE usages SET payload='{}'",
            ["positionGap"] = "UPDATE usages SET position=1", ["positionReal"] = "UPDATE usages SET position=0.5",
            ["indexedCorrupt"] = "UPDATE senses SET meaning='tampered'", ["templateFlag"] = "UPDATE state SET has_templates=0",
        };
        if (statements.TryGetValue(mode, out string? sql))
        {
            c.Execute(sql); var before = c.Rows("SELECT hex(payload) FROM usages")[0][0];
            var error = Assert.Throws<VocabularyException>(() => db.Read());
            return new { error = Error(error), preserved = c.Rows("SELECT hex(payload) FROM usages")[0][0] == before, revision = db.Revision() };
        }
        if (mode == "foreignKey")
        {
            var error = Assert.Throws<VocabularyException>(() => c.Execute("UPDATE usages SET parent_id='orphan'"));
            return new { error = Error(error), preserved = db.Read().Document == old };
        }
        if (mode == "nestedAccess")
        {
            int shared = db.Access(false, () => db.Access(false, () => 1)), exclusive = db.Access(true, () => db.Access(true, () => 2));
            var error = Assert.Throws<VocabularyException>(() => db.Access(false, () => db.Access(true, () => 3)));
            return new { shared, exclusive, error = Error(error) };
        }
        throw new InvalidOperationException("Unknown fixed SQLite fixture operation.");
    }
}
