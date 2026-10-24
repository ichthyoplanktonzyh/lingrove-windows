using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual Windows named SQLite owners and the real STA caller complement portable
/// source traces. Only Anki is synthetic: there is no external server, model or editor. Reopening
/// creates a fresh archive/Store; cleanup waits for both lifetimes to drain before deleting files.</summary>
[Collection("WPF views")]
public sealed class AnkiReviewSessionNativeTests(WpfDispatcher dispatcher)
{
    private static AnkiReviewSettings Settings()
    { var values = new AppSettings(); return new(() => values, () => { }, new AnkiTestCredentials()); }
    private static AnkiReviewSession Session(AnkiReviewController controller)
    { var e = controller.Store.Entries[0]; return new(controller, e, e.Senses[0]); }
    private static async Task Seed(VocabularyStore store)
    {
        await store.LoadAsync();
        await store.SaveAsync(new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "银行"));
        var entry = store.Entries[0];
        Assert.NotNull(await store.CreateSenseAsync(entry.Id));
    }
    [Fact]
    public Task LostCommittedReplyRecoversAfterNamedArchiveReopenAndSenseMerge() => dispatcher.RunAsync(async () =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "lingrove-review-session-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "notebook.sqlite"); var server = new SessionTestServer { LoseAdd = true }; var settings = Settings();
        Guid entryId, sourceId, targetId, pendingId;
        try
        {
            await using (var store = new VocabularyStore(new VocabularyArchive(path), SynchronizationContext.Current!))
            {
                await Seed(store); var entry = store.Entries[0]; entryId = entry.Id; sourceId = entry.Senses[0].Id; targetId = entry.Senses[1].Id;
                var controller = new AnkiReviewController(settings, store, server); var session = Session(controller);
                Assert.False(await session.SendAsync()); Assert.Equal(1, server.AddCount);
                var pending = store.Entries[0].Senses[0].Review!.ActiveBinding!; pendingId = pending.Id;
                Assert.Null(pending.NoteId); session.Deck = "Other"; Assert.True(await session.SaveAsync());
            }
            await using (var store = new VocabularyStore(new VocabularyArchive(path), SynchronizationContext.Current!))
            {
                await store.LoadAsync(); var restored = store.Entries[0].Senses[0].Review!;
                Assert.Equal(pendingId, restored.ActiveBindingId); Assert.Equal("Other", restored.ActiveBinding!.Deck);
                Assert.Equal("Other", restored.Plan.Deck); Assert.True(await store.MergeSensesAsync(entryId, sourceId, targetId));
                var controller = new AnkiReviewController(settings, store, server); Assert.True(await Session(controller).SendAsync());
                var receipt = store.Entries[0].Senses[0].Review!.ActiveBinding!;
                Assert.Equal(pendingId, receipt.Id); Assert.Equal(sourceId, receipt.OriginSenseId); Assert.NotNull(receipt.NoteId); Assert.Equal(1, server.AddCount);
            }
            await using var final = new VocabularyArchive(path);
            var disk = (await final.LoadAsync()).Document.Entries[0].Senses[0].Review!.ActiveBinding!;
            Assert.Equal(pendingId, disk.Id); Assert.Equal(sourceId, disk.OriginSenseId); Assert.NotNull(disk.NoteId);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });
    [Fact]
    public Task HeldRemoteReplyRetainsTheDispatcherAndTheControllerLease() => dispatcher.RunAsync(async () =>
    {
        var owner = Dispatcher.CurrentDispatcher;
        await using var store = new VocabularyStore(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory), SynchronizationContext.Current!);
        await Seed(store); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new SessionTestServer { AfterAdd = async () => { entered.TrySetResult(); await release.Task; } };
        var controller = new AnkiReviewController(Settings(), store, server); var first = Session(controller); var second = Session(controller);
        int notifications = 0; first.PropertyChanged += (_, _) => { Assert.True(owner.CheckAccess()); notifications++; };
        controller.PropertyChanged += (_, _) => Assert.True(owner.CheckAccess());
        Task<bool> send = first.SendAsync(); Exception? assertion = null; bool sent = false;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(first.Busy); Assert.Equal(first.SenseId, controller.SendingSenseId);
            Assert.False(await second.SendAsync()); Assert.Equal("Wait for the current Anki operation to finish.", second.Message);
            Assert.Equal(1, server.AddCount); Assert.False(send.IsCompleted);
        }
        catch (Exception error) { assertion = error; }
        finally
        {
            // Join the accepted send even when an assertion fails. Releasing the remote gate
            // alone would let Store/dispatcher teardown race the still-running transaction.
            await Task.Run(() => release.TrySetResult());
            try { sent = await send.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception drainError) { if (assertion is not null) throw new AggregateException(assertion, drainError); throw; }
        }
        if (assertion is not null) ExceptionDispatchInfo.Capture(assertion).Throw();
        Assert.True(sent); Assert.False(first.Busy); Assert.Null(controller.SendingSenseId); Assert.True(notifications > 0);
    });
}
