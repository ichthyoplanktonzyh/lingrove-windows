using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyNotebookWindowTests(WpfDispatcher dispatcher)
{
    private static readonly SourceDate Time = new(1e20);
    [Fact]
    public Task RealStoreEmptyCreateSearchAndSelection() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => Time);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!;
        Render(window, "frozen-empty.png"); Assert.NotNull(Find<Button>(window, "frozenEmptyAdd"));
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            var child = Application.Current.Windows.OfType<VocabularyEntryCreationWindow>().Single();
            Find<TextBox>(child, "vocabularyNewExpression").Text = "bank"; Find<TextBox>(child, "vocabularyNewMeaning").Text = "shore";
            Find<Button>(child, "vocabularyCreateEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Find<Button>(window, "frozenEmptyAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        Assert.NotNull(window.Session.Selection); Assert.Equal("bank", window.Session.Entries.Single().Expression);
        Assert.Equal(Time, window.Session.Entries.Single().CreatedAt); var selected = window.Session.Selection;
        await store.AddEntriesAsync([new(Expression: "other")]); await Until(() => window.Session.Snapshot?.Revision == 2); Assert.Equal(selected, window.Session.Selection);
        Render(window, "frozen-selected.png");
        var search = Find<TextBox>(window, "frozenNotebookSearch"); search.Text = "missing";
        Assert.Equal("No matching expressions", Find<TextBlock>(window, "frozenNotebookState").Text); Render(window, "frozen-search-empty.png");
        search.Text = "shore bank"; Assert.Single(window.Session.Visible);
        window.Width = 800; window.Height = 500; window.UpdateLayout(); Render(window, "frozen-resized.png"); window.Close();
        Assert.False(window.Session.Active); Assert.Equal(2ul, (await store.ReadAsync()).Revision);
    });
    [Fact]
    public Task RealCollectedGraphSupportsMeaningCategoryAndMultilingualNavigation() => dispatcher.RunAsync(async () =>
    {
        var first = F.VocabularyEntryImportPolicy.Plan([new(Expression: "bank", Meaning: "shore", Example: "river bank", ExampleTranslation: "河岸"), new(Expression: "banque", Meaning: "finance", Language: LearningLanguage.French), new(Expression: "hidden", Meaning: "uncollected")], new(), Time);
        var doc = first.Document; var bank = doc.Catalog.Single(e => e.Expression == "bank"); var hidden = doc.Catalog.Single(e => e.Expression == "hidden");
        var node = new F.VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("River edge") { Language = LearningLanguage.English }, new R.VocabularyMeaningLabel("河岸") { Language = LearningLanguage.Chinese }]) { Definition = "Land beside a river", ScopeNote = "Geography", CreatedAt = Time, UpdatedAt = Time };
        var cat = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Nature")]);
        doc = doc with { Collections = doc.Collections.Where(c => c.SenseId != hidden.Senses[0].Id).ToArray(), Semantics = new() { Nodes = [node], Mappings = [new(node.Id, bank.Senses[0].Id)] }, Classification = new() { Categories = [cat], Memberships = [new(Guid.NewGuid(), node.Id, cat.Id)] } };
        var db = FrozenVocabularySQLiteDatabase.CreateMemory(); db.Write(doc, new(), 0); await using var store = new FrozenVocabularyImportStore(db, () => Time);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), [LearningLanguage.Chinese]); window.Show(); await window.LastOperation!;
        Assert.Equal(2, window.Session.Entries.Count); Assert.Equal(3, window.Session.Snapshot!.Document.Catalog.Count);
        Assert.Single(window.Session.Snapshot.Document.Sources); Assert.Equal(Time, window.Session.Snapshot.Document.Catalog[0].CreatedAt);
        Find<ComboBox>(window, "frozenNotebookOrganization").SelectedIndex = 1; window.UpdateLayout();
        var category = FindByAutomation<ListBox>(window, "vocabularyCategoryList");
        var categoryRow = category.Items.Cast<ListBoxItem>().Single(x => x.Tag is string id && id.Equals(cat.Id.ToString(), StringComparison.OrdinalIgnoreCase)); category.SelectedItem = categoryRow; window.UpdateLayout();
        Assert.Equal(cat.Id, Guid.Parse(window.Session.Category));
        var meaning = FindByAutomation<ListBox>(window, "vocabularyMeaningList"); var row = meaning.Items.Cast<ListBoxItem>().Single(x => x.Tag is VocabularyMeaningDestination { NodeId: not null }); meaning.SelectedItem = row; window.UpdateLayout();
        Assert.Equal(node.Id, window.Session.Destination!.NodeId); Assert.Contains(Descendants(window).OfType<TextBlock>(), t => t.Text == "河岸");
        Assert.Contains(Descendants(window).OfType<TextBox>(), t => t.Text == "Land beside a river" && t.IsReadOnly);
        window.SetPreferredLanguages([LearningLanguage.English]); window.UpdateLayout();
        Assert.Equal(node.Id, window.Session.Destination!.NodeId); Assert.Contains(Descendants(window).OfType<TextBlock>(), t => t.Text == "River edge");
        window.SetPreferredLanguages([LearningLanguage.Chinese]); window.UpdateLayout();
        Assert.False(FindByAutomation<Button>(window, "vocabularyNewCategory").IsEnabled); Assert.False(FindByAutomation<Button>(window, "vocabularyManageCategories").IsEnabled);
        Assert.False(((MenuItem)categoryRow.ContextMenu.Items[0]).IsEnabled);
        Render(window, "frozen-meaning.png");
        var card = Descendants(window).OfType<Button>().Single(b => b.Tag is VocabularySelection selection && selection.SenseId == bank.Senses[0].Id);
        card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        Assert.Equal(bank.Senses[0].Id, window.Session.Destination!.SenseId); Assert.NotNull(Find<Button>(window, "frozenMeaningBack"));
        Render(window, "frozen-meaning-sense.png"); Find<Button>(window, "frozenMeaningBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        Assert.Equal(node.Id, window.Session.Destination!.NodeId); Assert.Null(window.Session.Destination.SenseId);
        Find<Button>(window, "frozenToggleCategories").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        Assert.DoesNotContain(Descendants(window), x => AutomationProperties.GetAutomationId(x) == "vocabularyCategoryList");
        Assert.Equal(node.Id, window.Session.Destination!.NodeId);
        Find<TextBox>(window, "frozenNotebookSearch").Text = "river"; Assert.Single(window.Session.MeaningItems);
        var lang = Find<ComboBox>(window, "frozenNotebookLanguage"); lang.SelectedIndex = 2; Assert.All(window.Session.Scoped, e => Assert.Equal(LearningLanguage.Spanish, e.Language));
        Assert.Equal(3, (await store.ReadAsync()).Document.Catalog.Count); window.Close();
    });
    [Fact]
    public Task MultiSenseSelectionKeepsIdentityAcrossStoreUpdates() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => Time);
        var one = await store.AddEntriesAsync([new(Expression: "bank", Meaning: "shore")]);
        var two = await store.AddEntriesAsync([new(Expression: "bank", Meaning: "finance")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!;
        var tree = Find<TreeView>(window, "frozenNotebookEntries"); var header = (TreeViewItem)tree.Items[0]; header.IsSelected = true; window.UpdateLayout();
        tree = Find<TreeView>(window, "frozenNotebookEntries"); header = (TreeViewItem)tree.Items[0]; Assert.True(header.IsExpanded);
        var second = header.Items.Cast<TreeViewItem>().Single(row => row.Tag is VocabularySelection selection && selection.SenseId == two.Selection!.Value.SenseId);
        second.IsSelected = true; Assert.Equal(two.Selection!.Value.SenseId, window.Session.Selection!.Value.SenseId);
        await store.AddEntriesAsync([new(Expression: "other")]); await Until(() => window.Session.Snapshot?.Revision == 3);
        Assert.Equal(two.Selection.Value.SenseId, window.Session.Selection!.Value.SenseId); Assert.Equal(one.Selection!.Value.EntryId, window.Session.Selection.Value.EntryId);
        Render(window, "frozen-multisense.png"); window.Close();
    });
    [Fact]
    public Task SortAndDensityChangePresentationWithoutWriting() => dispatcher.RunAsync(async () =>
    {
        int clock = 0; await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(++clock * 100));
        await store.AddEntriesAsync([new(Expression: "older", Example: "older example", Tags: ["All tags"])]); await store.AddEntriesAsync([new(Expression: "newer", Example: "newer example")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!;
        Assert.Equal("newer", window.Session.Visible[0].Expression); Find<ComboBox>(window, "frozenNotebookSort").SelectedIndex = 1;
        Assert.Equal("older", window.Session.Visible[0].Expression); Find<ComboBox>(window, "frozenNotebookDensity").SelectedIndex = 1;
        var tree = Find<TreeView>(window, "frozenNotebookEntries"); Assert.Contains("older example", ((TextBlock)((TreeViewItem)tree.Items[0]).Header).Text);
        Find<ComboBox>(window, "frozenNotebookTag").SelectedIndex = 1; Assert.Equal("All tags", window.Session.Tag); Assert.Single(window.Session.Visible);
        Assert.Equal(2ul, (await store.ReadAsync()).Revision); window.Close();
    });
    [Fact]
    public Task LongEntryLabelsTrimWithinNarrowSidebar() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => Time);
        await store.AddEntriesAsync([new(Expression: new string('W', 120), Meaning: "A long expression")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()) { Width = 760, Height = 500 }; window.Show(); await window.LastOperation!; window.UpdateLayout();
        var tree = Find<TreeView>(window, "frozenNotebookEntries"); var label = (TextBlock)((TreeViewItem)tree.Items[0]).Header;
        Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming); Assert.InRange(label.ActualWidth, 1, tree.ActualWidth - 20);
        Render(window, "frozen-long-label.png"); window.Close();
    });
    [Fact]
    public Task ImportUsesRealReaderAndRevealsAcknowledgedSelection() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => Time);
        await store.AddEntriesAsync([new(Expression: "older", Language: LearningLanguage.French)]);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"); await File.WriteAllTextAsync(path, "expression,meaning,example,translation\nbank,shore,river bank,河岸\n");
        try
        {
            var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), pickFile: () => path); window.Show(); await window.LastOperation!;
            Find<TextBox>(window, "frozenNotebookSearch").Text = "old"; Find<ComboBox>(window, "frozenNotebookLanguage").SelectedIndex = 3;
            _ = window.Dispatcher.BeginInvoke(async () =>
            {
                var child = Application.Current.Windows.OfType<VocabularyWordListImportWindow>().Single();
                while (child.Session.Preparing || child.Session.Preview is null) await Task.Delay(10);
                Find<Button>(child, "vocabularyConfirmImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Find<Button>(window, "frozenNotebookImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
            Assert.Equal("", window.Session.Query); Assert.Null(window.Session.Language); Assert.False(window.Session.MeaningMode);
            var selected = window.Session.Selection!.Value; var snapshot = await store.ReadAsync();
            Assert.Equal("bank", snapshot.Document.Catalog.Single(e => e.Id == selected.EntryId).Expression);
            Assert.Contains(snapshot.Document.CollectedEntries.SelectMany(e => e.Senses), sense => sense.Id == selected.SenseId && sense.Examples.Count == 1);
            Render(window, "frozen-imported.png"); window.Close();
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task FailedLoadShowsErrorAndDisablesMutations() => dispatcher.RunAsync(async () =>
    {
        var window = new FrozenVocabularyNotebookWindow(new FailedStore(), new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!;
        Assert.Contains("Could not load", Find<TextBlock>(window, "frozenNotebookState").Text);
        Assert.False(Find<Button>(window, "frozenNotebookAdd").IsEnabled); Assert.False(Find<Button>(window, "frozenNotebookImport").IsEnabled);
        Render(window, "frozen-error.png"); window.Close();
    });
    [Fact]
    public Task BurstCoalescesAndUncollectionReconcilesSelectionWithoutDeletingEvidence() => dispatcher.RunAsync(async () =>
    {
        var plan = F.VocabularyEntryImportPolicy.Plan([new(Expression: "bank", Example: "river bank")], new(), Time);
        var fake = new ManualStore(new(1, plan.Document)); var window = new FrozenVocabularyNotebookWindow(fake, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!;
        window.Session.Selection = plan.Result.Selection; window.Session.Refresh();
        var removed = plan.Document with { Collections = [] }; var hooks = window.Dispatcher.Hooks; int posts = 0;
        System.Windows.Threading.DispatcherHookEventHandler posted = (_, e) => { if (e.Operation.Priority == System.Windows.Threading.DispatcherPriority.DataBind) Interlocked.Increment(ref posts); };
        hooks.OperationPosted += posted;
        try { Task.Run(() => { for (ulong i = 1000; i > 1; i--) fake.Publish(new(i, removed)); }).GetAwaiter().GetResult(); Assert.Equal(1, posts); }
        finally { hooks.OperationPosted -= posted; }
        await Until(() => window.Session.Snapshot?.Revision == 1000); Assert.Null(window.Session.Selection); Assert.Empty(window.Session.Entries);
        Assert.Single(window.Session.Snapshot!.Document.Sources); Assert.Single(window.Session.Snapshot.Document.Interpretations);
        window.Close(); Task.Run(() => fake.Publish(new(1001, removed))).GetAwaiter().GetResult(); await Task.Delay(50);
        Assert.Equal(1000ul, window.Session.Snapshot.Revision); Assert.True(fake.Disposed);
    });
    private sealed class ManualStore(FrozenVocabularyImportSnapshot initial) : IVocabularyEntryImportStore
    {
        private Action<FrozenVocabularyImportSnapshot>? _callback; internal bool Disposed;
        internal void Publish(FrozenVocabularyImportSnapshot value) => _callback?.Invoke(value);
        public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) => Task.FromResult(initial);
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => throw new NotSupportedException();
        public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default) => throw new NotSupportedException();
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) { _callback = changed; return new Subscription(this); }
        private sealed class Subscription(ManualStore owner) : IDisposable { public void Dispose() => owner.Disposed = true; }
    }
    private sealed class FailedStore : IVocabularyEntryImportStore
    {
        public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) => Task.FromException<FrozenVocabularyImportSnapshot>(new IOException("Notebook unavailable"));
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => throw new NotSupportedException();
        public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default) => throw new NotSupportedException();
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) => new Subscription();
        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
    private static async Task Until(Func<bool> predicate)
    { var deadline = DateTime.UtcNow.AddSeconds(10); while (!predicate()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException(); await Task.Delay(10); } }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants(root).OfType<T>().First(x => x.Name == name);
    private static T FindByAutomation<T>(DependencyObject root, string name) where T : DependencyObject => Descendants(root).OfType<T>().First(x => AutomationProperties.GetAutomationId(x) == name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static void Render(Window window, string name)
    {
        var root = (FrameworkElement)window.Content; root.UpdateLayout(); int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is { Length: > 0 } dir)
        { Directory.CreateDirectory(dir); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(dir, name)); encoder.Save(output); }
    }
}
