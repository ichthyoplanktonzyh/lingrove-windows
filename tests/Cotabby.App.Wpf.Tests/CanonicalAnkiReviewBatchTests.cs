using System.IO;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Review;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalAnkiReviewBatchTests(WpfDispatcher dispatcher)
{
    private sealed class Preferences : IVocabularyNotebookListPreferences { public string Density { get; set; } = "compact"; }
    private static async Task<(VocabularySelection Selection, Guid Card)> Add(F.ICanonicalVocabularyStore store, AnkiReviewFeature feature, string expression)
    {
        await store.AddEntriesAsync([new VocabularyEntryInput(Expression: expression, Meaning: "meaning")]);
        var entry = (await store.ReadAsync()).Document.CollectedEntries.Single(e => e.Expression == expression); var card = Guid.NewGuid();
        var session = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, entry.Id, entry.Senses[0].Id, card); Assert.True(await session.SaveAsync()); session.Dispose();
        return (new(entry.Id, entry.Senses[0].Id), card);
    }
    private static async Task Finish(CanonicalAnkiReviewBatchSession batch) { if (batch.Work is { } work) await work; }
    [Fact]
    public Task FrozenCardsSerialFailureAndSingleRetryPreserveAcknowledgements() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer { FailAdd = true }; await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var first = await Add(store, feature, "first"); var second = await Add(store, feature, "second");
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "empty", Meaning: "nothing")]); var empty = (await store.ReadAsync()).Document.CollectedEntries.Single(e => e.Expression == "empty");
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [first.Selection, second.Selection, first.Selection, new(empty.Id, empty.Senses[0].Id)]);
            Assert.Equal(1, batch.EmptySenseCount); Assert.Equal(new[] { first.Card, second.Card }, batch.Items.Select(i => i.CardId!.Value));
            var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None); batch.Start(scope); await Finish(batch);
            Assert.Equal(ReviewBatchStatus.Failed, batch.Items[0].Status); Assert.Equal(ReviewBatchStatus.Synced, batch.Items[1].Status); Assert.Equal(1, server.AddCount);
            batch.Start(scope, batch.Items[0].Id); await Finish(batch); Assert.Equal(2, batch.CompleteCount); Assert.Equal(2, server.AddCount);
            batch.Start(scope); await Finish(batch); Assert.Equal(2, server.AddCount);
            var document = (await store.ReadAsync()).Document.CollectedEntries; Assert.All(document.Where(e => e.Expression != "empty"), e => Assert.NotNull(e.Senses[0].ReviewCards!.Single().ActiveBinding!.NoteId));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task AudioRowNeedsPreparationWithoutRemoteWrites() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "audio"); var snapshot = await store.ReadAsync(); var entry = snapshot.Document.CollectedEntries.Single(); var baseline = entry.Senses[0].ReviewCards!.Single();
            var layout = baseline.Plan.Layout!; var updated = baseline with { Plan = baseline.Plan with { Layout = layout with { Front = layout.Front.Append(ReviewCardPart.Create(ReviewContentSource.ExpressionAudio)).ToArray() }, AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } } };
            await store.SaveReviewAsync(entry.Id, entry.Senses[0].Id, updated, baseline, item.Card);
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]); var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None); batch.Start(scope); await Finish(batch);
            Assert.Equal(ReviewBatchStatus.Attention, batch.Items.Single().Status); Assert.Contains("Speech", batch.Items.Single().Message); Assert.Equal(0, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task BatchWindowShowsDestinationAndExplicitSend() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]);
            var window = new CanonicalAnkiReviewBatchWindow(batch, feature, () => { }); window.Show(); if (window.LastOperation is { } load) await load;
            Assert.True(window.SendButton.IsEnabled); VocabularyThemeRender.Capture(window, "canonical-batch-ready", 760, 740); Assert.Equal(0, server.AddCount); window.SendButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (window.LastOperation is { } start) await start; await Finish(batch); Assert.Equal(1, batch.CompleteCount); Assert.Equal(1, server.AddCount); await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task StopAfterRemoteAddKeepsPendingIdentityAndRetryDoesNotDuplicate() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new SessionTestServer { AfterAdd = async () => { entered.TrySetResult(); await release.Task; } }; await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]); var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None);
            batch.Start(scope); await entered.Task; Task stop = batch.StopAsync(); Assert.False(batch.Work!.IsCompleted); release.SetResult(); await stop; await Finish(batch);
            Assert.Equal(ReviewBatchStatus.Cancelled, batch.Items.Single().Status); Assert.Equal(1, server.AddCount);
            var pending = (await store.ReadAsync()).Document.CollectedEntries.Single().Senses[0].ReviewCards!.Single().ActiveBinding!; Assert.Null(pending.NoteId);
            batch.Start(scope, batch.Items.Single().Id); await Finish(batch); Assert.Equal(ReviewBatchStatus.Synced, batch.Items.Single().Status); Assert.Equal(1, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task StopAfterVerificationStillPublishesReceipt() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]); var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None);
            Task? stopping = null; server.AfterNote = () => stopping = batch.StopAsync(); batch.Start(scope); await Finish(batch); if (stopping is not null) await stopping;
            Assert.Equal(ReviewBatchStatus.Synced, batch.Items.Single().Status); Assert.NotNull((await store.ReadAsync()).Document.CollectedEntries.Single().Senses[0].ReviewCards!.Single().ActiveBinding!.NoteId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task BatchApplyPreservesSentDeckAndChangesOnlyUnboundPlan() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var first = await Add(store, feature, "first"); var second = await Add(store, feature, "second");
            var one = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, first.Selection.EntryId, first.Selection.SenseId, first.Card); Assert.True(await one.SendAsync()); one.Dispose();
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [first.Selection, second.Selection]); batch.StartApply("Other"); await Finish(batch);
            var entries = (await store.ReadAsync()).Document.CollectedEntries; Assert.Equal("Default", entries.Single(e => e.Id == first.Selection.EntryId).Senses[0].ReviewCards!.Single().Plan.Deck);
            Assert.Equal("Other", entries.Single(e => e.Id == second.Selection.EntryId).Senses[0].ReviewCards!.Single().Plan.Deck); Assert.Equal(1, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task PreparationSavesOnlySelectedCardAndRefreshesFrozenRow() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]);
            var window = new CanonicalAnkiReviewBatchWindow(batch, feature, () => { }); window.Show(); if (window.LastOperation is { } load) await load;
            var outer = (StackPanel)((Border)window.Rows.Children[0]).Child; var actions = outer.Children.OfType<WrapPanel>().Single();
            actions.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 200 && window.Preparation is null; i++) await Task.Delay(10); var child = Assert.IsType<CanonicalAnkiReviewWindow>(window.Preparation);
            if (child.LastOperation is { } pending) await pending; await child.PreviewOperation; child.CardNameBox.Text = "Changed card"; await child.PreviewOperation;
            child.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (child.LastOperation is { } save) await save; await child.RetireAsync(); if (window.LastOperation is { } refresh) await refresh;
            Assert.Equal("Changed card", batch.Items.Single().Meaning); Assert.Equal(ReviewBatchStatus.Ready, batch.Items.Single().Status); Assert.Equal(item.Card, batch.Items.Single().CardId); Assert.Equal(0, server.AddCount); await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task ChangedProfileIsNotSentWithoutReload() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]);
            var window = new CanonicalAnkiReviewBatchWindow(batch, feature, () => { }); window.Show(); if (window.LastOperation is { } load) await load;
            server.Profile = "Other profile"; window.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (window.LastOperation is { } start) await start;
            Assert.Equal(0, server.AddCount); Assert.Contains("profile changed", window.MessageText.Text); Assert.False(window.SendButton.IsEnabled); await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ClosingBatchJoinsInFlightSend() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new SessionTestServer { AfterAdd = async () => { entered.TrySetResult(); await release.Task; } }; await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]);
            var window = new CanonicalAnkiReviewBatchWindow(batch, feature, () => { }); window.Show(); if (window.LastOperation is { } load) await load;
            window.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await entered.Task; Task retire = window.RetireAsync(); Assert.False(retire.IsCompleted); Assert.True(window.IsVisible);
            release.SetResult(); await retire; Assert.False(window.IsVisible); Assert.Equal(1, server.AddCount); Assert.NotNull((await store.ReadAsync()).Document.CollectedEntries.Single().Senses[0].ReviewCards!.Single().ActiveBinding);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task OneSenseExpandsEveryCardAndUnchangedPreparationPreservesFailure() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer { FailAdd = true }; await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var otherId = Guid.NewGuid(); var second = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, item.Selection.EntryId, item.Selection.SenseId, otherId); Assert.True(await second.SaveAsync()); second.Dispose();
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection, item.Selection]); Assert.Equal(new[] { item.Card, otherId }, batch.Items.Select(row => row.CardId!.Value));
            var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None); batch.Start(scope); await Finish(batch); var failed = batch.Items.First(row => row.Status == ReviewBatchStatus.Failed);
            var preparation = await batch.PrepareAsync(failed.Id); Assert.NotNull(preparation); preparation.Dispose(); await batch.RefreshAsync(failed.Id);
            Assert.Equal(ReviewBatchStatus.Failed, batch.Items.First(row => row.Id == failed.Id).Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task NotebookBatchEntryBorrowsFeatureAndDrainsOnOwnerClose() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var first = await Add(store, feature, "first"); var second = await Add(store, feature, "second");
            var controller = new FrozenVocabularyWindowController(store, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
            controller.AttachReviewFeature(feature, () => { }); controller.Show(first.Selection); var notebook = controller.Window!; if (notebook.LastOperation is { } load) await load;
            notebook.OpenReviewBatch([first.Selection, second.Selection]); for (int i = 0; i < 200 && notebook.ReviewBatchWindow is null; i++) await Task.Delay(10);
            var window = Assert.IsType<CanonicalAnkiReviewBatchWindow>(notebook.ReviewBatchWindow); if (window.LastOperation is { } metadata) await metadata;
            Assert.Equal(2, window.Batch.Items.Count); window.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (window.LastOperation is { } started) await started; await Finish(window.Batch);
            Assert.Equal(2, server.AddCount); Assert.Equal(2, window.Batch.CompleteCount); await controller.DisposeAsync(); Assert.False(window.IsVisible); Assert.False(notebook.IsVisible);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task FrozenSelectionFollowsMergedSenseAndKeepsIndependentCardIds() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var first = await Add(store, feature, "bank"); var created = await store.CreateSenseAsync(first.Selection.EntryId);
            var other = new VocabularySelection(first.Selection.EntryId, created.SenseId); var otherId = Guid.NewGuid();
            var second = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, other.EntryId, other.SenseId, otherId); Assert.True(await second.SaveAsync()); second.Dispose();
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [first.Selection, other]); await store.MergeSensesAsync(first.Selection.EntryId, first.Selection.SenseId, other.SenseId);
            var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None); batch.Start(scope); await Finish(batch);
            Assert.Equal(2, batch.CompleteCount); Assert.Equal(2, server.AddCount); var cards = (await store.ReadAsync()).Document.CollectedEntries.Single().Senses.Single().ReviewCards!;
            Assert.Equal(new[] { first.Card, otherId }.Order(), cards.Select(c => c.Plan.Id).Order()); Assert.All(cards, card => Assert.NotNull(card.ActiveBinding!.NoteId));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task MissingNoteNeedsExplicitPreparationBeforeRecreate() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var original = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, item.Selection.EntryId, item.Selection.SenseId, item.Card); Assert.True(await original.SendAsync()); original.Dispose();
            var card = (await store.ReadAsync()).Document.CollectedEntries.Single().Senses[0].ReviewCards!.Single(); server.Delete(card.ActiveBinding!.NoteId!.Value);
            await store.SaveReviewAsync(item.Selection.EntryId, item.Selection.SenseId, card with { Plan = card.Plan with { Name = "Changed", Layout = card.Plan.Layout! with { Front = [ReviewCardPart.Create(ReviewContentSource.Meaning)] } } }, card, item.Card);
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]); var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None); batch.Start(scope); await Finish(batch);
            Assert.Equal(ReviewBatchStatus.Failed, batch.Items.Single().Status); Assert.Equal(1, server.AddCount);
            var session = await batch.PrepareAsync(batch.Items.Single().Id); Assert.NotNull(session); Assert.False(await session.SendAsync()); Assert.True(session.MissingNote); Assert.True(await session.SendAsync(recreateMissing: true)); session.Dispose();
            await batch.RefreshAsync(batch.Items.Single().Id); Assert.Equal(ReviewBatchStatus.Synced, batch.Items.Single().Status); Assert.Equal(2, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task ConflictIsResolvedOnlyByExplicitPreparationChoice() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var original = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, item.Selection.EntryId, item.Selection.SenseId, item.Card); Assert.True(await original.SendAsync()); original.Dispose();
            var card = (await store.ReadAsync()).Document.CollectedEntries.Single().Senses[0].ReviewCards!.Single(); server.Edit(card.ActiveBinding!.NoteId!.Value, "Front", "remote edit");
            await store.SaveReviewAsync(item.Selection.EntryId, item.Selection.SenseId, card with { Plan = card.Plan with { Layout = card.Plan.Layout! with { Front = [ReviewCardPart.Create(ReviewContentSource.Meaning)] } } }, card, item.Card);
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]); var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None); batch.Start(scope); await Finish(batch);
            Assert.Equal(ReviewBatchStatus.Attention, batch.Items.Single().Status); Assert.Equal(1, server.AddCount);
            var session = await batch.PrepareAsync(batch.Items.Single().Id); Assert.NotNull(session); Assert.False(await session.SendAsync()); Assert.Single(session.Conflicts);
            session.SetKeepRemote(session.Conflicts.Single().Id, true); Assert.True(await session.SendAsync()); session.Dispose(); await batch.RefreshAsync(batch.Items.Single().Id);
            Assert.Equal(ReviewBatchStatus.Synced, batch.Items.Single().Status); Assert.Equal(1, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task SynchronousStopFromBusyNotificationSeesReservedWork() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]); var scope = await feature.Controller.CanonicalScopeAsync(CancellationToken.None);
            Task? stop = null; bool reserved = false; batch.PropertyChanged += (_, _) => { if (batch.Busy && stop is null) { reserved = batch.Work is not null; stop = batch.StopAsync(); } };
            batch.Start(scope); await Finish(batch); if (stop is not null) await stop;
            Assert.True(reserved); Assert.Equal(ReviewBatchStatus.Cancelled, batch.Items.Single().Status); Assert.Equal(0, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task MissingNoteInBatchRequiresUserToPrepareThenReadd() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            var values = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => values, () => { }, null, server, new AnkiTestCredentials());
            var item = await Add(store, feature, "bank"); var original = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, item.Selection.EntryId, item.Selection.SenseId, item.Card); Assert.True(await original.SendAsync()); original.Dispose();
            var card = (await store.ReadAsync()).Document.CollectedEntries.Single().Senses[0].ReviewCards!.Single(); server.Delete(card.ActiveBinding!.NoteId!.Value);
            await store.SaveReviewAsync(item.Selection.EntryId, item.Selection.SenseId, card with { Plan = card.Plan with { Layout = card.Plan.Layout! with { Front = [ReviewCardPart.Create(ReviewContentSource.Meaning)] } } }, card, item.Card);
            var batch = await CanonicalAnkiReviewBatchSession.OpenAsync(feature, store, [item.Selection]); var window = new CanonicalAnkiReviewBatchWindow(batch, feature, () => { }); window.Show(); if (window.LastOperation is { } load) await load;
            window.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (window.LastOperation is { } start) await start; await Finish(batch); Assert.Equal(ReviewBatchStatus.Failed, batch.Items.Single().Status); Assert.Equal(1, server.AddCount);
            var panel = (StackPanel)((Border)window.Rows.Children[0]).Child; var actions = panel.Children.OfType<WrapPanel>().Single(); actions.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 200 && window.Preparation is null; i++) await Task.Delay(10); var child = Assert.IsType<CanonicalAnkiReviewWindow>(window.Preparation); if (child.LastOperation is { } loading) await loading;
            child.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (child.LastOperation is { } check) await check; Assert.Equal("Re-add to Anki", child.SendButton.Content); Assert.Equal(1, server.AddCount);
            child.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (child.LastOperation is { } send) await send; await child.RetireAsync(); if (window.LastOperation is { } refresh) await refresh;
            Assert.Equal(2, server.AddCount); Assert.Equal(ReviewBatchStatus.Synced, batch.Items.Single().Status); await window.RetireAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

}
