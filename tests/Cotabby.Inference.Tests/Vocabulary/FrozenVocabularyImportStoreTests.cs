using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.Vocabulary;
public sealed class FrozenVocabularyImportStoreTests
{
    private static FrozenVocabularyImportStore Create() => new(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(814759445.12345672));
    [Fact]
    public async Task PreviewIsReplannedAndNoOpKeepsRevisionAndSelection()
    {
        await using var store = Create();
        var table = new VocabularyWordList(VocabularyWordListFormat.Text, [new(1, ["hello"])]);
        var configuration = new VocabularyWordListConfiguration(false, new Dictionary<VocabularyImportField, int> { [VocabularyImportField.Expression] = 0 }, LearningLanguage.English);
        var preview = await store.PreviewWordListAsync(table, configuration); Assert.Equal(0UL, preview.Revision);
        var first = await store.AddEntriesAsync(preview.Inputs); Assert.True(first.HasChanges);
        var repeated = await store.AddEntriesAsync(preview.Inputs); Assert.False(repeated.HasChanges);
        Assert.Equal(first.Selection, repeated.Selection); Assert.Equal(1UL, (await store.ReadAsync()).Revision);
    }
    [Fact]
    public async Task ConcurrentAddsPublishAcknowledgedSnapshotsAndDrain()
    {
        var store = Create(); var seen = new TaskCompletionSource<FrozenVocabularyImportSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = store.Subscribe(snapshot => { if (snapshot.Revision == 2) seen.TrySetResult(snapshot); });
        var first = store.AddEntriesAsync([new(Expression: "one")]);
        var second = store.AddEntriesAsync([new(Expression: "two")]);
        var results = await Task.WhenAll(first, second); Assert.All(results, result => Assert.True(result.HasChanges));
        var notified = await seen.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(2, notified.Document.Catalog.Count);
        Assert.Equal(2UL, (await store.ReadAsync()).Revision); await store.DisposeAsync();
    }
    [Fact]
    public async Task CancelledRequestDoesNotCommitOrPublish()
    {
        await using var store = Create(); int calls = 0; using var subscription = store.Subscribe(_ => Interlocked.Increment(ref calls));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AddEntriesAsync([new(Expression: "one")], cancellation.Token));
        Assert.Equal(0UL, (await store.ReadAsync()).Revision); Assert.Equal(0, calls);
    }
    [Fact]
    public async Task FailedReplanDoesNotPublishOrChangeRevision()
    {
        await using var store = Create();
        var input = new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there");
        await store.AddEntriesAsync([input]);
        await Assert.ThrowsAsync<VocabularyWordListException>(() => store.AddEntriesAsync([input with { Meaning = "other" }]));
        var snapshot = await store.ReadAsync(); Assert.Equal(1UL, snapshot.Revision);
        Assert.Single(snapshot.Document.Interpretations); Assert.Equal("greeting", snapshot.Document.Catalog[0].Senses[0].Meaning);
    }
    [Fact]
    public async Task DisposeWaitsForQueuedObserverAndRejectsNewWork()
    {
        var store = Create(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var subscription = store.Subscribe(_ => { started.TrySetResult(); release.Wait(); });
        await store.AddEntriesAsync([new(Expression: "one")]); await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var disposal = store.DisposeAsync().AsTask();
        try { Assert.False(disposal.IsCompleted); }
        finally { release.Set(); }
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ReadAsync());
    }

}
