using System.Text;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Value = Cotabby.Win32.Vocabulary.VocabularySQLiteConnection.Value;

namespace Cotabby.App.Tests;

public sealed class VocabularySQLiteTests
{
    [Fact]
    public void MemoryFactoriesAreIsolatedAndDisposedOwnersRejectFurtherWork()
    {
        using var first = VocabularySQLiteDatabase.CreateMemory(); using var second = VocabularySQLiteDatabase.CreateMemory();
        first.Write(VocabularySQLiteParityTests.Document(), new(), 0);
        Assert.Equal(1UL, first.Read().Revision); Assert.Equal(0UL, second.Read().Revision); Assert.Empty(second.Read().Document.Entries);
        first.Dispose(); AssertFailure(VocabularyFailure.InvalidArchive, () => first.Read());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void UnknownSchemaIsRejectedWithoutAttemptingAutomaticReset(int version)
    {
        var c = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(c); c.Execute("PRAGMA user_version=" + version);
        AssertFailure(VocabularyFailure.UnsupportedVersion, () => VocabularySQLiteDatabase.Admit(c));
    }
    [Fact]
    public void WrongApplicationIdentityIsRejected()
    {
        var c = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(c); c.Execute("PRAGMA application_id=1");
        AssertFailure(VocabularyFailure.InvalidArchive, () => VocabularySQLiteDatabase.Admit(c));
    }
    [Fact]
    public void VersionOneUpgradeAddsReceiptTableAndRetainsExistingStateAndPayload()
    {
        var c = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(c);
        var source = VocabularySQLiteParityTests.Document().Entries[0] with { Senses = [] };
        var doc = new VocabularyDocument { Entries = [source] }; var row = Assert.Single(VocabularyDatabaseProjection.Rows(doc)).Value;
        byte[] payload = VocabularyPayloadCodec.Encode(row.Payload);
        c.Execute("INSERT INTO entries (id,parent_id,position,payload,lookup_key,language,expression) VALUES (?,NULL,0,?,?,?,?)",
            new Value.Text(row.Id), new Value.Blob(payload), new Value.Text(row.Extra[0]), new Value.Text(row.Extra[1]), new Value.Text(row.Extra[2]));
        c.Execute("UPDATE state SET revision=7"); c.Execute("DROP TABLE cli_receipts"); c.Execute("PRAGMA user_version=1");
        using var db = VocabularySQLiteDatabase.Admit(c);
        Assert.Equal(2, c.Scalar("PRAGMA user_version")); Assert.Equal(7UL, db.Revision()); Assert.Equal(doc, db.Read().Document);
        Assert.Equal(payload, Assert.IsType<Value.Blob>(c.Rows("SELECT payload FROM entries")[0][0]).Data); Assert.Null(db.Receipt("missing"));
        db.Write(doc, doc, 7, new("key", "fingerprint", [])); Assert.Empty(db.Receipt("key")!.Result);
    }
    [Fact]
    public void StartupForeignKeyCheckRejectsAnExistingOrphan()
    {
        var c = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(c); c.Execute("PRAGMA foreign_keys=OFF");
        c.Execute("INSERT INTO usages VALUES ('usage','orphan',0,x'7b')"); c.Execute("PRAGMA foreign_keys=ON");
        AssertFailure(VocabularyFailure.InvalidArchive, () => VocabularySQLiteDatabase.Admit(c));
    }
    [Fact]
    public void LookupKeySwapSucceedsInsideOneTransaction()
    {
        using var db = VocabularySQLiteDatabase.CreateMemory(); var original = VocabularySQLiteParityTests.Document().Entries[0];
        var second = new VocabularyEntry(VocabularySQLiteParityTests.Id(10), "tree", "tree", original.Language, original.Kind, [], original.CreatedAt, original.UpdatedAt);
        var old = new VocabularyDocument { Entries = [original, second] }; db.Write(old, new(), 0);
        var next = old with { Entries = [original with { Expression = "tree", DictionaryForm = "tree" }, second with { Expression = "bank", DictionaryForm = "bank" }] };
        Assert.Equal(2UL, db.Write(next, old, 1)); Assert.Equal(next, db.Read().Document);
    }
    [Fact]
    public void DeferredCommitFailureRollsBackAllRowsAndRevision()
    {
        var c = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(c); using var db = VocabularySQLiteDatabase.Admit(c);
        var old = VocabularySQLiteParityTests.Document(); db.Write(old, new(), 0);
        c.Execute("CREATE TRIGGER inject_orphan AFTER UPDATE ON state BEGIN UPDATE usages SET parent_id='orphan'; END");
        var next = VocabularySQLiteParityTests.Sense(old, sense => sense with { Note = "not committed" });
        AssertFailure(VocabularyFailure.InvalidArchive, () => db.Write(next, old, 1));
        Assert.Equal(old, db.Read().Document); Assert.Equal(1UL, db.Revision());
    }
    [Fact]
    public void InvalidGraphSemanticsRemainAnArchiveAdmissionResponsibility()
    {
        using var db = VocabularySQLiteDatabase.CreateMemory(); var old = VocabularySQLiteParityTests.Document();
        var changed = VocabularySQLiteParityTests.Sense(old, sense => sense with { Examples = [sense.Examples[0] with { Ranges = [new(999, 1)] }] });
        db.Write(changed, new(), 0); var reconstructed = db.Read().Document;
        Assert.Equal(changed, reconstructed); AssertFailure(VocabularyFailure.InvalidArchive, () => VocabularyArchiveValidation.Validate(reconstructed));
    }
    [Fact]
    public void EmptyTextBlobAndEmbeddedNullRoundTripAsTheirOriginalSqlTypes()
    {
        using var c = VocabularySQLiteConnection.OpenMemory(); const string text = "a\0b";
        var values = Assert.Single(c.Rows("SELECT ?,?,?,?", new Value.Text(""), new Value.Text(text), new Value.Blob([]), new Value.Null()));
        Assert.Equal("", Assert.IsType<Value.Text>(values[0]).Data); Assert.Equal(text, Assert.IsType<Value.Text>(values[1]).Data);
        Assert.Empty(Assert.IsType<Value.Blob>(values[2]).Data); Assert.IsType<Value.Null>(values[3]);
    }
    [Fact]
    public void InvalidTextAndUnsupportedSqlTypesDoNotBecomeReplacementCharactersOrCoercions()
    {
        using var c = VocabularySQLiteConnection.OpenMemory();
        AssertFailure(VocabularyFailure.InvalidArchive, () => c.Rows("SELECT CAST(x'ff' AS TEXT)"));
        AssertFailure(VocabularyFailure.InvalidArchive, () => c.Rows("SELECT ?", new Value.Text(new string((char)0xd800, 1))));
        AssertFailure(VocabularyFailure.InvalidArchive, () => c.Rows("SELECT 0.5"));
        AssertFailure(VocabularyFailure.InvalidContent, () => c.Rows("SELECT ?"));
    }
    [Fact]
    public void ByteBudgetBelongsToSqliteBindingRatherThanThePureCodec()
    {
        using var c = VocabularySQLiteConnection.OpenMemory();
        AssertFailure(VocabularyFailure.OversizedArchive, () => c.Rows("SELECT ?", new Value.Blob(new byte[VocabularySQLiteConnection.MaximumValueBytes + 1])));
    }
    [Fact]
    public void ReceiptBytesAreOwnedAndNotMutableByTheCaller()
    {
        using var db = VocabularySQLiteDatabase.CreateMemory(); byte[] original = [1, 2, 3]; var receipt = new VocabularySQLiteReceipt("key", "hash", original);
        original[0] = 9; db.Write(new(), new(), 0, receipt); var first = db.Receipt("key")!.Result; first[0] = 8;
        Assert.Equal(new byte[] { 1, 2, 3 }, db.Receipt("key")!.Result);
    }
    private static void AssertFailure(VocabularyFailure expected, Action action) => Assert.Equal(expected, Assert.Throws<VocabularyException>(action).Failure);
}
