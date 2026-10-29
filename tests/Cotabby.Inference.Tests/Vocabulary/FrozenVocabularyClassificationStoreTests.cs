using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Resources;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class FrozenVocabularyClassificationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-classification-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_root, FrozenVocabularyStoreFactory.CanonicalFileName);
    public FrozenVocabularyClassificationStoreTests() => VocabularyWindowsFile.CreateParentDirectory(_root);

    [Fact]
    public async Task DefaultOwnerInstallsOncePublishesAndReopensWithoutAnotherRevision()
    {
        var published = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var store = FrozenVocabularyStoreFactory.Create(PathName))
        {
            using var subscription = store.Subscribe(snapshot => published.TrySetResult(snapshot.Revision));
            Assert.False(File.Exists(PathName));
            var first = await store.ReadAsync();
            Assert.Equal  (1UL, first.Revision);
            Assert.NotEmpty(first.Document.Classification.Frameworks);
            Assert.Equal(1UL, await published.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1UL, (await store.ReadAsync()).Revision);
        }
        await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(PathName);
        Assert.Equal(1UL, (await reopened.ReadAsync()).Revision);
    }

    [Fact]
    public async Task DisabledNeverLoadsCatalog()
    {
        int loads = 0;
        await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.CreateMemory(),
            installBundledClassification: false, loadCatalog: () => { loads++; throw new InvalidOperationException(); });
        Assert.Equal(0UL, (await store.ReadAsync()).Revision);
        Assert.Equal(0, loads);
    }

    [Fact]
    public async Task FailedLoadRetriesAndConcurrentReadsShareOneInstallation()
    {
        int loads = 0;
        await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.CreateMemory(),
            installBundledClassification: true, loadCatalog: () =>
            {
                if (Interlocked.Increment(ref loads) == 1) throw new InvalidOperationException("injected catalog failure");
                return VocabularyClassificationCatalog.Bundled();
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync());
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.ReadAsync()));
        Assert.All(snapshots, snapshot => Assert.Equal(1UL, snapshot.Revision));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task InvalidCatalogIsNotCachedAndCanBeReplaced()
    {
        var valid = VocabularyClassificationCatalog.Bundled();
        var categories = valid.Categories.ToArray();
        categories[0] = categories[0] with { Parents = ["missing-parent"] };
        var invalid = valid with { Categories = categories };
        var database = FrozenVocabularySQLiteDatabase.CreateMemory();
        int loads = 0; int notifications = 0;
        await using var store = new FrozenVocabularyImportStore(() => database, installBundledClassification: true,
            loadCatalog: () => ++loads == 1 ? invalid : valid);
        using var subscription = store.Subscribe(_ => Interlocked.Increment(ref notifications));
        await Assert.ThrowsAsync<VocabularyException>(() => store.ReadAsync());
        Assert.Equal(0UL, database.Revision());
        Assert.Equal(0, notifications);
        Assert.Equal(1UL, (await store.ReadAsync()).Revision);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task FailedCommitRollsBackAndRetriesWithSuccessfullyLoadedCatalog()
    {
        var database = FrozenVocabularySQLiteDatabase.OpenFile(PathName);
        using var connection = VocabularySQLiteConnection.OpenNamed(PathName);
        connection.Execute("CREATE TRIGGER reject_classification BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
        int loads = 0; int notifications = 0;
        await using var store = new FrozenVocabularyImportStore(() => database, installBundledClassification: true,
            loadCatalog: () => { loads++; return VocabularyClassificationCatalog.Bundled(); });
        using var subscription = store.Subscribe(_ => Interlocked.Increment(ref notifications));
        await Assert.ThrowsAnyAsync<VocabularyException>(() => store.ReadAsync());
        Assert.Equal(0UL, database.Revision());
        Assert.Empty(database.Read().Document.Classification.Frameworks);
        Assert.Equal(0, notifications);
        connection.Execute("DROP TRIGGER reject_classification");
        Assert.Equal(1UL, (await store.ReadAsync()).Revision);
        Assert.Equal(1, loads);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
