using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyReviewSectionTests(WpfDispatcher dispatcher)
{
    private sealed class Preferences : IVocabularyNotebookListPreferences { public string Density { get; set; } = "compact"; }
    private sealed class NavigationAnki(SessionTestServer inner) : IAnkiConnecting
    {
        internal bool HoldNote;
        internal string? HoldAction;
        internal int Browses, Reviews;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken token = default)
        {
            if (action == HoldAction || action == "notesInfo" && HoldNote) { HoldNote = false; HoldAction = null; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            if (action == "guiBrowse") { Browses++; return new AnkiValue.Sequence([]); }
            if (action == "findCards") return new AnkiValue.Sequence([new AnkiValue.Number(42)]);
            if (action == "cardsInfo") return new AnkiValue.Sequence([new AnkiValue.Map(new Dictionary<string, AnkiValue> { ["deckName"] = new AnkiValue.Text("Default") })]);
            if (action == "guiDeckOverview") { Reviews++; return new AnkiValue.Boolean(true); }
            return await inner.PerformAsync(action, parameters, connection, token);
        }
    }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var node in Nodes(VisualTreeHelper.GetChild(root, i))) yield return node; }
    private static Button Button(Window owner, string text) { owner.UpdateLayout(); return Nodes(owner).OfType<Button>().Single(b => Equals(b.Content, text)); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static async Task Wait(FrozenVocabularyReviewSection section) { if (section.LastOperation is { } pending) await pending; }
    private static async Task<Cotabby.App.UI.Review.CanonicalAnkiReviewWindow> Opened(FrozenVocabularyReviewSection section)
    { for (int i = 0; i < 200 && section.CardWindow is null; i++) await Task.Delay(10); return Assert.IsType<Cotabby.App.UI.Review.CanonicalAnkiReviewWindow>(section.CardWindow); }
    private static async Task Ready(Cotabby.App.UI.Review.CanonicalAnkiReviewWindow window) { if (window.LastOperation is { } pending) await pending; await window.PreviewOperation; }
    [Fact]
    public Task SectionCreatesEditsAndRemovesIndependentCardThroughSharedOwner() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-section-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, server, new AnkiTestCredentials());
            var owner = new Window { Width = 700, Height = 500 }; owner.Show(); bool confirmed = false; bool allowed = true;
            var section = new FrozenVocabularyReviewSection(owner, store, feature, () => { }, () => { }, _ => confirmed, (_, _) => allowed);
            owner.Content = section.Render(entry, entry.Senses[0]); var staleAdd = Button(owner, "Add card"); allowed = false; Click(staleAdd); await Wait(section); Assert.Null(section.CardWindow);
            allowed = true; Click(staleAdd); var cardWindow = await Opened(section); await Ready(cardWindow);
            cardWindow.CardNameBox.Text = "Stable card"; await cardWindow.PreviewOperation; Click(cardWindow.SaveButton); await Ready(cardWindow); await cardWindow.RetireAsync();
            entry = (await store.ReadAsync()).Document.CollectedEntries[0]; var card = Assert.Single(entry.Senses[0].ReviewCards!); Assert.Null(entry.Senses[0].Review);
            owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "Edit")); var edited = await Opened(section); await Ready(edited); Assert.Equal(card.Plan.Id, edited.CardId); await edited.RetireAsync();
            owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "Remove local card")); await Wait(section); Assert.Single((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!);
            confirmed = true; Click(Button(owner, "Remove local card")); await Wait(section); Assert.Empty((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards!);
            await section.RetireAsync(); owner.Close(); Assert.Equal(0, server.AddCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task NavigationUsesCanonicalBindingAndRejectsStaleSelection() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-section-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var backend = new SessionTestServer(); var client = new NavigationAnki(backend); await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, client, new AnkiTestCredentials());
            var draft = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, entry.Id, entry.Senses[0].Id, Guid.NewGuid());
            await feature.RunAsync(async token => Assert.True(await draft.SendAsync(cancellationToken: token))); draft.Dispose(); entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var owner = new Window { Width = 700, Height = 500 }; owner.Show(); bool allowed = true;
            var section = new FrozenVocabularyReviewSection(owner, store, feature, () => { }, () => { }, canAct: (_, _) => allowed); owner.Content = section.Render(entry, entry.Senses[0]);
            client.HoldNote = true; Click(Button(owner, "View in Anki")); await client.Entered.Task; allowed = false; client.Release.TrySetResult(); await Wait(section); Assert.Equal(0, client.Browses);
            allowed = true; owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "View in Anki")); await Wait(section); Assert.Equal(1, client.Browses);
            owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "Review in Anki")); await Wait(section); Assert.Equal(1, client.Reviews);
            backend.Profile = "Different"; owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "View in Anki")); await Wait(section); Assert.Equal(1, client.Browses);
            await section.RetireAsync(); owner.Close();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task NotebookControllerAttachesOneFeatureAndDrainsOpenCard() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-notebook-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, server, new AnkiTestCredentials());
            var controller = new FrozenVocabularyWindowController(store, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
            controller.AttachReviewFeature(feature, () => { }); controller.Show(new VocabularySelection(entry.Id, entry.Senses[0].Id));
            var notebook = controller.Window!; if (notebook.LastOperation is { } load) await load; await notebook.Session.RevealSelectionAsync(new VocabularySelection(entry.Id, entry.Senses[0].Id));
            var section = Assert.IsType<FrozenVocabularyReviewSection>(notebook.ReviewSection); Click(Button(notebook, "Add card")); var child = await Opened(section); await Ready(child);
            await controller.DisposeAsync(); Assert.False(child.IsVisible); Assert.False(notebook.IsVisible); Assert.Null(controller.Window); Assert.Null((await store.ReadAsync()).Document.CollectedEntries[0].Senses[0].ReviewCards);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task NotebookAddCardAndExplicitSendShareTheCanonicalOwner() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-notebook-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var server = new SessionTestServer(); await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, server, new AnkiTestCredentials());
            var controller = new FrozenVocabularyWindowController(store, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
            controller.AttachReviewFeature(feature, () => { }); controller.Show(new VocabularySelection(entry.Id, entry.Senses[0].Id)); var notebook = controller.Window!;
            if (notebook.LastOperation is { } load) await load; await notebook.Session.RevealSelectionAsync(new VocabularySelection(entry.Id, entry.Senses[0].Id));
            var section = Assert.IsType<FrozenVocabularyReviewSection>(notebook.ReviewSection); Click(Button(notebook, "Add card")); var child = await Opened(section); await Ready(child);
            var id = child.CardId!.Value; Assert.True(child.SendButton.IsEnabled); Click(child.SendButton); await Ready(child); await child.RetireAsync(); await Wait(section);
            var saved = (await store.ReadAsync()).Document.CollectedEntries[0].Senses[0]; var review = Assert.Single(saved.ReviewCards!);
            Assert.Equal(id, review.Plan.Id); Assert.Equal(id, review.ActiveBinding!.OriginSenseId); Assert.NotNull(review.ActiveBinding.NoteId); Assert.Null(saved.Review); Assert.Equal(1, server.AddCount);
            await controller.DisposeAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Fact]
    public Task FeatureCanDrainWhileIdleModalRemainsOpen() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-section-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var feature = new AnkiReviewFeature(() => settings, () => { }, null, new SessionTestServer(), new AnkiTestCredentials());
            var owner = new Window { Width = 700, Height = 500 }; owner.Show(); var section = new FrozenVocabularyReviewSection(owner, store, feature, () => { }, () => { });
            owner.Content = section.Render(entry, entry.Senses[0]); Click(Button(owner, "Add card")); var child = await Opened(section); await Ready(child);
            await feature.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); Assert.True(child.IsVisible);
            await section.RetireAsync(); Assert.False(child.IsVisible); owner.Close();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    [Theory]
    [InlineData("notesInfo", false)]
    [InlineData("getActiveProfile", false)]
    [InlineData("findCards", false)]
    [InlineData("notesInfo", true)]
    [InlineData("getActiveProfile", true)]
    [InlineData("findCards", true)]
    public Task NavigationRechecksStoreAfterRemoteAwait(string heldAction, bool replaceBinding) => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "review-stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var settings = new AppSettings(); var server = new SessionTestServer(); var client = new NavigationAnki(server);
            await using var feature = new AnkiReviewFeature(() => settings, () => { }, null, client, new AnkiTestCredentials());
            var session = await CanonicalAnkiReviewSession.OpenAsync(feature.Controller, store, entry.Id, entry.Senses[0].Id, Guid.NewGuid());
            await feature.RunAsync(async token => Assert.True(await session.SendAsync(cancellationToken: token))); session.Dispose(); entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var card = entry.Senses[0].ReviewCards!.Single(); var binding = card.ActiveBinding!;
            var owner = new Window { Width = 700, Height = 500 }; owner.Show(); var section = new FrozenVocabularyReviewSection(owner, store, feature, () => { }, () => { });
            owner.Content = section.Render(entry, entry.Senses[0]); client.HoldAction = heldAction;
            Click(Button(owner, heldAction == "findCards" ? "Review in Anki" : "View in Anki")); await client.Entered.Task;
            if (replaceBinding)
            {
                var replacement = new Cotabby.Core.Vocabulary.Frozen.AnkiNoteBinding(Guid.NewGuid(), binding.OriginSenseId, binding.Endpoint, binding.Profile, binding.NoteId, binding.Model, binding.Deck, binding.IdentityField, binding.Mapping, binding.Fields, binding.SyncedAt);
                await store.SaveReviewAsync(entry.Id, entry.Senses[0].Id, card with { Bindings = [replacement], ActiveBindingId = replacement.Id }, card, card.Plan.Id);
            }
            else await store.RemoveReviewAsync(entry.Id, entry.Senses[0].Id, card.Plan.Id, card);
            client.Release.SetResult(); await Wait(section); Assert.Equal(0, client.Browses); Assert.Equal(0, client.Reviews);
            await section.RetireAsync(); owner.Close();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

}
