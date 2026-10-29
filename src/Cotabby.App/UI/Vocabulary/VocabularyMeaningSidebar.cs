using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

public enum VocabularyMeaningDestinationKind { Node, Unclassified, Sense }
public sealed record VocabularyMeaningDestination(VocabularyMeaningDestinationKind Kind, Guid? NodeId = null, Guid? SenseId = null);

/// <summary>Detached presentation of an acknowledged browser snapshot. It owns only selection,
/// disclosure and WPF controls; the eventual notebook owner supplies current data and callbacks.</summary>
public sealed class VocabularyMeaningSidebar : UserControl
{
    private readonly ListBox _list = new();
    private IReadOnlyList<VocabularyMeaningBrowserProjection.Item> _items = [];
    private IReadOnlyList<F.VocabularyEntry> _entries = [];
    private IReadOnlyList<VocabularySelection> _unclassified = [];
    private readonly Func<F.VocabularySense, string> _senseTitle;
    private readonly Func<LearningLanguage?, string> _languageLabel;
    private readonly string _otherLabel, _suggestedLabel;
    private VocabularyMeaningDestination? _destination;
    private bool _expanded, _updating;
    private double _scale = 1;
    private bool _detailed, _notebookTheme;
    public event Action<VocabularyMeaningDestination>? DestinationChanged;

    public VocabularyMeaningSidebar(string otherExpressionsLabel, string suggestedGroupingLabel,
        Func<F.VocabularySense, string> senseTitle, Func<LearningLanguage?, string> languageLabel)
    {
        _otherLabel = otherExpressionsLabel; _suggestedLabel = suggestedGroupingLabel;
        _senseTitle = senseTitle; _languageLabel = languageLabel;
        VocabularySidebarVisuals.Automation(_list, "vocabularyMeaningList", otherExpressionsLabel);
        _list.BorderThickness = new Thickness(0); _list.Background = SystemColors.WindowBrush;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += SelectionChanged; Content = _list;
    }

