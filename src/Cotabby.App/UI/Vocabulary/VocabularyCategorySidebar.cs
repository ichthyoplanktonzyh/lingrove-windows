using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

public sealed record VocabularyCategorySidebarLabels(string Categories, string SearchCategories, string AllMeanings,
    string UncategorizedMeanings, string ContentMeanings, string CommunicativeFunctions, string NewCategory,
    string ManageCategories, string EditCategory);

/// <summary>Category filter and disclosure state over supplied immutable snapshots. Edit/create/manage
/// callbacks express intent to the real notebook owner; this control owns no Store or persistence.</summary>
public sealed class VocabularyCategorySidebar : UserControl
{
    private readonly VocabularyCategorySidebarLabels _labels;
    private readonly IVocabularyTextSearching _search;
    private readonly ListBox _list = new();
    private readonly TextBox _query = new();
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private R.VocabularyClassificationIndex _index = new();
    private IReadOnlyList<F.VocabularyMeaningNode> _nodes = [];
    private IReadOnlyList<LearningLanguage> _languages = [];
    private string _selection = "all";
    private bool _updating;
    private double _scale = 1;
    public event Action<string>? SelectionChanged;
    public event Action<Guid?>? CreateRequested;
    public event Action? ManageRequested;
    public event Action<Guid>? EditRequested;

    public VocabularyCategorySidebar(VocabularyCategorySidebarLabels labels, IVocabularyTextSearching search)
    {
        _labels = labels; _search = search;
        var root = new DockPanel { Background = SystemColors.WindowBrush };
        var heading = new DockPanel { Margin = new Thickness(12) };
        var manage = VocabularySidebarVisuals.IconButton("\uE712", labels.ManageCategories);
        VocabularySidebarVisuals.Automation(manage, "vocabularyManageCategories", labels.ManageCategories);
        manage.Click += (_, _) => ManageRequested?.Invoke(); DockPanel.SetDock(manage, Dock.Right); heading.Children.Add(manage);
        var create = VocabularySidebarVisuals.IconButton("\uE710", labels.NewCategory);
        VocabularySidebarVisuals.Automation(create, "vocabularyNewCategory", labels.NewCategory);
        create.Click += (_, _) => CreateRequested?.Invoke(Guid.TryParse(_selection, out var parent) ? parent : null); DockPanel.SetDock(create, Dock.Right); heading.Children.Add(create);
        heading.Children.Add(VocabularySidebarVisuals.Text(labels.Categories, 14, FontWeights.SemiBold));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var searchBox = new Grid { Margin = new Thickness(10, 0, 10, 8) };
        _query.ToolTip = labels.SearchCategories;
        VocabularySidebarVisuals.Automation(_query, "vocabularyCategorySearch", labels.SearchCategories);
        var placeholder = VocabularySidebarVisuals.Text(labels.SearchCategories, 12, FontWeights.Normal, VocabularySidebarVisuals.Secondary);
        placeholder.Margin = new Thickness(5, 0, 0, 0); placeholder.IsHitTestVisible = false;
        searchBox.Children.Add(_query); searchBox.Children.Add(placeholder);
        _query.TextChanged += (_, _) => { placeholder.Visibility = _query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
        DockPanel.SetDock(searchBox, Dock.Top); root.Children.Add(searchBox);
        _list.BorderThickness = new Thickness(0); _list.Background = Brushes.Transparent;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        VocabularySidebarVisuals.Automation(_list, "vocabularyCategoryList", labels.Categories);
        _list.SelectionChanged += OnSelectionChanged; _list.PreviewKeyDown += OnListKeyDown; root.Children.Add(_list); Content = root;
    }

    public void Present(R.VocabularyClassificationIndex index, IReadOnlyList<F.VocabularyMeaningNode> nodes,
        IReadOnlyList<LearningLanguage> languages, string selection, double textScale = 1)
    {
        _index = index; _nodes = Array.AsReadOnly(nodes.ToArray()); _languages = Array.AsReadOnly(languages.ToArray()); _selection = selection; _scale = textScale;
        if (Guid.TryParse(selection, out var id))
        {
            if (!index.Categories.Any(category => category.Id == id && !category.IsRetired)
                || index.Overrides.Any(value => value.CategoryId == id && value.IsHidden))
            { _selection = "all"; SelectionChanged?.Invoke(_selection); }
            else Reveal(id);
        }
        Refresh();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _list.SelectedItem is not ListBoxItem { Tag: string value }) return;
        _selection = value; SelectionChanged?.Invoke(value);
    }

