using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Actual notebook XAML/chrome, graph worker, shared Store and SQLite run together on
/// WPF. Only preference storage is in memory. Routed controls and canvas intent transitions do
/// not claim physical input, external UIA, native popover placement or desktop pixel parity.</summary>
[Collection("WPF views")]
public sealed class VocabularyGraphNotebookTests(WpfDispatcher dispatcher)
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static VocabularySelection Bank => new(Id(1), Id(2));
    private static VocabularySelection OtherBank => new(Id(1), Id(4));
    private static VocabularyGraphNodeId Head(int n) => VocabularyGraphNodeId.Headword(Id(n));
    private static VocabularyDocument Seed()
    {
        var now = DateTimeOffset.UnixEpoch;
        return new() { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            [new VocabularySense(Id(2), "river shore", now, now) with { Tags = ["Work"] },
             new VocabularySense(Id(4), "financial institution", now, now) with { Tags = ["Home"] }], now, now),
            new(Id(10), "café", "café", LearningLanguage.French, VocabularyEntryKind.Word,
                [new VocabularySense(Id(12), "coffee", now, now) with { Tags = ["Work"] }], now, now.AddSeconds(1))] };
    }
    private sealed class Preferences(string mode = "list") : IVocabularyGraphPreferences
    {
        private byte[]? _data;
        internal int Writes;
        public byte[]? ReadSettings() => _data?.ToArray();
        public void WriteSettings(ReadOnlySpan<byte> data) { _data = data.ToArray(); Writes++; }
        public string BrowserMode { get; set; } = mode;
    }
    private static async Task<VocabularyStore> Store(SynchronizationContext? context = null)
    {
        var archive = new VocabularyArchive((string?)null); await archive.CommitAsync(Seed());
        var store = new VocabularyStore(archive, context ?? SynchronizationContext.Current!); await store.LoadAsync(); return store;
    }
    private static async Task Refresh(VocabularyNotebookWindow window)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh.WaitAsync(TimeSpan.FromSeconds(10));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh.WaitAsync(TimeSpan.FromSeconds(10)); window.UpdateLayout();
    }
    private static async Task Until(Func<bool> predicate)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!predicate()) { Assert.True(DateTime.UtcNow < limit, "Notebook graph state did not arrive."); await Task.Delay(5); }
    }
    private static async Task Cooled(VocabularyGraphView view)
    {
        await Until(() => view.Canvas.RenderedFrameCount > 0);
        await view.Canvas.LastAnimation.WaitAsync(TimeSpan.FromSeconds(10)); await Until(() => !view.Canvas.IsAnimating);
    }
    private static VocabularyGraphView Graph(VocabularyNotebookWindow window) => Assert.IsType<VocabularyGraphView>(window.GraphView);
    private static T Control<T>(VocabularyGraphView view, string name) where T : class => Assert.IsAssignableFrom<T>(view.FindName(name));
    private static void Mode(VocabularyNotebookWindow window, bool graph) => Find<RadioButton>(window, graph ? "GraphModeButton" : "ListModeButton").IsChecked = true;
    private static VocabularySenseRow[] Rows(VocabularyNotebookWindow window) => Find<TreeView>(window, "EntriesTree").Items.OfType<VocabularyEntryRow>().SelectMany(row => row.Senses).ToArray();
    private static Task Retire(VocabularyNotebookWindow window) => window.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    public Task SharedShowRouteRestoresDurableModeAndSettingsAndRevealsTheExactSense() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store();
        var saved = new AppSettings { VocabularyBrowserMode = "graph" }; saved.WriteVocabularyGraphSettings(VocabularyGraphSettingsCodec.EncodeNormalized(new() { NodeSize = 23 }));
        var current = SettingsStore.Parse(SettingsStore.Serialize(saved)); int saves = 0;
        var preferences = new VocabularyGraphPreferences(() => current, () => saves++);
        var controller = new VocabularyWindowController(store, Dispatcher.CurrentDispatcher, graphPreferences: preferences);
        try
        {
            controller.Show(Bank); var window = Assert.Single(Application.Current.Windows.OfType<VocabularyNotebookWindow>()); await Refresh(window);
            var view = Graph(window); await Cooled(view);
            Assert.True(window.IsGraphMode); Assert.Equal(Bank, window.Selection); Assert.Contains(Id(1), view.Presentation.Expanded);
            Assert.Equal(23, view.Presentation.Settings.NodeSize); Assert.Equal(0, saves);
            Assert.Contains(VocabularyGraphNodeId.Sense(Bank), view.Canvas.Positions.Keys);
            window.Close(); controller.Show(OtherBank); await Refresh(window);
            Assert.Same(window, Assert.Single(Application.Current.Windows.OfType<VocabularyNotebookWindow>())); Assert.Equal(OtherBank, window.Selection);
        }
        finally { await controller.DisposeAsync(); }
    });

    [Fact]
    public Task ModeChangesShareOneDetailAndClosingGraphDetailClearsOnlySelectionAndFocus() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var preferences = new Preferences(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: preferences);
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var detail = Find<StackPanel>(window, "DetailContent").Children[0];
            Assert.False(window.IsGraphMode); Mode(window, true); await Cooled(Graph(window));
            Assert.Equal(Visibility.Collapsed, Find<Button>(window, "SortButton").Visibility);
            Assert.Same(detail, Find<StackPanel>(window, "DetailContent").Children[0]); Assert.Equal(Bank, window.Selection);
            Assert.Equal(Visibility.Visible, Find<DockPanel>(window, "DetailsPane").Visibility);
            Click(Find<Button>(window, "CloseGraphDetailButton")); Assert.Null(window.Selection); Assert.Null(Graph(window).Presentation.FocusedEntryId);
            Assert.Contains(Id(1), Graph(window).Presentation.Expanded); Assert.Equal(Visibility.Collapsed, Find<DockPanel>(window, "DetailsPane").Visibility);
            var held = UIElementAutomationPeer.CreatePeerForElement(Graph(window).Canvas)!.GetChildren()!.First();
            Mode(window, false); await Refresh(window); Assert.Null(window.Selection); Assert.Equal("list", preferences.BrowserMode);
            Assert.Equal(Visibility.Visible, Find<DockPanel>(window, "DetailsPane").Visibility);
            Assert.Equal(Visibility.Visible, Find<Button>(window, "SortButton").Visibility);
            Assert.False(held.IsEnabled()); Assert.Throws<ElementNotEnabledException>(() => ((IInvokeProvider)held.GetPattern(PatternInterface.Invoke)!).Invoke());
            Graph(window).Canvas.Activate(VocabularyGraphNodeId.Sense(OtherBank)); Assert.Null(window.Selection);
        }
        finally { await Retire(window); }
    });

    [Theory]
    [InlineData("query")]
    [InlineData("tag")]
    [InlineData("language")]
    public Task GraphFilterChangesReconcileTheExactSenseEvenIfItsHeadwordRemains(string filter) => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: new Preferences("graph"));
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window);
            if (filter == "query") Find<TextBox>(window, "SearchText").Text = "café";
            else if (filter == "tag") Find<ComboBox>(window, "TagFilter").SelectedValue = "Home";
            else Find<ComboBox>(window, "LanguageFilter").SelectedValue = LearningLanguage.French;
            await Refresh(window); Assert.Null(window.Selection); Assert.Null(Graph(window).Presentation.FocusedEntryId);
            Assert.Equal(Visibility.Collapsed, Find<DockPanel>(window, "DetailsPane").Visibility);
            if (filter == "tag") Assert.Equal(OtherBank, Assert.Single(Rows(window)).Selection);
        }
        finally { await Retire(window); }
    });

    [Fact]
    public Task ListFilteringKeepsGlobalDetailAndEnteringGraphWaitsForANewFilterBeforeReconciling() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window);
            Find<TextBox>(window, "SearchText").Text = "café"; await Refresh(window); Assert.Equal(Bank, window.Selection);
            Mode(window, true); Assert.Equal(Bank, window.Selection); Assert.Contains(Id(1), Graph(window).Presentation.Expanded);
            Find<TextBox>(window, "SearchText").Text = "missing"; await Refresh(window); Assert.Null(window.Selection);
            await Until(() => Graph(window).Canvas.Positions.Count == 0); Assert.False(Graph(window).Canvas.IsAnimating);
        }
        finally { await Retire(window); }
    });

    [Fact]
    public Task HeadwordAndSenseIntentsUseTheAcknowledgedStoreAndSameDetailPanel() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: new Preferences("graph"));
        try
        {
            window.Show(); await Refresh(window); var view = Graph(window); await Cooled(view); Assert.Equal(2, view.Canvas.Positions.Count);
            view.Canvas.Activate(Head(1)); await Until(() => view.Canvas.Positions.Count == 4); Assert.Null(window.Selection);
            view.Canvas.Activate(VocabularyGraphNodeId.Sense(OtherBank)); Assert.Equal(OtherBank, window.Selection);
            Assert.Equal("bank", Assert.IsType<TextBox>(Find<StackPanel>(window, "DetailContent").Children[0]).Text);
            view.Canvas.Activate(Head(1)); await Until(() => view.Canvas.Positions.Count == 2); Assert.Null(window.Selection);
            Assert.Empty(view.Presentation.Expanded); Assert.Equal(Visibility.Collapsed, Find<DockPanel>(window, "DetailsPane").Visibility);
        }
        finally { await Retire(window); }
    });

    [Fact]
    public Task AppearanceAndForceControlsPersistNormalizedOriginalSettingsAndRestoreDefaults() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var preferences = new Preferences("graph");
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: preferences);
        try
        {
            window.Show(); await Refresh(window); var view = Graph(window); Assert.Equal(0, preferences.Writes);
            Click(Control<Button>(view, "GraphSettings")); Assert.True(Control<Popup>(view, "GraphSettingsPopup").IsOpen);
            Control<Slider>(view, "GraphNodeSize").Value = 24; Control<Slider>(view, "GraphLinkWidth").Value = 2.5;
            Control<Slider>(view, "GraphCenter").Value = .04; Control<Slider>(view, "GraphRepulsion").Value = 600;
            Control<Slider>(view, "GraphLinkStrength").Value = .4; Control<Slider>(view, "GraphLinkDistance").Value = 180;
            Control<ComboBox>(view, "GraphLabels").SelectedValue = VocabularyGraphLabels.Focused;
            var expected = new VocabularyGraphSettings { NodeSize = 24, LinkWidth = 2.5, Center = .04, Repulsion = 600, LinkStrength = .4, LinkDistance = 180, Labels = VocabularyGraphLabels.Focused };
            Assert.Equal(expected, view.Presentation.Settings); Assert.Equal(expected, VocabularyGraphSettingsCodec.Decode(preferences.ReadSettings()!)); Assert.Equal(7, preferences.Writes);
            Click(Control<Button>(view, "GraphDefaults")); Assert.Equal(new VocabularyGraphSettings(), view.Presentation.Settings);
            Assert.Equal(new VocabularyGraphSettings(), VocabularyGraphSettingsCodec.Decode(preferences.ReadSettings()!)); Assert.Equal(8, preferences.Writes);
            Assert.Equal(3, store.Snapshot.Document.Entries.Sum(entry => entry.Senses.Count));
        }
        finally { await Retire(window); }
    });

    [Fact]
    public Task ChromeCommandsUseOnePresentationAndCollapseClearsTheSharedDetail() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: new Preferences("graph"));
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var view = Graph(window); await Cooled(view);
            var camera = view.Presentation.Camera; var worker = view.Presentation.Worker;
            Click(Control<Button>(view, "GraphZoomIn")); Assert.Equal(camera.Scale * 1.2, view.Presentation.Camera.Scale, 8);
            Click(Control<Button>(view, "GraphZoomOut")); Assert.Equal(camera.Scale, view.Presentation.Camera.Scale, 8);
            Click(Control<Button>(view, "GraphFit")); var expected = new VocabularyGraphCamera().Fit(view.Canvas.Positions.Values.ToArray(), new(view.Canvas.ActualWidth, view.Canvas.ActualHeight));
            Assert.Equal(expected, view.Presentation.Camera);
            Click(Control<Button>(view, "GraphLocate")); var head = view.Canvas.Screen(view.Canvas.Positions[Head(1)]);
            Assert.Equal(view.Canvas.ActualWidth / 2, head.X, 6); Assert.Equal(view.Canvas.ActualHeight / 2, head.Y, 6);
            Click(Control<Button>(view, "GraphCollapse")); Assert.Null(window.Selection); Assert.Empty(view.Presentation.Expanded); Assert.Null(view.Presentation.FocusedEntryId);
            await Until(() => view.Canvas.Positions.Count == 2); Assert.Same(worker, view.Presentation.Worker);
        }
        finally { await Retire(window); }
    });

    [Fact]
    public Task OrdinaryCloseRetainsCameraExpansionAndModeButClosesTheSettingsPopup() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var preferences = new Preferences("graph");
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: preferences);
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var view = Graph(window); await Cooled(view);
            Click(Control<Button>(view, "GraphZoomIn")); var camera = view.Presentation.Camera; var worker = view.Presentation.Worker;
            Click(Control<Button>(view, "GraphSettings")); Assert.True(Control<Popup>(view, "GraphSettingsPopup").IsOpen);
            window.Close(); await view.Canvas.LastSuspension.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(window.IsVisible); Assert.False(view.Canvas.IsAnimating); Assert.False(Control<Popup>(view, "GraphSettingsPopup").IsOpen);
            window.Reveal(null); window.Show(); await Refresh(window); await Cooled(view);
            Assert.True(window.IsGraphMode); Assert.Equal("graph", preferences.BrowserMode); Assert.Equal(Bank, window.Selection);
            Assert.Equal(camera, view.Presentation.Camera); Assert.Contains(Id(1), view.Presentation.Expanded); Assert.Same(worker, view.Presentation.Worker);
            await Retire(window); Assert.False(view.Canvas.IsAnimating);
            view.Canvas.Activate(VocabularyGraphNodeId.Sense(OtherBank)); Assert.Equal(Bank, window.Selection);
        }
        finally { await Retire(window); }
    });

    [Fact]
    public Task EnteringBatchModeForcesListAndRetainsExactCheckedIdentitiesAcrossModeAndFilters() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var preferences = new Preferences("graph");
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: preferences);
        try
        {
            window.Show(); await Refresh(window); var document = store.Snapshot.Document; ulong revision = store.Snapshot.Revision;
            var selecting = Find<ToggleButton>(window, "SelectSensesButton"); selecting.IsChecked = true; await Refresh(window);
            Assert.False(window.IsGraphMode); Assert.Equal("list", preferences.BrowserMode);
            Rows(window).Single(row => row.Selection == Bank).IsBatchSelected = true;
            Mode(window, true); Assert.True(selecting.IsChecked); Assert.True(window.IsGraphMode);
            selecting.IsChecked = false; selecting.IsChecked = true; await Refresh(window); Assert.False(window.IsGraphMode);
            Assert.True(Rows(window).Single(row => row.Selection == Bank).IsBatchSelected);
            Find<TextBox>(window, "SearchText").Text = "café"; await Refresh(window); Assert.False(Assert.Single(Rows(window)).IsBatchSelected);
            Find<TextBox>(window, "SearchText").Text = ""; await Refresh(window); Assert.True(Rows(window).Single(row => row.Selection == Bank).IsBatchSelected);
            Assert.True(Find<Button>(window, "ExportSelectedButton").IsEnabled); Assert.Equal(revision, store.Snapshot.Revision); Assert.Equal(document, store.Snapshot.Document);
        }
        finally { await Retire(window); }
    });

    [Fact]
    public Task GraphSelectionFollowsMergedIdentityAndRetiredSenseCannotReopenTheDetail() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), graphPreferences: new Preferences("graph"));
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var view = Graph(window); await Cooled(view);
            Assert.True(await store.MergeSensesAsync(Id(1), Bank.SenseId, OtherBank.SenseId)); await Refresh(window);
            Assert.Equal(OtherBank, window.Selection); await Until(() => !view.Canvas.Positions.ContainsKey(VocabularyGraphNodeId.Sense(Bank)));
            Assert.True(await store.RemoveSenseAsync(Id(1), OtherBank.SenseId)); await Refresh(window); Assert.Null(window.Selection);
            view.Canvas.Activate(VocabularyGraphNodeId.Sense(OtherBank)); Assert.Null(window.Selection);
            Assert.Equal(Visibility.Collapsed, Find<DockPanel>(window, "DetailsPane").Visibility);
        }
        finally { await Retire(window); }
    });

    private sealed class SearchGate(string term = "slow") : IVocabularyTextSearching
    {
        private readonly WindowsVocabularyTextSearch _actual = new();
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Contains(string field, string term)
        { if (term == _term) { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); } return _actual.Contains(field, term); }
        private readonly string _term = term;
    }
    private sealed class FailOnceSearch : IVocabularyTextSearching
    {
        private readonly WindowsVocabularyTextSearch _actual = new();
        private int _failed;
        public bool Contains(string field, string term)
        {
            if (term == "bank" && Interlocked.Exchange(ref _failed, 1) == 0) throw new InvalidOperationException("controlled provider failure");
            return _actual.Contains(field, term);
        }
    }
    [Fact]
    public Task FailedBrowseOffersRetryAndOnlyASuccessfulLatestResultEnablesGraphIntents() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyNotebookWindow(store, new FailOnceSearch(), graphPreferences: new Preferences("graph"));
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var view = Graph(window);
            Find<TextBox>(window, "SearchText").Text = "bank"; await Refresh(window);
            Assert.False(view.IsEnabled); Assert.Equal(Visibility.Visible, Find<Button>(window, "RetryBrowseButton").Visibility);
            Assert.Equal("The vocabulary view could not be updated.", Find<TextBlock>(window, "StorageError").Text);
            Click(Find<Button>(window, "RetryBrowseButton")); await Refresh(window);
            Assert.True(view.IsEnabled); Assert.Equal(Visibility.Collapsed, Find<Button>(window, "RetryBrowseButton").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "StorageError").Visibility); Assert.Equal(Bank, window.Selection);
            Assert.All(Rows(window), row => Assert.Equal(Id(1), row.Selection.EntryId));
        }
        finally { await Retire(window); }
    });
    [Fact]
    public Task BrowseAndSettingsRedrawsPreserveUserSplitWidthsWithinTheSameMode() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window);
            var browser = Assert.IsType<ColumnDefinition>(window.FindName("BrowserColumn")); browser.Width = new(335);
            Click(Find<Button>(window, "SortButton")); await Refresh(window); Assert.Equal(new GridLength(335), browser.Width);
            Mode(window, true); await Cooled(Graph(window)); var detail = Assert.IsType<ColumnDefinition>(window.FindName("DetailColumn")); detail.Width = new(420);
            Control<Slider>(Graph(window), "GraphNodeSize").Value = 24;
            Graph(window).Canvas.Activate(VocabularyGraphNodeId.Sense(OtherBank)); Assert.Equal(OtherBank, window.Selection);
            Assert.Equal(new GridLength(420), detail.Width);
        }
        finally { await Retire(window); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GraphFilterReconciliationSurvivesModeRefreshButCannotClearLaterExplicitNavigation(bool navigate) => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var search = new SearchGate();
        var window = new VocabularyNotebookWindow(store, search, graphPreferences: new Preferences("graph"));
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var input = Find<TextBox>(window, "SearchText");
            input.Text = "slow"; var old = window.LastRefresh; await search.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var view = Graph(window); Assert.False(view.IsEnabled); view.Canvas.Activate(VocabularyGraphNodeId.Sense(OtherBank)); Assert.Equal(Bank, window.Selection);
            var peer = UIElementAutomationPeer.CreatePeerForElement(view.Canvas)!.GetChildren()!.Single(node => node.GetAutomationId() == Id(1).ToString("D"));
            Assert.False(peer.IsEnabled()); Assert.Throws<ElementNotEnabledException>(() => ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke());
            Mode(window, false);
            if (navigate) window.Reveal(OtherBank);
            else input.Text = "café";
            search.Release.TrySetResult(); await Refresh(window); Assert.Equal(navigate ? OtherBank : null, window.Selection);
            await old.WaitAsync(TimeSpan.FromSeconds(10)); await Refresh(window);
            Assert.Equal(navigate ? OtherBank : null, window.Selection);
            if (!navigate) Assert.Equal(Id(10), Assert.Single(Rows(window)).Selection.EntryId);
        }
        finally { search.Release.TrySetResult(); await Retire(window); }
    });

    [Theory]
    [InlineData("bank", "café", true, false)]
    [InlineData("café", "bank", true, true)]
    [InlineData("café", "bank", false, true)]
    [InlineData("bank", "café", false, true)]
    public Task PendingGraphEditsRetainTheirOriginalCriteriaAcrossDifferentLaterFilters(string first, string second, bool toList, bool cleared) => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var search = new SearchGate(first);
        var window = new VocabularyNotebookWindow(store, search, graphPreferences: new Preferences("graph"));
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var input = Find<TextBox>(window, "SearchText");
            input.Text = first; var original = window.LastRefresh; await search.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (toList)
            {
                Mode(window, false);
                var tree = Find<TreeView>(window, "EntriesTree");
                // WPF may replay an already selected row while generating visible containers.
                // This routed event must not count as a new user choice or cancel graph edits.
                tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object?>(null, Rows(window).Single(row => row.Selection == Bank), TreeView.SelectedItemChangedEvent));
            }
            input.Text = second; search.Release.TrySetResult(); await Refresh(window); await original.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(cleared ? null : Bank, window.Selection);
            if (cleared) Assert.All(Rows(window), row => Assert.False(row.IsSelected));
            Assert.Equal(toList, !window.IsGraphMode);
            Assert.All(Rows(window), row => Assert.Equal(second == "bank" ? Id(1) : Id(10), row.Selection.EntryId));
        }
        finally { search.Release.TrySetResult(); await Retire(window); }
    });

    /// <summary>Only the Store's notification return is gated: the real commit/snapshot and
    /// Changed callback run before its awaiting command can publish UI acknowledgement.</summary>
    private sealed class MutationNotificationGate(Dispatcher ui) : SynchronizationContext
    {
        private int _armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            ui.BeginInvoke(() => callback(state));
            if (Interlocked.Exchange(ref _armed, 0) != 1) return;
            Entered.TrySetResult(); Release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
    }
    [Fact]
    public Task AcknowledgedMergeSelectionRetiresEarlierFilterConstraintsBeforeAliasResolution() => dispatcher.RunAsync(async () =>
    {
        var notification = new MutationNotificationGate(Dispatcher.CurrentDispatcher);
        await using var store = await Store(notification); var search = new SearchGate();
        var window = new VocabularyNotebookWindow(store, search, graphPreferences: new Preferences("graph"));
        try
        {
            window.Reveal(Bank); window.Show(); await Refresh(window); var input = Find<TextBox>(window, "SearchText");
            input.Text = "slow"; var old = window.LastRefresh; await search.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            notification.Arm();
            var merge = Find<Button>(window, "MergeSenseButton"); Click(merge);
            Assert.Single(merge.ContextMenu!.Items.OfType<MenuItem>()).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await notification.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            search.Release.TrySetResult(); await Refresh(window); await old.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(window.LastMutation.IsCompleted); Assert.Equal(OtherBank, window.Selection);
            notification.Release.TrySetResult(); await window.LastMutation.WaitAsync(TimeSpan.FromSeconds(10)); await Refresh(window);
            Assert.Equal(OtherBank, window.Selection);
            Assert.Contains(Bank.SenseId, store.Snapshot.Document.Entries.Single(entry => entry.Id == Id(1)).Senses.Single().MergedFrom);
        }
        finally { notification.Release.TrySetResult(); search.Release.TrySetResult(); await Retire(window); }
    });
}
