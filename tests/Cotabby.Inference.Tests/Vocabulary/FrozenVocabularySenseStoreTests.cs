using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class FrozenVocabularySenseStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "canonical-sense-" + Guid.NewGuid().ToString("N"));
    private string FileName => Path.Combine(_root, FrozenVocabularyStoreFactory.CanonicalFileName);
    public FrozenVocabularySenseStoreTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    [Fact]
    public async Task AcknowledgedMoveMergeAndUncollectSurviveNamedReopen()
    {
        Guid entry, source, destination, usage; ulong revision;
        await using (var store = FrozenVocabularyStoreFactory.Create(FileName, false))
        {
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]);
            var before = await store.ReadAsync(); entry = before.Document.Catalog[0].Id; source = before.Document.Catalog[0].Senses[0].Id;
            usage = before.Document.CollectedEntries[0].Examples[0].Id;
            var created = await store.CreateSenseAsync(entry); destination = created.SenseId;
            Assert.Equal(before.Revision + 1, created.Snapshot.Revision);
            var moved = await store.MoveExampleAsync(entry, usage, source, destination);
            Assert.Equal(destination, moved.SenseId); Assert.Equal(created.Snapshot.Revision + 1, moved.Snapshot.Revision);
            Assert.Equal(usage, moved.Snapshot.Document.CollectedEntries[0].Senses.Single(value => value.Id == destination).Examples[0].Id);
            var merged = await store.MergeSensesAsync(entry, destination, source);
            Assert.Contains(destination, merged.Document.Catalog[0].Senses[0].MergedFrom);
            await Assert.ThrowsAsync<VocabularyException>(() => store.MergeSensesAsync(entry, destination, source));
            var deleted = await store.RemoveSenseAsync(entry, source); revision = deleted.Revision;
            Assert.Empty(deleted.Document.Collections); Assert.Contains(deleted.Document.Interpretations, value => value.Id == usage);
        }
        await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false);
        var result = await reopened.ReadAsync(); Assert.Equal(revision, result.Revision); Assert.Empty(result.Document.Collections);
        Assert.Contains(result.Document.Interpretations, value => value.Id == usage);
    }
    [Fact]
    public async Task MoveToNewSenseReturnsPersistedDestinationAndInvalidMoveDoesNotCommit()
    {
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false);
        await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]);
        var before = await store.ReadAsync(); var entry = before.Document.Entries[0]; var source = entry.Senses[0].Id; var usage = entry.Examples[0].Id;
        var moved = await store.MoveExampleAsync(entry.Id, usage, source, null);
        Assert.NotEqual(source, moved.SenseId); Assert.Contains(moved.Snapshot.Document.Catalog[0].Senses, value => value.Id == moved.SenseId);
        Assert.Contains(moved.Snapshot.Document.Catalog[0].Senses, value => value.Id == source);
        await Assert.ThrowsAsync<VocabularyException>(() => store.MoveExampleAsync(entry.Id, usage, source, moved.SenseId));
        Assert.Equal(moved.Snapshot, await store.ReadAsync());
    }
    [Fact]
    public async Task ConcurrentMovesOfSameUsageAcknowledgeOnlyOneDestination()
    {
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false);
        await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Example: "bank opens")]);
        var before = await store.ReadAsync(); var entry = before.Document.Entries[0]; var source = entry.Senses[0].Id; var usage = entry.Examples[0].Id;
        async Task<Cotabby.Core.Vocabulary.Frozen.CanonicalSenseReceipt?> Attempt()
        { try { return await store.MoveExampleAsync(entry.Id, usage, source, null); } catch (VocabularyException) { return null; } }
        var results = await Task.WhenAll(Attempt(), Attempt());
        var winner = Assert.Single(results, value => value is not null)!;
        Assert.Equal(before.Revision + 1, winner.Snapshot.Revision); Assert.Equal(winner.Snapshot, await store.ReadAsync());
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
