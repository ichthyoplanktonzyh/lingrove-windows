using Cotabby.App.Localization;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;
using VocabularyEntry = Cotabby.Core.Vocabulary.Frozen.VocabularyEntry;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>One transient taxonomy outline; IDs remain the Store's category and meaning IDs.</summary>
internal sealed class FrozenVocabularyMeaningOutline : UserControl
{
    private sealed class Search : IVocabularyTextSearching { public bool Contains(string field, string term) => CultureInfo.CurrentCulture.CompareInfo.IndexOf(field, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0; }
    private readonly ListBox _list = new() { Name = "canonicalMeaningOutline" };
    private readonly CheckBox _unexplored = new() { Name = "canonicalShowUnexplored", Content = InterfaceText.Get("Show unexplored categories") };
    private readonly Button _manage = new() { Content = "Manage", Name = "outlineManageCategories", Padding = new Thickness(8, 4, 8, 4) }, _create = new() { Content = "+", Name = "outlineNewCategory", Padding = new Thickness(8, 4, 8, 4), ToolTip = "New category" };
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private R.VocabularyClassificationIndex _index = new();
    private IReadOnlyList<VocabularyMeaningBrowserProjection.Item> _items = [];
    private IReadOnlyList<VocabularyEntry> _entries = [];
    private IReadOnlyList<VocabularySelection> _unclassified = [];
    private VocabularyMeaningDestination? _destination;
    private string _query = ""; private bool _updating, _initialExpanded, _canEdit, _canCreateMeaning, _unclassifiedExpanded, _uncategorizedExpanded = true;
    internal event Action<VocabularyMeaningDestination>? Selected;
    internal event Action<Guid?>? NewCategory;
    internal event Action<Guid>? EditCategory;
    internal event Action<Guid>? NewMeaning;
    internal event Action? ManageCategories;
    internal event Action<bool>? ShowUnexploredChanged;
    internal bool ShowUnexplored { get => _unexplored.IsChecked == true; set => _unexplored.IsChecked = value; }
    internal FrozenVocabularyMeaningOutline()
    {
        var root = new DockPanel(); VocabularyNotebookTheme.Brush(root, Panel.BackgroundProperty, "SidebarBg"); Content = root; var bar = new DockPanel { Margin = new Thickness(10, 8, 10, 8) };
        VocabularyNotebookTheme.Button(_manage); _manage.Click += (_, _) => { if (_canEdit) ManageCategories?.Invoke(); }; DockPanel.SetDock(_manage, Dock.Right); bar.Children.Add(_manage); VocabularyNotebookTheme.Button(_create); _create.Click += (_, _) => { if (_canEdit) NewCategory?.Invoke(null); }; DockPanel.SetDock(_create, Dock.Right); bar.Children.Add(_create);
        var outlineMenu = new ContextMenu(); var expandAll = new MenuItem { Header = InterfaceText.Get("Expand all populated") }; expandAll.Click += (_, _) => { var populated = _index.Memberships.Where(x => x.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(x => x.CategoryId).ToHashSet(); _expanded.UnionWith(VocabularyClassificationProjection.ExpansionPaths(populated, _index, includeTarget: true)); Render(); }; var collapseAll = new MenuItem { Header = InterfaceText.Get("Collapse all") }; collapseAll.Click += (_, _) => { _expanded.Clear(); Render(); }; outlineMenu.Items.Add(expandAll); outlineMenu.Items.Add(collapseAll); _manage.ContextMenu = outlineMenu;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_manage, "vocabularyManageCategories"); System.Windows.Automation.AutomationProperties.SetAutomationId(_create, "vocabularyNewCategory"); System.Windows.Automation.AutomationProperties.SetAutomationId(_list, "vocabularyMeaningList");
        VocabularyNotebookTheme.Brush(_unexplored, Control.ForegroundProperty, "TextPrimary"); _unexplored.Margin = new Thickness(0, 5, 8, 5); _unexplored.Click += (_, _) => { ShowUnexploredChanged?.Invoke(ShowUnexplored); Render(); }; bar.Children.Add(_unexplored); DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        _list.BorderThickness = new Thickness(0); ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled); VocabularyNotebookTheme.Brush(_list, Control.BackgroundProperty, "SidebarBg"); VocabularyNotebookTheme.Brush(_list, Control.ForegroundProperty, "TextPrimary"); System.Windows.Automation.AutomationProperties.SetName(_list, "Meaning categories"); _list.SelectionChanged += (_, _) => { if (!_updating && _list.SelectedItem is ListBoxItem { Tag: VocabularyMeaningDestination destination }) Selected?.Invoke(destination); }; root.Children.Add(_list);
    }
    internal void Present(R.VocabularyClassificationIndex index, IReadOnlyList<VocabularyMeaningBrowserProjection.Item> items, IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<VocabularySelection> unclassified, VocabularyMeaningDestination? destination, string query, bool canEdit, bool canCreateMeaning)
    {
        _index = index; _items = items; _entries = entries; _unclassified = unclassified; _destination = destination; _query = query; _canEdit = canEdit; _canCreateMeaning = canCreateMeaning; _manage.IsEnabled = _create.IsEnabled = canEdit;
        if (destination is { Kind: VocabularyMeaningDestinationKind.Unclassified } or { Kind: VocabularyMeaningDestinationKind.Sense, NodeId: null }) _unclassifiedExpanded = true;
        var selected = destination?.NodeId is { } node ? index.Memberships.Where(x => x.NodeId == node && x.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(x => x.CategoryId).ToHashSet() : new HashSet<Guid>(); if (destination?.CategoryId is { } category) selected.Add(category);
        _expanded.UnionWith(VocabularyClassificationProjection.ExpansionPaths(selected, index, includeTarget: destination?.NodeId is not null));
        if (!_initialExpanded && index.Categories.Count > 0) { var populated = index.Memberships.Where(x => x.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(x => x.CategoryId).ToHashSet(); _expanded.UnionWith(VocabularyClassificationProjection.ExpansionPaths(populated, index, includeTarget: true)); _initialExpanded = true; }
        Render();
    }
    private void Render()
    {
        _updating = true; try
        {
            _list.Items.Clear(); var active = _index.Categories.Any(x => !x.IsRetired); var locale = VocabularyCategoryUiLanguage.Identifier; var revealed = new HashSet<Guid>();
            if (_destination?.CategoryId is { } category) revealed.Add(category); if (_destination?.NodeId is { } node) revealed.UnionWith(_index.Memberships.Where(x => x.NodeId == node && x.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(x => x.CategoryId));
            if (active) foreach (var axis in Enum.GetValues<R.VocabularyCategoryAxis>())
            {
                var rows = VocabularyClassificationProjection.Outline(axis, _index, _items, _entries, _expanded, new Search(), [locale], _query, _unexplored.IsChecked == true, revealed); if (rows.Count == 0) continue;
                var coverage = VocabularyClassificationProjection.AxisCoverage(axis, _index, _items.Select(x => x.Node).ToArray()); AddHeader((axis == R.VocabularyCategoryAxis.Content ? "Content meanings" : "Communicative functions") + (coverage.Total > 0 ? $"  {coverage.Explored}/{coverage.Total}" : "")); foreach (var row in rows) AddOutlineRow(row);
            }
            var unassigned = active ? VocabularyClassificationProjection.Uncategorized(_index, _items) : _items; if (active && unassigned.Count > 0) AddDisclosure("Uncategorized meanings (" + unassigned.Count + ")", _uncategorizedExpanded, () => _uncategorizedExpanded = !_uncategorizedExpanded); if (!active || _uncategorizedExpanded) foreach (var item in unassigned) AddMeaning(item, 0);
            if (_unclassified.Count > 0) { var row = new DockPanel(); var toggle = new Button { Content = _unclassifiedExpanded ? "−" : "+", Width = 24, ToolTip = "Expand other expressions" }; VocabularyNotebookTheme.Button(toggle); VocabularyNotebookTheme.Brush(toggle, Control.ForegroundProperty, "TextPrimary"); toggle.Click += (_, e) => { e.Handled = true; _unclassifiedExpanded = !_unclassifiedExpanded; Render(); }; DockPanel.SetDock(toggle, Dock.Left); row.Children.Add(toggle); row.Children.Add(new TextBlock { Text = "Other expressions (" + _unclassified.Count + ")", Margin = new Thickness(6, 4, 0, 4) }); AddItem(row, new(VocabularyMeaningDestinationKind.Unclassified)); if (_unclassifiedExpanded) foreach (var selection in _unclassified) { var entry = _entries.FirstOrDefault(x => x.Id == selection.EntryId); var sense = entry?.Senses.FirstOrDefault(x => x.Id == selection.SenseId); if (entry is not null && sense is not null) AddItem(entry.Expression + " · " + sense.Meaning, 1, new(VocabularyMeaningDestinationKind.Sense, SenseId: sense.Id)); } }
            VocabularyNotebookTheme.Sidebar(_list);
        }
        finally { _updating = false; }
    }
    private void AddHeader(string title) => _list.Items.Add(new ListBoxItem { Content = VocabularyNotebookTheme.Brush(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }, TextBlock.ForegroundProperty, "TextSecondary"), IsHitTestVisible = false, Focusable = false, Margin = new Thickness(4, 8, 0, 2) });
    private void AddDisclosure(string title, bool expanded, Action toggle) { var button = new Button { Content = (expanded ? "−  " : "+  ") + title, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(5, 4, 5, 4) }; VocabularyNotebookTheme.Button(button); VocabularyNotebookTheme.Brush(button, Control.ForegroundProperty, "TextPrimary"); button.Click += (_, _) => { toggle(); Render(); }; _list.Items.Add(new ListBoxItem { Content = button, Focusable = false }); }
    private void AddOutlineRow(VocabularyClassificationProjection.OutlineRow row)
    {
        if (row.Meaning is { } meaning) { AddMeaning(meaning, row.Depth); return; } if (row.Category is not { } category) return;
        var content = new DockPanel { Margin = new Thickness(Math.Min(row.Depth, 8) * 13, 2, 0, 2) }; var expand = new Button { Content = _expanded.Contains(row.Id) ? "−" : "+", Width = 24, IsEnabled = row.HasChildren, ToolTip = "Expand " + row.Title }; VocabularyNotebookTheme.Button(expand); VocabularyNotebookTheme.Brush(expand, Control.ForegroundProperty, "TextPrimary"); expand.Click += (_, e) => { e.Handled = true; if (!_expanded.Add(row.Id)) _expanded.Remove(row.Id); Render(); }; DockPanel.SetDock(expand, Dock.Left); content.Children.Add(expand); content.Children.Add(new TextBlock { Text = row.Title + (row.ItemCount == 0 ? "  ·  unexplored" : "  ·  " + row.ItemCount), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        var menu = new ContextMenu(); var newMeaning = Menu("New meaning", () => NewMeaning?.Invoke(category.Id)); newMeaning.IsEnabled = _canCreateMeaning; menu.Items.Add(newMeaning); menu.Items.Add(Menu("New subcategory", () => NewCategory?.Invoke(category.Id))); menu.Items.Add(Menu("Edit category", () => EditCategory?.Invoke(category.Id))); var item = AddItem(content, new(VocabularyMeaningDestinationKind.Category, CategoryId: category.Id)); item.ContextMenu = menu;
    }
    private MenuItem Menu(string label, Action action) { var item = new MenuItem { Header = label, IsEnabled = _canEdit }; item.Click += (_, _) => action(); return item; }
    private void AddMeaning(VocabularyMeaningBrowserProjection.Item item, int depth)
    {
        var names = item.Selections.Select(s => _entries.FirstOrDefault(e => e.Id == s.EntryId)?.Expression).Where(s => s is not null).Distinct(StringComparer.CurrentCulture).ToArray(); var preview = names.Length > 0 ? string.Join(" · ", names) : item.Node.Definition;
        var content = new StackPanel { Margin = new Thickness(Math.Min(depth, 8) * 13 + 30, 3, 0, 3) }; content.Children.Add(new TextBlock { Text = item.Title, TextTrimming = TextTrimming.CharacterEllipsis }); if (preview.Length > 0) content.Children.Add(new TextBlock { Text = preview, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.PendingCount > 0) content.Children.Add(new TextBlock { Text = item.PendingCount + " suggested", FontSize = 11 }); AddItem(content, new(VocabularyMeaningDestinationKind.Node, item.Id));
    }
    private ListBoxItem AddItem(string text, int depth, VocabularyMeaningDestination destination) => AddItem(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(Math.Min(depth, 8) * 13 + 30, 4, 0, 4) }, destination);
    private ListBoxItem AddItem(object content, VocabularyMeaningDestination destination) { var item = new ListBoxItem { Content = content, Tag = destination, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsSelected = Equals(_destination, destination) }; _list.Items.Add(item); return item; }
}