    private void Refresh()
    {
        bool restoreFocus = _list.IsKeyboardFocusWithin;
        _updating = true;
        try
        {
            _list.Items.Clear(); AddFixed(_labels.AllMeanings, "all", "\uE80A"); AddFixed(_labels.UncategorizedMeanings, "uncategorized", "\uE7B8");
            var rows = VocabularyClassificationProjection.Rows(_index, _nodes, _languages, _expanded, _search, _query.Text);
            foreach (var axis in new[] { R.VocabularyCategoryAxis.Content, R.VocabularyCategoryAxis.Function })
            {
                var title = axis == R.VocabularyCategoryAxis.Content ? _labels.ContentMeanings : _labels.CommunicativeFunctions;
                var heading = new ListBoxItem { Content = VocabularySidebarVisuals.Text(title, 11 * _scale, FontWeights.SemiBold, VocabularySidebarVisuals.Secondary),
                    IsEnabled = false, Padding = new Thickness(8, 8, 8, 3) }; _list.Items.Add(heading);
                foreach (var row in rows.Where(value => value.Category.Axis == axis)) AddCategory(row);
            }
        }
        finally { _updating = false; }
        if (restoreFocus) (_list.SelectedItem as ListBoxItem ?? (UIElement)_list).Focus();
    }

    private void AddFixed(string title, string key, string glyph)
    {
        var panel = new DockPanel(); var icon = VocabularySidebarVisuals.Icon(glyph, 12 * _scale, VocabularySidebarVisuals.Secondary);
        icon.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(icon, Dock.Left); panel.Children.Add(icon);
        panel.Children.Add(VocabularySidebarVisuals.Text(title, 12.5 * _scale, FontWeights.Normal));
        var row = MakeRow(panel, key, title); _list.Items.Add(row); if (_selection == key) _list.SelectedItem = row;
    }

    private void AddCategory(VocabularyClassificationProjection.Row value)
    {
        var panel = new DockPanel { Margin = new Thickness(Math.Min(value.Depth, 8) * 12, 0, 0, 0) };
        var toggle = VocabularySidebarVisuals.IconButton(_expanded.Contains(value.Id) || _query.Text.Length != 0 ? "\uE70D" : "\uE76C", value.Title);
        toggle.Width = 12; toggle.Height = 18; toggle.MinWidth = 12; toggle.MinHeight = 18; toggle.Padding = new Thickness(0);
        toggle.Visibility = value.HasChildren ? Visibility.Visible : Visibility.Hidden;
        toggle.Click += (_, e) => { if (!_expanded.Add(value.Id)) _expanded.Remove(value.Id); Refresh(); e.Handled = true; };
        DockPanel.SetDock(toggle, Dock.Left); panel.Children.Add(toggle);
        if (value.MeaningCount > 0)
        {
            var count = VocabularySidebarVisuals.Text(value.MeaningCount.ToString(), 12.5 * _scale, FontWeights.Normal, VocabularySidebarVisuals.Secondary);
            DockPanel.SetDock(count, Dock.Right); panel.Children.Add(count);
        }
        var text = VocabularySidebarVisuals.Text(value.Title, 12.5 * _scale, FontWeights.Normal); text.Margin = new Thickness(5, 0, 2, 0); panel.Children.Add(text);
        string key = value.Category.Id.ToString("D").ToUpperInvariant(); var row = MakeRow(panel, key, value.Title); row.DataContext = value;
        var menu = new ContextMenu(); var edit = new MenuItem { Header = _labels.EditCategory };
        edit.Click += (_, _) => EditRequested?.Invoke(value.Category.Id); menu.Items.Add(edit); row.ContextMenu = menu;
        _list.Items.Add(row); if (_selection.Equals(key, StringComparison.OrdinalIgnoreCase) && _list.SelectedItem is null) _list.SelectedItem = row;
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (_list.SelectedItem is not ListBoxItem { DataContext: VocabularyClassificationProjection.Row row } || !row.HasChildren) return;
        if (e.Key == Key.Right && _expanded.Add(row.Id)) { Refresh(); e.Handled = true; }
        else if (e.Key == Key.Left && _expanded.Remove(row.Id)) { Refresh(); e.Handled = true; }
    }
    private static ListBoxItem MakeRow(UIElement content, string key, string title)
    {
        var row = new ListBoxItem { Content = content, Tag = key, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7, 2.5, 7, 2.5) };
        System.Windows.Automation.AutomationProperties.SetName(row, title); return row;
    }

    private void Reveal(Guid id)
    {
        var hidden = _index.Overrides.Where(value => value.IsHidden).Select(value => value.CategoryId).ToHashSet();
        var visible = _index.Categories.Where(category => !category.IsRetired && !hidden.Contains(category.Id)).Select(category => category.Id).ToHashSet();
        var parents = _index.Parents.Where(edge => visible.Contains(edge.ParentId)).ToLookup(edge => edge.CategoryId);
        var path = new List<Guid> { id }; var seen = new HashSet<Guid> { id };
        while (parents[path[^1]].FirstOrDefault() is { } edge && seen.Add(edge.ParentId)) path.Add(edge.ParentId);
        string key = "";
        foreach (var ancestor in path.AsEnumerable().Reverse().SkipLast(1)) { key += "/" + ancestor.ToString("D").ToUpperInvariant(); _expanded.Add(key); }
    }
}
