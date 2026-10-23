using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Notebook glue adapts the original list/graph mode and shared selection. Filtered
/// entries come only from the existing latest-wins browse worker. The graph borrows those values,
/// one window presentation and the same detail/Store; hiding it retains camera/expansion state.</summary>
public partial class VocabularyNotebookWindow
{
    private IVocabularyGraphPreferences _graphPreferences = new TransientGraphPreferences();
    private VocabularyGraphPresentation? _graphPresentation;
    private VocabularyGraphView? _graphView;
    private IReadOnlyList<VocabularyEntry> _graphEntries = [];
    private string _graphQuery = "";
    private Guid? _lastGraphSense;
    /// <summary>One graph-mode filter edit freezes its original acknowledged inputs until the
    /// browse worker evaluates it. Later list-only edits do not replace this criterion.</summary>
    private sealed record GraphFilterConstraint(VocabularyDocument Document, string Query, LearningLanguage? Language, string Tag);
    /// <summary>Window-owned pending constraints share one selected identity. Any excluding edit
    /// clears it; a later explicit selection cancels the whole receipt before publication.</summary>
    private sealed record GraphFilterReconciliation(Guid SenseId, IReadOnlyList<GraphFilterConstraint> Filters);
    private GraphFilterReconciliation? _pendingGraphReconciliation;
    private (bool Graph, bool Detail)? _presentedBrowserLayout;
    internal bool IsGraphMode => _graphPreferences.BrowserMode == "graph";
    internal VocabularyGraphView? GraphView => _graphView;
    private void InitializeGraph(IVocabularyGraphPreferences? preferences)
    {
        _graphPreferences = preferences ?? _graphPreferences;
        _graphPresentation = new(_graphPreferences);
        var search = _textSearch as IVocabularyGraphTextSearching ?? new GraphSearchAdapter(_textSearch);
        _graphView = new(_graphPresentation, search, Track) { Visibility = Visibility.Collapsed };
        GraphHost.Children.Add(_graphView); PresentBrowserMode();
    }
    private void OnBrowserMode(object sender, RoutedEventArgs args)
    {
        if (_presenting || !IsInitialized || _retired || _graphPresentation is null || sender is not RadioButton { Tag: string mode }) return;
        if (_graphPreferences.BrowserMode == mode) return;
        _graphPreferences.BrowserMode = mode;
        if (mode == "graph") { _graphPresentation.Reveal(Selection); _lastGraphSense = Selection?.SenseId; }
        PresentBrowserMode(); RefreshGraph();
        // Existing tree rows contain a projected selection flag. Re-publish that list when
        // returning from graph navigation, keeping the acknowledged detail while filtering runs.
        if (mode != "graph") RequestRefresh();
    }
    private void PresentBrowserMode()
    {
        if (_graphView is null) return;
        bool presenting = _presenting; _presenting = true;
        try { ListModeButton.IsChecked = !IsGraphMode; GraphModeButton.IsChecked = IsGraphMode; }
        finally { _presenting = presenting; }
        EntriesTree.Visibility = IsGraphMode ? Visibility.Collapsed : Visibility.Visible;
        SortButton.Visibility = IsGraphMode ? Visibility.Collapsed : Visibility.Visible;
        GraphHost.Visibility = _graphView.Visibility = IsGraphMode ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = IsGraphMode || _graphEntries.Count != 0 ? Visibility.Collapsed : Visibility.Visible;
        bool detail = !IsGraphMode || Selection is not null;
        var layout = (Graph: IsGraphMode, Detail: detail);
        // A filter/selection/settings redraw is not a new split-view layout. Preserve widths
        // the user resized, resetting defaults only when mode/detail presence actually changes.
        if (_presentedBrowserLayout != layout)
        {
            BrowserColumn.MinWidth = IsGraphMode ? 350 : 230; BrowserColumn.MaxWidth = IsGraphMode ? double.PositiveInfinity : 420;
            if (_presentedBrowserLayout?.Graph != IsGraphMode) BrowserColumn.Width = IsGraphMode ? new(1, GridUnitType.Star) : new(290);
            DetailColumn.MinWidth = detail ? 300 : 0; DetailColumn.MaxWidth = IsGraphMode ? 480 : double.PositiveInfinity;
            DetailColumn.Width = !detail ? new(0) : IsGraphMode ? new(360) : new(1, GridUnitType.Star);
            _presentedBrowserLayout = layout;
        }
        DetailsPane.Visibility = DetailDivider.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        DividerColumn.Width = new(detail ? 5 : 0); CloseGraphDetailButton.Visibility = IsGraphMode && detail ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshGraph()
    {
        if (_graphView is null || _graphPresentation is null) return;
        var selection = Selection;
        if (IsGraphMode && selection?.SenseId != _lastGraphSense)
        { _graphPresentation.Reveal(selection); _lastGraphSense = selection?.SenseId; }
        _graphView.Update(_graphEntries, _graphQuery, selection, SelectFromGraph); PresentBrowserMode();
    }
    private void ApplyGraphBrowse(BrowseRequest request, BrowseResult result)
    {
        _graphEntries = result.Entries; _graphQuery = request.Query;
        // Mac reconciles synchronously at a graph filter edit. Our browse worker is asynchronous:
        // carry that intent across superseding Store/sort/mode refreshes until the latest result.
        // Explicit selection/navigation cancels it, so a delayed filter cannot clear a later choice.
        if (request.GraphReconciliation is { } reconciliation && ReferenceEquals(reconciliation, _pendingGraphReconciliation))
        {
            var selected = VocabularyNotebookPolicy.ResolveSelection(request.Document, reconciliation.SenseId)?.SenseId;
            if (_selection == selected && selected is not null && result.ClearGraphSelection)
            { _selection = null; _graphPresentation?.ClearFocus(); }
            _pendingGraphReconciliation = null;
        }
        RefreshGraph();
        if (_graphView is { } view) view.IsEnabled = true;
    }
    private bool GraphFilterExcludesSelection(GraphFilterConstraint filter, Guid selected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Each constraint retains the graph-mode filter and acknowledged document at that edit.
        // A later list-only query cannot change its outcome, and an earlier excluding graph edit
        // cannot be undone by a later including one before the dispatcher receives the result.
        string tag = VocabularyNotebookPolicy.ResolveTag(VocabularyNotebookPolicy.Tags(filter.Document), filter.Tag);
        return !VocabularyNotebookPolicy.VisibleEntries(filter.Document, _textSearch, filter.Query, filter.Language, tag, true, token)
            .Any(entry => VocabularySensePolicy.Index(selected, entry) is not null);
    }
    private void SelectFromGraph(VocabularySelection? selection)
    {
        if (_retired || !IsGraphMode || !IsVisible || _graphView is not { IsEnabled: true, IsVisible: true }) return;
        _pendingGraphReconciliation = null;
        var resolved = VocabularyNotebookPolicy.ResolveSelection(_store.Snapshot.Document, selection?.SenseId);
        if (_selection != resolved?.SenseId) DetailViewport.ScrollToTop();
        _selection = resolved?.SenseId; RenderDetail(_store.Snapshot.Document);
    }
    private void OnCloseGraphDetail(object sender, RoutedEventArgs args)
    { _pendingGraphReconciliation = null; _selection = null; _graphPresentation?.ClearFocus(); RenderDetail(_store.Snapshot.Document); }
    private void ShowListForBatch()
    {
        if (!IsGraphMode) return;
        _graphPreferences.BrowserMode = "list"; PresentBrowserMode();
    }
    private void RetireGraph() { if (_graphView is { } view) Track(view.RetireAsync()); }

    /// <summary>Explicit in-memory ownership for an embedded/test notebook with no application
    /// preferences owner. AppHost always injects its existing durable settings adapter.</summary>
    private sealed class TransientGraphPreferences : IVocabularyGraphPreferences
    {
        private byte[]? _settings;
        public byte[]? ReadSettings() => _settings?.ToArray();
        public void WriteSettings(ReadOnlySpan<byte> data) => _settings = data.ToArray();
        public string BrowserMode { get; set; } = "list";
    }
    /// <summary>Existing custom notebook search providers may implement only sense search. Keep
    /// that provider for sense matches while the required case-only headword path uses Windows.</summary>
    private sealed class GraphSearchAdapter(IVocabularyTextSearching sense) : IVocabularyGraphTextSearching
    {
        private readonly WindowsVocabularyTextSearch _head = new();
        public bool Contains(string field, string term) => sense.Contains(field, term);
        public bool HeadwordContains(string field, string term) => _head.HeadwordContains(field, term);
    }
}
