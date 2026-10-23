using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Real notebook windows and memory SQLite exercise asynchronous browsing on the STA dispatcher.
/// Only the text-provider scheduling gate is controlled; archive/Store, XAML, selection containers
/// and literal document rendering remain production code. No model, editor or clipboard is used.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyNotebookViewTests(WpfDispatcher dispatcher)
{
    private static VocabularyCapture Capture(string text = "The bank.", string expression = "bank", LearningLanguage language = LearningLanguage.English) =>
        new(expression, expression, language, VocabularyEntryKind.Word, Example: new(Guid.NewGuid(), text, "原句",
            [new(text.IndexOf(expression, StringComparison.Ordinal), expression.Length)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch));
    private static VocabularyStore Store(VocabularyArchive archive) => new(archive, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    private static VocabularySelection Destination(VocabularyStore store, VocabularyCapture capture)
    { var value = store.ExplanationDestination(capture)!.Value; return new(value.EntryId, value.SenseId); }
    private static async Task Refresh(VocabularyNotebookWindow window)
    {
        // Store notifications already posted before its operation completes must get a turn
        // before awaiting the latest worker; the dispatcher keeps pumping throughout both waits.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; window.UpdateLayout();
    }
    private static VocabularyEntryRow[] Rows(VocabularyNotebookWindow window) => Find<TreeView>(window, "EntriesTree").Items.Cast<VocabularyEntryRow>().ToArray();
    private static void Select(VocabularyNotebookWindow window, VocabularySelection selection)
    {
        var tree = Find<TreeView>(window, "EntriesTree");
        var row = tree.Items.Cast<VocabularyEntryRow>().Single(value => value.Senses.Any(sense => sense.Selection == selection));
        var entry = Assert.IsType<TreeViewItem>(tree.ItemContainerGenerator.ContainerFromItem(row));
        entry.IsExpanded = true; window.UpdateLayout();
        var sense = row.Senses.Single(value => value.Selection == selection);
        Assert.IsType<TreeViewItem>(entry.ItemContainerGenerator.ContainerFromItem(sense)).IsSelected = true;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T result) yield return result;
            foreach (var value in Descendants<T>(child)) yield return value;
        }
    }
    [Fact]
    public Task ActivatingNotebookRendersLiteralContentAndFullSavedExampleRanges() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); var capture = Capture(); Assert.True(await store.SaveAsync(capture));
        var baseline = Assert.Single(store.Entries); var original = Assert.Single(baseline.Senses);
        string text = new string('x', 12000) + "bank";
        var sense = original with { Meaning = "<meaning>&literal", Note = "<script>literal only</script>", Tags = ["Work"],
            Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("<b>Authored</b>", VocabularyTextStyle.Heading, Italic: true)))],
            AlternateMeanings = [new("other wording", LearningLanguage.English)],
            Examples = [original.Examples[0] with { Text = text, Ranges = [new(12000, 4)], Translation = "原句", TranslationLanguage = LearningLanguage.Chinese,
                TranslationVariants = [new("原句", LearningLanguage.Chinese), new("another translation", LearningLanguage.English)], Meaning = "context meaning" }] };
        Assert.True(await store.UpdateAsync(baseline with { Senses = [sense] }, baseline));
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(new(baseline.Id, sense.Id)); window.Show(); await Refresh(window);
            Assert.True(window.ShowActivated); Assert.True(window.Focusable); Assert.True(window.ShowInTaskbar);
            Assert.Equal(0, GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 0x08000000);
            Assert.Equal(new VocabularySelection(baseline.Id, sense.Id), window.Selection);
            var boxes = Descendants<TextBox>(Find<StackPanel>(window, "DetailContent")).ToArray();
            Assert.Contains(boxes, box => box.Text == "<script>literal only</script>" && box.IsReadOnly);
            Assert.Contains(boxes, box => box.Text == "<meaning>&literal");
            Assert.Contains(boxes, box => box.Text == "<b>Authored</b>" && box.FontStyle == FontStyles.Italic);
            Assert.Single(boxes, box => box.Text == "原句"); Assert.Contains(boxes, box => box.Text == "another translation");
            var rich = Assert.Single(Descendants<RichTextBox>(window)); var paragraph = Assert.Single(rich.Document.Blocks.OfType<Paragraph>());
            var runs = paragraph.Inlines.OfType<Run>().ToArray(); Assert.Equal(text, string.Concat(runs.Select(run => run.Text)));
            Assert.Equal("bank", Assert.Single(runs, run => run.FontWeight == FontWeights.Bold).Text);
            var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content); Assert.InRange(root.ActualWidth, 700, 1200);
            var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index => pixels[index * 4 + 3] > 0);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task FiltersPreserveGlobalDetailAndOrdinaryClosePreservesBrowsing() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); var bank = Capture(); var coffee = Capture("Le café.", "café", LearningLanguage.French);
        Assert.True(await store.SaveAsync(bank)); Assert.True(await store.SaveAsync(coffee));
        var baseline = store.Entries.Single(entry => entry.Expression == "bank");
        Assert.True(await store.UpdateAsync(baseline with { Senses = [baseline.Senses[0] with { Tags = ["Work"], Note = "preserved note" }] }, baseline));
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            var target = Destination(store, bank); window.Reveal(target); window.Show(); await Refresh(window);
            var detail = Find<StackPanel>(window, "DetailContent").Children[0]; var graph = store.Snapshot.Document; ulong revision = store.Snapshot.Revision;
            Find<TextBox>(window, "SearchText").Text = "cafe"; await Refresh(window);
            Assert.Equal("café", Assert.Single(Rows(window)).Expression); Assert.Equal(target, window.Selection);
            Assert.Same(detail, Find<StackPanel>(window, "DetailContent").Children[0]);
            Find<ComboBox>(window, "LanguageFilter").SelectedValue = LearningLanguage.English; await Refresh(window); Assert.Empty(Rows(window));
            Find<TextBox>(window, "SearchText").Text = ""; Find<ComboBox>(window, "TagFilter").SelectedValue = "Work"; await Refresh(window);
            Assert.Equal("bank", Assert.Single(Rows(window)).Expression); Click(Find<Button>(window, "SortButton")); await Refresh(window);
            window.Close(); Assert.False(window.IsVisible); window.Reveal(null); window.Show(); await Refresh(window);
            Assert.Equal("Work", Find<ComboBox>(window, "TagFilter").SelectedValue); Assert.Equal("Oldest first", Find<Button>(window, "SortButton").Content);
            window.Reveal(Destination(store, coffee)); await Refresh(window);
            Assert.Equal(2, Rows(window).Length); Assert.Equal("", Find<TextBox>(window, "SearchText").Text);
            Assert.Equal(0, Find<ComboBox>(window, "LanguageFilter").SelectedIndex); Assert.Equal("", Find<ComboBox>(window, "TagFilter").SelectedValue);
            Assert.Equal("Newest first", Find<Button>(window, "SortButton").Content); Assert.Equal(Destination(store, coffee), window.Selection);
            Assert.Equal(revision, store.Snapshot.Revision); Assert.Equal(graph, store.Snapshot.Document);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task ColdLoadUsesDirectedSelectionOnceAndLaterSaveKeepsUserSelection() => dispatcher.RunAsync(async () =>
    {
        var archive = new VocabularyArchive((string?)null); await using var store = Store(archive);
        var bank = Capture(); var coffee = Capture("Le café.", "café", LearningLanguage.French);
        await archive.SaveAsync(bank); var snapshot = await archive.SaveAsync(coffee);
        var entry = snapshot.Document.Entries.Single(value => value.Expression == "bank"); var target = new VocabularySelection(entry.Id, entry.Senses[0].Id);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(target); window.Show(); await Refresh(window); Assert.Null(window.Selection);
            Assert.Equal("Loading vocabulary…", Find<TextBlock>(window, "EmptyText").Text);
            await store.LoadAsync(); await Refresh(window); Assert.Equal(target, window.Selection);
            var next = Destination(store, coffee); Select(window, next); Assert.Equal(next, window.Selection);
            Assert.True(await store.SaveAsync(Capture("Another bank."))); await Refresh(window);
            Assert.Equal(next, window.Selection); Assert.Equal("café", Assert.IsType<TextBox>(Find<StackPanel>(window, "DetailContent").Children[0]).Text);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task MergedSenseAliasFollowsLiveTargetAndDeletedSenseClearsDetail() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); var first = Capture(); var second = Capture("My bank.");
        Assert.True(await store.SaveAsync(first)); Assert.True(await store.SaveAsync(second));
        var from = Destination(store, first); var to = Destination(store, second); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(from); window.Show(); await Refresh(window); Assert.Equal(from, window.Selection);
            Assert.True(await store.MergeSensesAsync(from.EntryId, from.SenseId, to.SenseId)); await Refresh(window); Assert.Equal(to, window.Selection);
            window.Reveal(from); await Refresh(window); Assert.Equal(to, window.Selection);
            Assert.True(await store.RemoveSenseAsync(to.EntryId, to.SenseId)); await Refresh(window);
            Assert.Null(window.Selection); Assert.Equal("Vocabulary", Assert.IsType<TextBlock>(Find<StackPanel>(window, "DetailContent").Children[0]).Text);
        }
        finally { await window.RetireAsync(); }
    });
    private sealed class SearchGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class GatedSearch : IVocabularyTextSearching
    {
        private readonly WindowsVocabularyTextSearch _actual = new();
        public Dictionary<string, SearchGate> Gates { get; } = new() { ["slow"] = new(), ["retiring"] = new() };
        public bool Contains(string field, string term)
        {
            if (Gates.TryGetValue(term, out var gate)) { gate.Entered.TrySetResult(); gate.Release.Task.GetAwaiter().GetResult(); }
            return _actual.Contains(field, term);
        }
    }
    [Fact]
    public Task LateSearchCannotReplaceNewerResultsAndRetirementJoinsAdmittedWork() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture()));
        var search = new GatedSearch(); var window = new VocabularyNotebookWindow(store, search);
        try
        {
            window.Show(); await Refresh(window); var input = Find<TextBox>(window, "SearchText");
            input.Text = "slow"; var old = window.LastRefresh; await search.Gates["slow"].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            input.Text = "bank"; await Refresh(window); Assert.Equal("bank", Assert.Single(Rows(window)).Expression);
            search.Gates["slow"].Release.SetResult(); await old; await Refresh(window); Assert.Equal("bank", Assert.Single(Rows(window)).Expression);
            input.Text = "retiring"; await search.Gates["retiring"].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var retirement = window.RetireAsync(); Assert.False(window.IsVisible); Assert.False(retirement.IsCompleted);
            search.Gates["retiring"].Release.SetResult(); await retirement;
        }
        finally { foreach (var gate in search.Gates.Values) gate.Release.TrySetResult(); await window.RetireAsync(); }
    });
    [Fact]
    public Task SharedShowRouteReusesOneWindowAndDisposalRemovesItsRoute() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); var capture = Capture(); Assert.True(await store.SaveAsync(capture));
        var controller = new VocabularyWindowController(store, Dispatcher.CurrentDispatcher);
        try
        {
            controller.Show(); var window = Assert.Single(Application.Current.Windows.OfType<VocabularyNotebookWindow>()); await Refresh(window);
            Find<TextBox>(window, "SearchText").Text = "missing"; await Refresh(window); window.Close();
            store.Show(capture); await Refresh(window);
            Assert.Same(window, Assert.Single(Application.Current.Windows.OfType<VocabularyNotebookWindow>()));
            Assert.True(window.IsVisible); Assert.Equal(Destination(store, capture), window.Selection); Assert.Equal("", Find<TextBox>(window, "SearchText").Text);
        }
        finally { await controller.DisposeAsync(); }
        Assert.Null(store.OpenSelection); Assert.Empty(Application.Current.Windows.OfType<VocabularyNotebookWindow>());
    });
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
}
