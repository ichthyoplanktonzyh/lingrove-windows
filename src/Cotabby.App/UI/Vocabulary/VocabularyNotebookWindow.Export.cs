using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Notebook-owned transient batch state and export presentation. Mac's beginExport policy freezes
/// IDs from one acknowledged browse projection; this adapter waits for Windows worker filtering
/// to publish that projection. Row/menu receipts retire on refresh, while an already opened export
/// retains its own request. The shared Store still owns every byte and filesystem operation.
/// </summary>
public partial class VocabularyNotebookWindow
{
    private bool _selectingSenses;
    private readonly HashSet<VocabularySelection> _batchSelection = [];
    private readonly HashSet<ContextMenu> _senseExportMenus = [];
    private sealed record ExportOffer(BrowseRequest Browse, IReadOnlyList<VocabularyEntry> Visible);
    private sealed record SenseExportReceipt(VocabularySelection Selection, long Version);
    private ExportOffer? _exportOffer;
    private VocabularyExportWindow? _exportWindow;

    private bool CanOfferExport => !_retired && IsVisible && _exportWindow is null && _exportOffer is { } offer
        && offer.Browse.Version == _version && offer.Browse.Loaded
        && _store.Snapshot.Revision == offer.Browse.Revision;
    private IEnumerable<VocabularySenseRow> SenseRows => EntriesTree.Items.OfType<VocabularyEntryRow>().SelectMany(entry => entry.Senses);
    private void UpdateExportAdmission()
    {
        bool allowed = CanOfferExport;
        ExportButton.IsEnabled = allowed && _exportOffer!.Browse.Document.Entries.Count > 0;
        SelectVisibleButton.IsEnabled = allowed && _selectingSenses;
        ExportSelectedButton.IsEnabled = allowed && _batchSelection.Count > 0;
        foreach (var row in SenseRows) row.BatchEnabled = allowed;
    }
    private void InvalidateExportOffer()
    {
        _exportOffer = null; CloseSenseExportMenus(); UpdateExportAdmission();
    }
    private void OnSelectSenses(object sender, RoutedEventArgs args)
    {
        if (_presenting || !IsInitialized || _retired) return;
        _selectingSenses = SelectSensesButton.IsChecked == true;
        if (_selectingSenses) ShowListForBatch();
        SelectVisibleButton.Visibility = ExportSelectedButton.Visibility = _selectingSenses ? Visibility.Visible : Visibility.Collapsed;
        // As on Mac, leaving checklist mode keeps its IDs; filters never clear that set either.
        RequestRefresh();
    }
    private void SetBatch(VocabularySelection selection, bool selected, long version)
    {
        if (!CanOfferExport || !_selectingSenses || version != _version) return;
        if (selected) _batchSelection.Add(selection); else _batchSelection.Remove(selection);
        UpdateExportAdmission();
    }
    private void OnSelectVisible(object sender, RoutedEventArgs args)
    {
        if (!CanOfferExport || !_selectingSenses || _exportOffer is not { } offer) return;
        _batchSelection.UnionWith(offer.Visible.SelectMany(entry => entry.Senses.Select(sense => new VocabularySelection(entry.Id, sense.Id))));
        foreach (var row in SenseRows) row.PresentBatch(_batchSelection.Contains(row.Selection));
        UpdateExportAdmission();
    }
    private void OnExportNotebook(object sender, RoutedEventArgs args)
    { if (CanOfferExport && _exportOffer!.Browse.Document.Entries.Count > 0) BeginExport(); }
    private void OnExportSelected(object sender, RoutedEventArgs args)
    { if (_batchSelection.Count > 0) BeginExport(VocabularyExportScope.Selected); }
    private void OnSenseExportMenuOpening(object sender, ContextMenuEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: VocabularySenseRow row }
            || !CanOfferExport || row.OfferVersion != _version)
        { args.Handled = true; return; }
    }
    private void OnSenseExportMenuOpened(object sender, RoutedEventArgs args)
    {
        if (sender is not ContextMenu menu) return;
        if (!CanOfferExport || menu.PlacementTarget is not FrameworkElement { DataContext: VocabularySenseRow row } || row.OfferVersion != _version)
        { menu.IsOpen = false; return; }
        var item = (MenuItem)menu.Items[0]; item.Tag = new SenseExportReceipt(row.Selection, _version);
        _senseExportMenus.Add(menu);
    }
    private void OnSenseExportMenuClosed(object sender, RoutedEventArgs args)
    {
        if (sender is not ContextMenu menu) return;
        _senseExportMenus.Remove(menu); foreach (var item in menu.Items.OfType<MenuItem>()) item.Tag = null;
    }
    private void OnExportSingle(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: SenseExportReceipt receipt } || !CanOfferExport || receipt.Version != _version) return;
        // No alias expansion: this command belongs to the exact sense whose menu was opened.
        if (_exportOffer!.Browse.Document.Entries.Any(entry => entry.Id == receipt.Selection.EntryId
            && entry.Senses.Any(sense => sense.Id == receipt.Selection.SenseId))) BeginExport(single: receipt.Selection);
    }
    private void CloseSenseExportMenus()
    {
        foreach (var menu in _senseExportMenus.ToArray())
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) item.Tag = null;
            menu.IsOpen = false;
        }
        _senseExportMenus.Clear();
    }
    private void BeginExport(VocabularyExportScope? scope = null, VocabularySelection? single = null)
    {
        if (!CanOfferExport || _exportOffer is not { } offer) return;
        var b = offer.Browse;
        var request = VocabularyExportOfferPolicy.Request(b.Document.Entries, offer.Visible, _batchSelection,
            b.Newest, b.Query, b.Language, b.Tag, scope, single);
        var window = new VocabularyExportWindow(_store, request) { Owner = this }; _exportWindow = window;
        window.OperationStarted += Track; CloseSenseExportMenus(); UpdateExportAdmission();
        try { window.ShowDialog(); }
        finally
        {
            window.OperationStarted -= Track; Track(window.DrainAsync()); _exportWindow = null; UpdateExportAdmission();
        }
    }
    private void RetireExport()
    {
        CloseSenseExportMenus();
        if (_exportWindow is { } window) Track(window.RetireAsync());
    }
}
