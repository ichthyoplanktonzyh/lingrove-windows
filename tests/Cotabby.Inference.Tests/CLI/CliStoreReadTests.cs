using Cotabby.Core.CLI;
using System.Text.Json.Nodes;
using Cotabby.Win32.Vocabulary;
using Xunit;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliStoreReadTests
{
    [Fact]
    public async Task ReadsUseTheExistingStoreAndRejectExpiredRequests()
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var now = CliWire.Now();
        var status = await store.ExecuteCliAsync(new CliRequest { Command = "status", ExpiresAt = now + 60 });
        Assert.Equal(0UL, status["revision"]!.GetValue<ulong>());
        Assert.Equal(0, status["entryCount"]!.GetValue<int>());
        var entries = await store.ExecuteCliAsync(new CliRequest { Command = "resourceList", Resource = "entries", ExpiresAt = now + 60 });
        Assert.Equal(0, entries["total"]!.GetValue<int>());
        var error = await Assert.ThrowsAsync<CliException>(() => store.ExecuteCliAsync(new CliRequest { ExpiresAt = now - 1 }));
        Assert.Equal(CliErrorCode.Timeout, error.Code);
        Assert.Equal(0UL, (await store.ReadAsync()).Revision);
    }

    [Fact]
    public async Task ResourceBatchDryRunCommitAndReplayShareTheStoreTransaction()
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var id = Guid.NewGuid();
        var batch = JsonNode.Parse("""{"version":2,"idempotencyKey":"one","expectedRevision":0,"operations":[{"put":{"resource":"entries","value":{"id":"IDPLACEHOLDER","expression":"bank","kind":"word","language":"en"}}}]}""".Replace("IDPLACEHOLDER", id.ToString("D")))!.AsObject();
        var request = new CliRequest { Command = "apply", ExpiresAt = CliWire.Now() + 60, Batch = batch, DryRun = true };
        var preview = await store.ExecuteCliAsync(request);
        Assert.Equal(0UL, preview["revision"]!.GetValue<ulong>());
        Assert.Empty((await store.ReadAsync()).Document.Catalog);
        var saved = await store.ExecuteCliAsync(request with { DryRun = false });
        Assert.Equal(1UL, saved["revision"]!.GetValue<ulong>());
        Assert.Equal(id, (await store.ReadAsync()).Document.Catalog.Single().Id);
        var replay = await store.ExecuteCliAsync(request with { DryRun = false });
        Assert.True(replay["replayed"]!.GetValue<bool>());
        Assert.Equal(1UL, replay["currentRevision"]!.GetValue<ulong>());
        batch["operations"]![0]!["put"]!["value"]!["expression"] = "other";
        var conflict = await Assert.ThrowsAsync<CliException>(() => store.ExecuteCliAsync(request with { DryRun = false }));
        Assert.Equal(CliErrorCode.Conflict, conflict.Code);
        Assert.Equal(1UL, (await store.ReadAsync()).Revision);
    }

    [Fact]
    public async Task ReceiptSurvivesNamedDatabaseReopen()
    {
        string root = Path.Combine(Path.GetTempPath(), "cli-reopen-" + Guid.NewGuid().ToString("N"));
        VocabularyWindowsFile.CreateParentDirectory(root);
        try
        {
            string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
            var id = Guid.NewGuid();
            var batch = JsonNode.Parse("""{"version":2,"idempotencyKey":"reopen","expectedRevision":0,"operations":[{"put":{"resource":"entries","value":{"id":"IDPLACEHOLDER","expression":"test","kind":"word","language":"en"}}}]}""".Replace("IDPLACEHOLDER", id.ToString("D")))!.AsObject();
            var request = new CliRequest { Command = "apply", ExpiresAt = CliWire.Now() + 60, Batch = batch };
            await using (var store = FrozenVocabularyStoreFactory.Create(path, false))
            {
                var cli = Assert.IsAssignableFrom<Cotabby.Core.Vocabulary.Frozen.ICanonicalVocabularyCliStore>(store);
                Assert.False((await cli.ExecuteCliAsync(request))["replayed"]!.GetValue<bool>());
            }
            await using (var store = FrozenVocabularyStoreFactory.Create(path, false))
            {
                var cli = Assert.IsAssignableFrom<Cotabby.Core.Vocabulary.Frozen.ICanonicalVocabularyCliStore>(store);
                var reply = await cli.ExecuteCliAsync(request);
                Assert.True(reply["replayed"]!.GetValue<bool>());
                Assert.Equal(1UL, (await store.ReadAsync()).Revision);
                Assert.Equal(id, (await store.ReadAsync()).Document.Catalog.Single().Id);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task TraditionalWriteUsesSameStoreAndReplaysAcknowledgement()
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var input = JsonNode.Parse("""{"version":1,"idempotencyKey":"traditional","operations":[{"add":{"expression":"bank","language":"en","kind":"word","meaning":"money","examples":[{"text":"bank opens","completeness":"unknown"}]}}]}""")!.AsObject();
        var request = new CliRequest { Command = "apply", ExpiresAt = CliWire.Now() + 60, Batch = input };
        var reply = await store.ExecuteCliAsync(request); Assert.Equal(1UL, reply["revision"]!.GetValue<ulong>());
        var snapshot = await store.ReadAsync(); Assert.Equal("bank", snapshot.Document.CollectedEntries.Single().Expression);
        Assert.Equal(Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness.Unknown, snapshot.Document.Interpretations.Single().Completeness);
        Assert.True((await store.ExecuteCliAsync(request))["replayed"]!.GetValue<bool>());
        Assert.Equal(1UL, (await store.ReadAsync()).Revision);
    }
}
