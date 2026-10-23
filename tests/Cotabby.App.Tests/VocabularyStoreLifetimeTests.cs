using System.Collections.Concurrent;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

public sealed class VocabularyStoreLifetimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private sealed class UiQueue : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue(() => callback(state));
        public void Drain() { while (_queue.TryDequeue(out var next)) next(); }
    }
    private sealed class RetiredUi : SynchronizationContext
    { public override void Post(SendOrPostCallback callback, object? state) => throw new InvalidOperationException("Dispatcher closed"); }
    [Fact]
    public async Task RetiredDispatcherCannotTurnAcknowledgedSaveIntoFailure()
    {
        await using var store = new VocabularyStore(new VocabularyArchive((string?)null), new RetiredUi());
        var capture = VocabularyArchiveParityTests.Capture(); Assert.True(await store.SaveAsync(capture));
        Assert.True(store.Contains(capture)); Assert.False(store.IsPending(capture)); Assert.Null(store.ErrorMessage);
    }
    [Fact]
    public async Task ConcurrentLoadsShareOneOperationAndDoNotOpenUntilRequested()
    {
        using var opened = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int calls = 0;
        var archive = new VocabularyArchive(() =>
        {
            Interlocked.Increment(ref calls); opened.Set(); if (!release.Wait(Timeout)) throw new TimeoutException();
            return VocabularySQLiteDatabase.CreateMemory();
        });
        await using var store = new VocabularyStore(archive, new UiQueue());
        Assert.Equal(0, calls); var first = store.LoadAsync();
        try
        {
            Assert.True(opened.Wait(Timeout)); var second = store.LoadAsync(); Assert.Same(first, second);
            release.Set(); await first.WaitAsync(Timeout); await store.LoadAsync();
            Assert.True(store.IsLoaded); Assert.Equal(1, calls);
        }
        finally { release.Set(); }
    }
    [Fact]
    public async Task FailedLoadCanRetryAndSuccessfulPublicationClearsFixedError()
    {
        int calls = 0;
        var archive = new VocabularyArchive(() => ++calls == 1 ? throw new IOException("private path and contents") : VocabularySQLiteDatabase.CreateMemory());
        await using var store = new VocabularyStore(archive, new UiQueue());
        await store.LoadAsync(); Assert.False(store.IsLoaded); Assert.NotNull(store.ErrorMessage); Assert.DoesNotContain("private", store.ErrorMessage);
        await store.LoadAsync(); Assert.True(store.IsLoaded); Assert.Null(store.ErrorMessage); Assert.Equal(2, calls);
    }
    [Fact]
    public async Task NotificationsRunThroughUiContextAndRetiredQueuedNotificationsAreIgnored()
    {
        var ui = new UiQueue(); var store = new VocabularyStore(new VocabularyArchive((string?)null), ui);
        int events = 0, eventThread = 0; store.Changed += () => { events++; eventThread = Environment.CurrentManagedThreadId; };
        await store.SaveAsync(VocabularyArchiveParityTests.Capture()); Assert.Equal(0, events);
        int uiThread = Environment.CurrentManagedThreadId; ui.Drain(); Assert.True(events > 0); Assert.Equal(uiThread, eventThread);
        await store.SaveAsync(VocabularyArchiveParityTests.Capture()); int before = events;
        await store.DisposeAsync(); ui.Drain(); Assert.Equal(before, events);
    }
    [Fact]
    public async Task ClosingDrainsAnAdmittedSaveAndRefusesNewWork()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var archive = new VocabularyArchive(() =>
        { entered.Set(); if (!release.Wait(Timeout)) throw new TimeoutException(); return VocabularySQLiteDatabase.CreateMemory(); });
        var store = new VocabularyStore(archive, new UiQueue());
        var capture = VocabularyArchiveParityTests.Capture(); var saving = store.SaveAsync(capture);
        try
        {
            Assert.True(entered.Wait(Timeout)); Assert.True(store.IsPending(capture));
            var closing = store.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); Assert.Same(closing, store.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => store.SaveAsync(capture));
            release.Set(); Assert.True(await saving.WaitAsync(Timeout)); await closing.WaitAsync(Timeout);
            Assert.True(store.Contains(capture)); Assert.False(store.IsPending(capture));
            await Assert.ThrowsAsync<VocabularyException>(() => archive.LoadAsync());
        }
        finally { release.Set(); await store.DisposeAsync(); }
    }
    [Fact]
    public async Task CallbackFailureClearsPendingWithoutLeakingForeignErrorText()
    {
        await using var store = new VocabularyStore(new VocabularyArchive((string?)null), new UiQueue());
        var capture = VocabularyArchiveParityTests.Capture(); store.EnrichCapture = _ => throw new IOException("secret text");
        Assert.False(await store.SaveAsync(capture)); Assert.False(store.IsPending(capture)); Assert.DoesNotContain("secret", store.ErrorMessage!);
        store.EnrichCapture = null; Assert.True(await store.SaveAsync(capture)); Assert.Null(store.ErrorMessage);
    }
    [Fact]
    public async Task SavedExplanationRestoresFromActualNotebookWithoutCreatingAnEngine()
    {
        var archive = new VocabularyArchive((string?)null); await archive.CommitAsync(VocabularySQLiteParityTests.Document());
        await using var store = new VocabularyStore(archive, new UiQueue()); int engines = 0;
        await using var service = new LearningExplanationService(new(), LearningLanguage.Chinese, _ => { engines++; throw new InvalidOperationException(); });
        service.LoadPersisted = async (input, language) => { await store.LoadAsync(); return store.Explanation(input, language); };
        service.LookupSaved = store.Explanation;
        var record = VocabularySQLiteParityTests.Document().Entries[0].Senses[0].Explanations[0];
        Assert.Null(service.SavedRecord(record.Input));
        Assert.Equal(record, await service.LoadSavedAsync(record.Input)); Assert.Equal(record, service.SavedRecord(record.Input));
        Assert.Equal(record, await service.GenerateAsync(record.Input, "req_saved")); Assert.Equal(0, engines);
    }
    [Fact]
    public async Task AdmittedSaveNoLongerNeedsExplanationServiceWhileWaitingForStorage()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var archive = new VocabularyArchive(() =>
        { entered.Set(); if (!release.Wait(Timeout)) throw new TimeoutException(); return VocabularySQLiteDatabase.CreateMemory(); });
        await using var store = new VocabularyStore(archive, new UiQueue());
        await using var service = new LearningExplanationService(new(), LearningLanguage.Chinese, _ => throw new InvalidOperationException());
        int calls = 0, caller = Environment.CurrentManagedThreadId;
        store.EnrichCapture = capture =>
        {
            Assert.Equal(caller, Environment.CurrentManagedThreadId); calls++;
            return capture with { Explanation = service.SavedRecord(LearningExplanationPolicy.Input(capture)) };
        };
        var capture = VocabularyArchiveParityTests.Capture(); var saving = store.SaveAsync(capture); Assert.Equal(1, calls);
        try
        {
            Assert.True(entered.Wait(Timeout)); await service.DisposeAsync();
            release.Set(); Assert.True(await saving.WaitAsync(Timeout)); Assert.True(store.Contains(capture)); Assert.Null(store.ErrorMessage);
        }
        finally { release.Set(); }
    }
}