    internal void UseNotebookTheme()
    {
        _notebookTheme = true; VocabularyNotebookTheme.Brush(_list, Control.BackgroundProperty, "SidebarBg"); Refresh();
    }
    public void Present(IReadOnlyList<VocabularyMeaningBrowserProjection.Item> items, IReadOnlyList<F.VocabularyEntry> entries,
        IReadOnlyList<VocabularySelection> unclassified, VocabularyMeaningDestination? destination, bool detailed = false, double textScale = 1)
    {
        _items = Array.AsReadOnly(items.ToArray()); _entries = Array.AsReadOnly(entries.ToArray()); _unclassified = Array.AsReadOnly(unclassified.ToArray());
        _destination = destination; _detailed = detailed; _scale = textScale;
        if (destination?.Kind == VocabularyMeaningDestinationKind.Unclassified || destination is { Kind: VocabularyMeaningDestinationKind.Sense, NodeId: null }) _expanded = true;
        Refresh();
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _list.SelectedItem is not ListBoxItem { Tag: VocabularyMeaningDestination value }) return;
        if (value.Kind == VocabularyMeaningDestinationKind.Unclassified && !_expanded) _expanded = true;
        _destination = value; Refresh(); DestinationChanged?.Invoke(value);
    }

    private void Refresh()
    {
        bool restoreFocus = _list.IsKeyboardFocusWithin;
        _updating = true;
        try
        {
            _list.Items.Clear();
            foreach (var item in _items)
            {
                bool selected = _destination?.NodeId == item.Id;
                var names = new HashSet<string>(CanonicalStringComparer.Instance); var preview = new List<string>();
                foreach (var selection in item.Selections)
                    if (_entries.FirstOrDefault(entry => entry.Id == selection.EntryId)?.Expression is { } expression && names.Add(expression)) preview.Add(expression);
                string detail = preview.Count > 0 ? string.Join(" · ", preview) : item.Node.Definition;
                var top = new DockPanel { LastChildFill = true };
                var badge = VocabularySidebarVisuals.Badge(item.Selections.Count.ToString(), item.Selections.Count > 0, _scale); DockPanel.SetDock(badge, Dock.Right); top.Children.Add(badge);
                if (item.PendingCount > 0)
                {
                    var dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.DarkOrange, Margin = new Thickness(3, 0, 5, 0), ToolTip = _suggestedLabel, VerticalAlignment = VerticalAlignment.Center };
                    System.Windows.Automation.AutomationProperties.SetName(dot, _suggestedLabel); DockPanel.SetDock(dot, Dock.Right); top.Children.Add(dot);
                }
                var icon = VocabularySidebarVisuals.SemanticIcon(item.Node.Kind == R.VocabularyMeaningNodeKind.Concept, _scale);
                icon.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(icon, Dock.Left); top.Children.Add(icon);
                var text = InlineText(item.Title, 14.5 * _scale, detail, 12.5 * _scale, !_detailed && selected);

                top.Children.Add(text);
                var body = new StackPanel(); body.Children.Add(top);
                if (_detailed && detail.Length > 0) { var sub = VocabularySidebarVisuals.Text(detail, 13 * _scale, FontWeights.Normal, VocabularySidebarVisuals.Secondary); sub.Margin = new Thickness(16, 3, 0, 0); body.Children.Add(sub); }
                var row = Row(body, new(VocabularyMeaningDestinationKind.Node, item.Id), item.Title, _detailed ? 4 : 2.5); _list.Items.Add(row); if (selected) _list.SelectedItem = row;
            }
            if (_unclassified.Count > 0) AddUnclassified();
        }
        finally { _updating = false; }
        if (_notebookTheme) VocabularyNotebookTheme.Sidebar(this);
        if (restoreFocus) (_list.SelectedItem as ListBoxItem ?? (UIElement)_list).Focus();
    }

    private void AddUnclassified()
    {
        var header = new DockPanel();
        var toggle = VocabularySidebarVisuals.IconButton(_expanded ? "\uE70D" : "\uE76C", _otherLabel);
        toggle.Click += (_, e) => { _expanded = !_expanded; Refresh(); e.Handled = true; };
        DockPanel.SetDock(toggle, Dock.Left); header.Children.Add(toggle);
        var badge = VocabularySidebarVisuals.Badge(_unclassified.Count.ToString(), false, _scale); DockPanel.SetDock(badge, Dock.Right); header.Children.Add(badge);
        var icon = VocabularySidebarVisuals.Icon("\uE7B8", 11.5 * _scale, VocabularySidebarVisuals.Secondary); icon.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(icon, Dock.Left); header.Children.Add(icon);
        header.Children.Add(VocabularySidebarVisuals.Text(_otherLabel, 13.5 * _scale, FontWeights.Medium));
        var headerRow = Row(header, new(VocabularyMeaningDestinationKind.Unclassified), _otherLabel, 2.5);
        headerRow.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (headerRow.IsSelected && !_expanded) { _expanded = true; Refresh(); e.Handled = true; }
        };
        headerRow.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space or Key.Right) { if (!_expanded) { _expanded = true; Refresh(); } e.Handled = true; }
            else if (e.Key == Key.Left && _expanded) { _expanded = false; Refresh(); e.Handled = true; }
        };
        _list.Items.Add(headerRow);
        if (_destination?.Kind == VocabularyMeaningDestinationKind.Unclassified) _list.SelectedItem = headerRow;
        if (!_expanded) return;
        foreach (var selection in _unclassified)
        {
            var entry = _entries.FirstOrDefault(value => value.Id == selection.EntryId); var sense = entry?.Senses.FirstOrDefault(value => value.Id == selection.SenseId);
            if (entry is null || sense is null) continue;
            bool selected = _destination is { Kind: VocabularyMeaningDestinationKind.Sense, NodeId: null } && _destination.SenseId == sense.Id;
            string detail = _senseTitle(sense); var top = new DockPanel();
            var language = VocabularySidebarVisuals.Text(_languageLabel(entry.Language), 10.5 * _scale, FontWeights.Medium, VocabularySidebarVisuals.Secondary);
            DockPanel.SetDock(language, Dock.Right); top.Children.Add(language);
            var text = InlineText(entry.Expression, 13.5 * _scale, detail, 12 * _scale, !_detailed && selected);

            top.Children.Add(text); var body = new StackPanel { Margin = new Thickness(16, 0, 0, 0) }; body.Children.Add(top);
            if (_detailed && detail.Length > 0) { var sub = VocabularySidebarVisuals.Text(detail, 12.5 * _scale, FontWeights.Normal, VocabularySidebarVisuals.Secondary); sub.Margin = new Thickness(0, 2, 0, 0); body.Children.Add(sub); }
            var row = Row(body, new(VocabularyMeaningDestinationKind.Sense, SenseId: sense.Id), entry.Expression, 2.5); _list.Items.Add(row); if (selected) _list.SelectedItem = row;
        }
    }
    // Both columns receive finite space so WPF can trim long translated titles while reserving
    // room for the selected row's secondary preview at narrow sidebar widths.
    private static Grid InlineText(string title, double titleSize, string detail, double detailSize, bool showDetail)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var primary = VocabularySidebarVisuals.Text(title, titleSize, FontWeights.SemiBold); grid.Children.Add(primary);
        if (showDetail && detail.Length > 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var secondary = VocabularySidebarVisuals.Text(detail, detailSize, FontWeights.Normal, VocabularySidebarVisuals.Secondary);
            secondary.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(secondary, 1); grid.Children.Add(secondary);
        }
        return grid;
    }
    private static ListBoxItem Row(UIElement content, VocabularyMeaningDestination destination, string name, double padding)
    {
        var row = new ListBoxItem { Content = content, Tag = destination, Padding = new Thickness(7, padding, 7, padding), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(row, name); return row;
    }
}
