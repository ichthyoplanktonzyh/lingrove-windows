using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class FrozenVocabularyLazyStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-lazy-store-" + Guid.NewGuid().ToString("N"));
    public FrozenVocabularyLazyStoreTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    [Fact]
    public async Task ConstructionSubscribeAndDisposalBeforeUsePerformNoNotebookIo()
    {
        string directory = Path.Combine(_root, "not-created"); string path = Path.Combine(directory, FrozenVocabularyStoreFactory.CanonicalFileName);
        var store = FrozenVocabularyStoreFactory.Create(path); using var subscription = store.Subscribe(_ => { });
        Assert.False(Directory.Exists(directory)); await store.DisposeAsync(); Assert.False(Directory.Exists(directory));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ReadAsync());
    }
    [Fact]
    public async Task ConcurrentFirstOperationsOpenOnceAndFailureIsRetryable()
    {
        int opens = 0;
        await using var store = new FrozenVocabularyImportStore(() =>
        {
            if (Interlocked.Increment(ref opens) == 1) throw new VocabularyException(VocabularyFailure.StorageBusy);
            return FrozenVocabularySQLiteDatabase.CreateMemory();
        });
        var error = await Assert.ThrowsAsync<VocabularyException>(() => store.ReadAsync()); Assert.Equal(VocabularyFailure.StorageBusy, error.Failure);
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.ReadAsync()));
        Assert.All(snapshots, value => Assert.Equal(0UL, value.Revision)); Assert.Equal(2, opens);
    }
    [Fact]
    public async Task CancelledBeforeFirstOperationDoesNotOpen()
    {
        int opens = 0; await using var store = new FrozenVocabularyImportStore(() => { Interlocked.Increment(ref opens); return FrozenVocabularySQLiteDatabase.CreateMemory(); });
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync(token.Token)); Assert.Equal(0, opens);
    }
    [Fact]
    public async Task DisposalDrainsAnAcceptedOpeningAndRejectsNewAdmission()
    {
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int opens = 0;
        var store = new FrozenVocabularyImportStore(() => { Interlocked.Increment(ref opens); started.Set(); release.Wait(); return FrozenVocabularySQLiteDatabase.CreateMemory(); });
        var accepted = store.ReadAsync(); Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        var disposal = store.DisposeAsync().AsTask();
        try { await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ReadAsync()); Assert.False(disposal.IsCompleted); }
        finally { release.Set(); }
        Assert.Equal(0UL, (await accepted.WaitAsync(TimeSpan.FromSeconds(10))).Revision);
        await disposal.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, opens);
    }
    [Fact]
    public async Task FirstUseCreatesOnlyTheNewCanonicalNamedFile()
    {
        string directory = Path.Combine(_root, "new-notebook"); string path = Path.Combine(directory, FrozenVocabularyStoreFactory.CanonicalFileName);
        await using var store = FrozenVocabularyStoreFactory.Create(path); Assert.False(Directory.Exists(directory));
        Assert.Equal(0UL, (await store.ReadAsync()).Revision); Assert.True(File.Exists(path));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
