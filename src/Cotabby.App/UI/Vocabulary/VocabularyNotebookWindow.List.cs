using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Adapts the current stable notebook's sidebar density and headword interactions.
/// The shared Store owns meaning identity; this window owns expansion and selection. Density
/// alone is durable. Updating a selected compact row changes inline content without rebuilding
/// its container or changing row height. Search snippets are calculated by the browse worker.</summary>
public partial class VocabularyNotebookWindow
{
    private readonly IVocabularyNotebookListPreferences _listPreferences;
    private VocabularyEntry? _lastSelectedEntry;
    private bool IsDetailedList => _listPreferences.Density == "detailed";
    private sealed class TransientListPreferences : IVocabularyNotebookListPreferences
    { public string Density { get; set; } = "compact"; public bool ShowUnexplored { get; set; } }
    private void OnListDensity(object sender, RoutedEventArgs args)
    {
        if (_retired || _presenting || !IsInitialized) return;
        _listPreferences.Density = IsDetailedList ? "compact" : "detailed";
        PresentListDensity(); RequestRefresh();
    }
    private void PresentListDensity()
    {
        ListDensityButton.Content = IsDetailedList ? "Detailed list" : "Compact list";
        ListDensityButton.ToolTip = IsDetailedList ? "Switch to compact list" : "Switch to detailed list";
        ListDensityButton.Visibility = IsGraphMode ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnEntryHeaderMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (_retired || _presenting || args.ChangedButton != MouseButton.Left || sender is not FrameworkElement { DataContext: VocabularyEntryRow row }) return;
        if (!EntriesTree.IsEnabled || row.OfferVersion != _version) return;
        bool previous = _presenting; _presenting = true;
        try { (EntriesTree.ItemContainerGenerator.ContainerFromItem(row) as TreeViewItem)?.Focus(); }
        finally { _presenting = previous; }
        ActivateHeadword(row); args.Handled = true;
    }
    private void OnEntryKeyDown(object sender, KeyEventArgs args)
    {
        if (_retired || _presenting || args.Key is not (Key.Enter or Key.Space)) return;
        // A focused checklist control owns its Space key; the tree's headword activation must
        // not consume it or change the selected sense behind that checkbox.
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source is not null && !ReferenceEquals(source, EntriesTree);)
        {
            if (source is System.Windows.Controls.Primitives.ToggleButton) return;
            if (source is TreeViewItem container)
            {
                if (container.DataContext is VocabularyEntryRow row) { ActivateHeadword(row); args.Handled = true; }
                return;
            }
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
    }
    internal void ActivateHeadword(VocabularyEntryRow row)
    {
        if (_retired || _presenting || !EntriesTree.IsEnabled || row.OfferVersion != _version || !EntriesTree.Items.Contains(row) || row.Senses.Count == 0) return;
        bool selected = row.Senses.Any(sense => sense.Selection.SenseId == _selection);
        if (!row.IsSingle) row.IsExpanded = !(row.IsExpanded && selected);
        if (!selected)
        {
            _pendingGraphReconciliation = null; DetailViewport.ScrollToTop();
            _selection = row.Senses[0].Selection.SenseId;
        }
        RenderDetail(_store.Snapshot.Document);
    }
    private void RevealSelectedEntry(VocabularyEntry? entry)
    {
        if (_lastSelectedEntry == entry) return;
        _lastSelectedEntry = entry;
        // External graph/directed navigation reveals a different multi-sense owner, while a
        // second click on the same selected header remains free to collapse it in place.
        if (entry is null || entry.Senses.Count <= 1) return;
        _expanded.Add(entry.Id);
        if (EntriesTree.Items.OfType<VocabularyEntryRow>().FirstOrDefault(row => row.EntryId == entry.Id) is { } row) row.IsExpanded = true;
    }
    private void PresentListSelection()
    {
        bool previous = _presenting; _presenting = true;
        try { foreach (var row in EntriesTree.Items.OfType<VocabularyEntryRow>()) row.PresentSelection(_selection); }
        finally { _presenting = previous; }
    }
    private IReadOnlyDictionary<Guid, string> MatchSnippets(IReadOnlyList<VocabularyEntry> entries, string query, CancellationToken token)
    {
        var snippets = new Dictionary<Guid, string>(); string trimmed = FoundationText.TrimWhitespaceAndNewlines(query);
        if (trimmed.Length == 0) return snippets;
        foreach (var entry in entries)
        foreach (var sense in entry.Senses)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Senses.Count == 1 && _textSearch.Contains(entry.Expression, trimmed)
                || _textSearch.Contains(VocabularyNotebookPolicy.DisplayTitle(sense), trimmed)) continue;
            foreach (var example in sense.Examples)
            {
                token.ThrowIfCancellationRequested();
                if (!_textSearch.Contains(example.Text, trimmed) && !_textSearch.Contains(example.Translation, trimmed)) continue;
                snippets[sense.Id] = example.Text; break;
            }
        }
        return snippets;
    }
}
