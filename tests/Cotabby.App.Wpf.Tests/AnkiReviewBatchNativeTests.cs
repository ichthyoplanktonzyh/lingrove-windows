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

/// <summary>Compiled app state runs on its actual Windows STA caller. This checks the .NET task
/// reservation/cancellation adapter; portable source traces establish original batch behaviour.</summary>
[Collection("WPF views")]
public sealed class AnkiReviewBatchNativeTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task BusyNotificationPublishesTheWorkHandleBeforeSynchronousStop() => dispatcher.RunAsync(async () =>
    {
        var owner = Dispatcher.CurrentDispatcher;
        await using var store = new VocabularyStore(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory), SynchronizationContext.Current!);
        await store.LoadAsync(); await store.SaveAsync(new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "银行"));
        var e = store.Entries[0]; var values = new AppSettings(); var client = new SessionTestServer();
        var settings = new AnkiReviewSettings(() => values, () => { }, new AnkiTestCredentials());
        var controller = new AnkiReviewController(settings, store, client);
        var batch = new AnkiReviewBatchSession(controller, [new(e.Id, e.Senses[0].Id)]);
        Task? stop = null; bool sawBusy = false, sawHandle = false, onDispatcher = true;
        batch.PropertyChanged += (_, _) =>
        {
            onDispatcher &= owner.CheckAccess();
            if (batch.Busy && !sawBusy) { sawBusy = true; sawHandle = batch.Work is not null; stop = batch.StopAsync(); }
        };
        try
        {
            batch.Start(); var work = batch.Work;
            if (stop is not null) await stop;
            if (work is not null) await work.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(sawBusy); Assert.True(sawHandle); Assert.True(onDispatcher);
            Assert.False(batch.Busy); Assert.Null(batch.Work); Assert.Empty(client.Calls);
            Assert.All(batch.Items, item => Assert.Equal(ReviewBatchStatus.Cancelled, item.Status));
            Assert.Empty(controller.Failures); Assert.Null(controller.SendingSenseId);
        }
        finally { await batch.StopAsync(); if (batch.Work is { } remaining) await remaining.WaitAsync(TimeSpan.FromSeconds(10)); }
    });
}
