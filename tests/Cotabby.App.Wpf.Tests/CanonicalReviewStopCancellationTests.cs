using System.IO;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalReviewStopCancellationTests(WpfDispatcher dispatcher)
{
    private sealed class HeldAdd(IAnkiConnecting inner) : IAnkiConnecting
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Active;
        public async Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken token = default)
        {
            var result = await inner.PerformAsync(action, parameters, connection, token);
            if (action == "addNote") { Active = token; Entered.TrySetResult(); await Release.Task; }
            return result;
        }
    }
    [Fact]
    public Task StopMarksActiveTokenBeforeHeldAddReturns() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "batch-stop-barrier-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var settings = new AppSettings(); var server = new SessionTestServer(); var held = new HeldAdd(server);
            await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, held, new AnkiTestCredentials());
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries.Single(); Guid card = Guid.NewGuid();
            using (var initial = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, entry.Id, entry.Senses[0].Id, card)) Assert.True(await initial.SaveAsync());
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [new VocabularySelection(entry.Id, entry.Senses[0].Id)]);
            var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None); batch.Start(scope); await held.Entered.Task;
            Task stop = batch.StopAsync(); Assert.True(held.Active.IsCancellationRequested); held.Release.TrySetResult(); await stop; if (batch.Work is { } work) await work;
            Assert.Equal(ReviewBatchStatus.Cancelled, batch.Items.Single().Status); Assert.Equal(1, server.AddCount);
            Assert.Null((await store.ReadAsync()).Document.CollectedEntries.Single().Senses[0].ReviewCards!.Single().ActiveBinding!.NoteId);
            batch.Start(scope); if (batch.Work is { } retry) await retry; Assert.Equal(1, server.AddCount); Assert.Equal(ReviewBatchStatus.Synced, batch.Items.Single().Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
