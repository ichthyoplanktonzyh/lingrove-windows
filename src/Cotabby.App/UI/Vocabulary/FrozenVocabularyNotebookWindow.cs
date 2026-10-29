using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using Microsoft.Win32;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Unactivated canonical notebook; presentation borrows the app-owned Store and reader.
/// Only Add/Import are connected. Other commands remain disabled until their shared owners exist.</summary>
internal sealed class FrozenVocabularyNotebookWindow : Window
{
    private sealed class Search : IVocabularyTextSearching
    { public bool Contains(string field, string term) => CultureInfo.CurrentCulture.CompareInfo.IndexOf(field, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0; }
    private sealed record LanguageChoice(string Label, LearningLanguage? Value) { public override string ToString() => Label; }
    private sealed record TagChoice(string Label, string Value) { public override string ToString() => Label; }
    private readonly IVocabularyEntryImportStore _store;
    private readonly IVocabularyWordListReader _reader;
    private readonly FrozenVocabularyEntryInputOwner _owner;
    private readonly FrozenVocabularyBrowserSession _session;
    private readonly TextBox _query = new() { Name = "frozenNotebookSearch", MinWidth = 200, ToolTip = "Search vocabulary" };
    private readonly ComboBox _language = new() { Name = "frozenNotebookLanguage", MinWidth = 130 }, _tag = new() { Name = "frozenNotebookTag", MinWidth = 130 };
    private readonly ComboBox _organization = new() { Name = "frozenNotebookOrganization", MinWidth = 125 };
    private readonly Button _add = new() { Name = "frozenNotebookAdd", Content = "Add entry", Padding = new Thickness(10, 5, 10, 5) }, _import = new() { Name = "frozenNotebookImport", Content = "Import…", Padding = new Thickness(10, 5, 10, 5) };
    private readonly ComboBox _sort = new() { Name = "frozenNotebookSort", MinWidth = 115 }, _density = new() { Name = "frozenNotebookDensity", MinWidth = 100 };
    private readonly Grid _content = new();
    private readonly Button _categoryToggle = new() { Name = "frozenToggleCategories", Content = "Categories", Padding = new Thickness(10, 5, 10, 5) };
    private bool _showCategories = true, _restoreEntryFocus;
    private readonly TreeView _entries = new() { Name = "frozenNotebookEntries", BorderThickness = new Thickness(0) };
    private readonly VocabularyMeaningSidebar _meanings;
    private readonly VocabularyCategorySidebar _categories;
    private readonly HashSet<Guid> _expanded = [];
    private readonly Func<string?> _pickFile;
    private bool _updating, _started, _retiring, _allowClose, _hideRequested;
    private readonly bool _hideOnClose;
    private readonly IVocabularyNotebookListPreferences? _listPreferences;
    private Window? _inputWindow;
    private TaskCompletionSource? _modalDone;
    private VocabularySelection? _pendingShow;
    private readonly HashSet<Task> _operations = [];
    internal FrozenVocabularyBrowserSession Session => _session;
    internal Task? LastOperation { get; private set; }
    internal FrozenVocabularyNotebookWindow(IVocabularyEntryImportStore store, IVocabularyWordListReader reader,
        IReadOnlyList<LearningLanguage>? preferredLanguages = null, Func<string?>? pickFile = null, IVocabularyNotebookListPreferences? listPreferences = null, bool hideOnClose = false)
    {
        _store = store; _reader = reader; _owner = new(store); _hideOnClose = hideOnClose; _listPreferences = listPreferences; var search = new Search();
        _session = new(Dispatcher, store, search) { PreferredLanguages = preferredLanguages ?? VocabularyMeaningLabelPolicy.PreferredLanguages(null, [CultureInfo.CurrentUICulture.Name]) };
        _session.Detailed = listPreferences?.Density == "detailed";
        _pickFile = pickFile ?? (() => { var dialog = new OpenFileDialog { Filter = "Word lists (*.csv;*.txt)|*.csv;*.txt", Multiselect = false }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; });
        Title = "Vocabulary notebook"; Width = 1100; Height = 720; MinWidth = 760; MinHeight = 460; Background = Brushes.White;
        var root = new DockPanel { Background = Brushes.White }; Content = root;
        var toolbar = new DockPanel { Margin = new Thickness(18, 14, 18, 12) };
        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { _categoryToggle, _add, _import, Disabled("Graph", "Graph view is not available in this notebook yet."), Disabled("Review", "Review is not available in this notebook yet."), Disabled("Export", "Export is not available in this notebook yet.") })
        { button.Margin = new Thickness(6, 0, 0, 0); commands.Children.Add(button); }
        DockPanel.SetDock(commands, Dock.Right); toolbar.Children.Add(commands);
        toolbar.Children.Add(new TextBlock { Text = "Vocabulary notebook", FontSize = 21, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var filters = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 18, 6) };
        _organization.Items.Add("By entry"); _organization.Items.Add("By meaning"); _organization.SelectedIndex = 0;
        _language.Items.Add(new LanguageChoice("All languages", null)); foreach (var value in LearningLanguages.All) _language.Items.Add(new LanguageChoice(value.Name(), value)); _language.SelectedIndex = 0;
        _tag.Items.Add(new TagChoice("All tags", "")); _tag.SelectedIndex = 0;
        _sort.Items.Add("Newest first"); _sort.Items.Add("Oldest first"); _sort.SelectedIndex = 0; _density.Items.Add("Compact"); _density.Items.Add("Detailed"); _density.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetName(_query, "Search vocabulary"); System.Windows.Automation.AutomationProperties.SetName(_language, "Language filter"); System.Windows.Automation.AutomationProperties.SetName(_tag, "Tag filter"); System.Windows.Automation.AutomationProperties.SetName(_organization, "Browse organization"); System.Windows.Automation.AutomationProperties.SetName(_sort, "Sort order"); System.Windows.Automation.AutomationProperties.SetName(_density, "List density");
        foreach (var control in new FrameworkElement[] { _organization, _query, _language, _tag, _sort, _density }) { control.Margin = new Thickness(0, 0, 8, 6); filters.Children.Add(control); }
        DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters); root.Children.Add(_content);
        ScrollViewer.SetHorizontalScrollBarVisibility(_entries, ScrollBarVisibility.Disabled);
        _meanings = new("Other expressions", "Suggested grouping", SenseTitle, LanguageLabel);
        _categories = new(new("Categories", "Search categories", "All meanings", "Uncategorized meanings", "Content meanings", "Communicative functions", "New category", "Manage categories", "Edit category"), search) { AllowEditing = false };
        _meanings.DestinationChanged += destination => { _session.Destination = destination; if (destination.SenseId is { } id) SelectSense(id, destination.NodeId); else _session.Refresh(); };
        _categories.SelectionChanged += value => { if (_updating) return; _session.Category = value; if (_session.Destination?.NodeId is { } id && !_session.MeaningItems.Any(x => x.Id == id)) _session.Destination = null; _session.Refresh(); };
        _query.TextChanged += (_, _) => { if (!_updating) { _session.Query = _query.Text; foreach (var e in _session.Visible) _expanded.Add(e.Id); _session.Refresh(); } };
        _language.SelectionChanged += (_, _) => { if (!_updating) { _session.Language = (_language.SelectedItem as LanguageChoice)?.Value; _session.Refresh(); } };
        _tag.SelectionChanged += (_, _) => { if (!_updating) { _session.Tag = (_tag.SelectedItem as TagChoice)?.Value ?? ""; _session.Refresh(); } };
        _sort.SelectionChanged += (_, _) => { if (!_updating) { _session.NewestFirst = _sort.SelectedIndex == 0; _session.Refresh(); } };
        _density.SelectionChanged += (_, _) => { if (!_updating) { _session.Detailed = _density.SelectedIndex == 1; if (_listPreferences is not null) _listPreferences.Density = _session.Detailed ? "detailed" : "compact"; _session.Refresh(); } };
        _organization.SelectionChanged += (_, _) => { if (!_updating) { ChangeOrganization(_organization.SelectedIndex == 1); } };
        _categoryToggle.Click += (_, _) => { _showCategories = !_showCategories; _session.Refresh(); };
        _add.Click += (_, _) => LastOperation = AddAsync(); _import.Click += (_, _) => LastOperation = ImportAsync();
        InputBindings.Add(new KeyBinding(new Relay(() => { if (_add.IsEnabled) LastOperation = AddAsync(); }), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => { if (_import.IsEnabled) LastOperation = ImportAsync(); }), Key.I, ModifierKeys.Control | ModifierKeys.Shift));
        _session.Changed += Present; Loaded += (_, _) => { if (!_started && !_retiring) { _started = true; LastOperation = Track(_session.LoadAsync()); } }; Closing += OnClosing; Closed += (_, _) => _session.Dispose(); Present();
    }
    internal void SetPreferredLanguages(IReadOnlyList<LearningLanguage> languages) { _session.PreferredLanguages = Array.AsReadOnly(languages.ToArray()); _session.Refresh(); }
    private static Button Disabled(string title, string tooltip) => new() { Content = title, ToolTip = tooltip, IsEnabled = false, Padding = new Thickness(10, 5, 10, 5) };
    private sealed class Relay(Action action) : ICommand { public event EventHandler? CanExecuteChanged { add { } remove { } } public bool CanExecute(object? p) => true; public void Execute(object? p) => action(); }
    internal void Reveal(VocabularySelection? selection)
    {
        Dispatcher.VerifyAccess(); if (_retiring || selection is null) return;
        if (_modalDone is not null) { _pendingShow = selection; return; }
        _expanded.Add(selection.Value.EntryId); LastOperation = Track(_session.RevealSelectionAsync(selection));
    }
    private async Task Track(Task operation)
    {
        _operations.Add(operation); try { await operation; } finally { _operations.Remove(operation); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_hideOnClose || _retiring) { e.Cancel = true; if (!_retiring) { if (_modalDone is null) Hide(); else _hideRequested = true; } }
    }
    internal async Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); if (_retiring) return; _retiring = true;
        _add.IsEnabled = _import.IsEnabled = false;
        if (_inputWindow is VocabularyEntryCreationWindow creation) await creation.RetireAsync();
        else if (_inputWindow is VocabularyWordListImportWindow import) await import.RetireAsync();
        if (_modalDone is { } modal) await modal.Task;
        await _session.RetireAsync();
        while (_operations.Count != 0) { var operations = _operations.ToArray(); await Task.WhenAll(operations); _operations.RemoveWhere(t => t.IsCompleted); }
        _allowClose = true; Close();
    }
    private async Task AddAsync()
    {
        if (_retiring || _modalDone is not null) return;
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var window = new VocabularyEntryCreationWindow(_owner, _session.Language ?? LearningLanguage.English) { Owner = this }; _inputWindow = window;
            try { window.ShowDialog(); } finally { await window.RetireAsync(); _inputWindow = null; }
            if (!_retiring && window.Result is { } result) { _expanded.Add(result.Selection?.EntryId ?? Guid.Empty); await _session.RevealAsync(result); }
            if (!_retiring && _pendingShow is { } selection) { _pendingShow = null; _expanded.Add(selection.EntryId); await _session.RevealSelectionAsync(selection); }
        }
        finally { _inputWindow = null; _modalDone = null; if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } done.TrySetResult(); }
    }
    private async Task ImportAsync()
    {
        if (_retiring || _modalDone is not null) return;
        var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            string? path = _pickFile();
            if (!_retiring && path is not null)
            {
                var window = new VocabularyWordListImportWindow(_owner, _reader, path, _session.Language ?? LearningLanguage.English) { Owner = this }; _inputWindow = window;
                try { window.ShowDialog(); } finally { await window.RetireAsync(); _inputWindow = null; }
                if (!_retiring && window.Result is { } result) { _expanded.Add(result.Selection?.EntryId ?? Guid.Empty); await _session.RevealAsync(result); }
            }
            if (!_retiring && _pendingShow is { } selection) { _pendingShow = null; _expanded.Add(selection.EntryId); await _session.RevealSelectionAsync(selection); }
        }
        finally { _inputWindow = null; _modalDone = null; if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); } done.TrySetResult(); }
    }
    private void ChangeOrganization(bool meaning)
    {
        if (meaning && _session.Destination is null && _session.Selection is { } selected && _session.Snapshot is { } snapshot)
        {
            var nodes = snapshot.Document.Semantics.Mappings.Where(m => m.SenseId == selected.SenseId).Select(m => m.NodeId).Distinct().ToArray();
            if (nodes.Length == 1) _session.Destination = new(VocabularyMeaningDestinationKind.Node, nodes[0]);
        }
        if (!meaning && _session.Destination?.SenseId is { } sense) SelectSense(sense, null, notify: false);
        _session.MeaningMode = meaning; _session.Refresh();
    }
    private void SelectSense(Guid id, Guid? node, bool notify = true)
    {
        var entry = _session.Entries.FirstOrDefault(e => e.Senses.Any(s => s.Id == id)); if (entry is null) return;
        _session.Selection = new(entry.Id, id); _expanded.Add(entry.Id);
        if (_session.MeaningMode) _session.Destination = new(VocabularyMeaningDestinationKind.Sense, node, id);
        if (notify) _session.Refresh();
    }
    private void Present()
    {
        if (!_session.Active) return; _restoreEntryFocus = _entries.IsKeyboardFocusWithin; _updating = true;
        try
        {
            _query.Text = _session.Query; _organization.SelectedIndex = _session.MeaningMode ? 1 : 0; _sort.SelectedIndex = _session.NewestFirst ? 0 : 1; _density.SelectedIndex = _session.Detailed ? 1 : 0;
            _language.SelectedItem = _language.Items.Cast<LanguageChoice>().First(x => x.Value == _session.Language);
            var tags = _session.Entries.SelectMany(e => e.Senses).SelectMany(s => s.Tags).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
            if (_session.Tag.Length > 0 && !tags.Contains(_session.Tag)) _session.Tag = "";
            _tag.Items.Clear(); _tag.Items.Add(new TagChoice("All tags", "")); foreach (var tag in tags) _tag.Items.Add(new TagChoice(tag, tag)); _tag.SelectedItem = _tag.Items.Cast<TagChoice>().First(x => x.Value == _session.Tag);
            _add.IsEnabled = _import.IsEnabled = !_session.Loading && _session.Failure is null; _categoryToggle.Visibility = _session.MeaningMode ? Visibility.Visible : Visibility.Collapsed;
            _content.Children.Clear(); _content.ColumnDefinitions.Clear();
            if (_session.Loading) { _content.Children.Add(State("Loading vocabulary…")); return; }
            if (_session.Failure is { } failure) { _content.Children.Add(State("Could not load vocabulary\n" + failure)); return; }
            if (!_session.MeaningMode && _session.Entries.Count == 0) { _content.Children.Add(Empty()); return; }
            if (_session.MeaningMode) BuildMeaningLayout(); else BuildEntryLayout();
        }
        finally { _updating = false; }
    }
    private FrameworkElement Empty()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "No saved expressions", FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Add an entry yourself or import a CSV or TXT word list.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 9, 0, 16), HorizontalAlignment = HorizontalAlignment.Center });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var add = new Button { Name = "frozenEmptyAdd", Content = "Add entry", Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; add.Click += (_, _) => LastOperation = AddAsync();
        var import = new Button { Name = "frozenEmptyImport", Content = "Import word list", Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; import.Click += (_, _) => LastOperation = ImportAsync();
        buttons.Children.Add(add); buttons.Children.Add(import); panel.Children.Add(buttons); return panel;
    }
    private static FrameworkElement State(string text) => new TextBlock { Text = text, Name = "frozenNotebookState", Foreground = Brushes.DimGray, FontSize = 16, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private void Columns(bool categories)
    {
        if (categories) { _content.ColumnDefinitions.Add(new() { Width = new GridLength(210), MinWidth = 160 }); _content.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); }
        _content.ColumnDefinitions.Add(new() { Width = new GridLength(300), MinWidth = 200 }); _content.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); _content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
        for (int i = categories ? 1 : 1; i < _content.ColumnDefinitions.Count - 1; i += 2) { var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Gainsboro }; Grid.SetColumn(splitter, i); _content.Children.Add(splitter); }
    }
    private void BuildEntryLayout()
    {
        Columns(false); _entries.Items.Clear();
        foreach (var entry in _session.Visible)
        {
            if (entry.Senses.Count == 1) _entries.Items.Add(SenseRow(entry, entry.Senses[0], true));
            else
            {
                var parent = new TreeViewItem { Header = EntryLabel(entry.Expression + "  ·  " + entry.Senses.Count), IsExpanded = _expanded.Contains(entry.Id), FontWeight = FontWeights.SemiBold };
                parent.Expanded += (_, e) => { if (e.OriginalSource == parent) _expanded.Add(entry.Id); }; parent.Collapsed += (_, e) => { if (e.OriginalSource == parent) _expanded.Remove(entry.Id); };
                foreach (var sense in entry.Senses) parent.Items.Add(SenseRow(entry, sense, false));
                parent.PreviewMouseLeftButtonDown += (_, e) => { if (e.OriginalSource is DependencyObject origin && FindAncestor<TreeViewItem>(origin) == parent) { ActivateHeadword(entry); e.Handled = true; } };
                parent.Selected += (_, e) => { if (!_updating && e.OriginalSource == parent) { ActivateHeadword(entry); e.Handled = true; } };
                _entries.Items.Add(parent);
            }
        }
        if (_session.Visible.Count == 0) { var empty = State("No matching expressions"); Grid.SetColumn(empty, 0); _content.Children.Add(empty); }
        else { Grid.SetColumn(_entries, 0); _content.Children.Add(_entries); if (_restoreEntryFocus) { _entries.UpdateLayout(); if (_entries.SelectedItem is TreeViewItem selected) selected.Focus(); else _entries.Focus(); } }
        var detail = Detail(); Grid.SetColumn(detail, 2); _content.Children.Add(detail);
    }
    private void ActivateHeadword(F.VocabularyEntry entry)
    {
        bool selected = _session.Selection?.EntryId == entry.Id;
        if (selected && _expanded.Contains(entry.Id)) _expanded.Remove(entry.Id);
        else { _expanded.Add(entry.Id); if (!selected) _session.Selection = new(entry.Id, entry.Senses[0].Id); }
        _session.Refresh();
    }
    private TreeViewItem SenseRow(F.VocabularyEntry entry, F.VocabularySense sense, bool single)
    {
        string label = single ? VocabularyMeaningLabelPolicy.ExpressionTitle(entry, VocabularyMeaningLabelPolicy.HasMultipleLanguages(_session.Visible), LanguageLabel) : SenseTitle(sense);
        if (single && (_session.Detailed || _session.Selection?.SenseId == sense.Id) && SenseTitle(sense).Length != 0) label += "  ·  " + SenseTitle(sense);
        if (_session.Detailed && sense.Examples.FirstOrDefault() is { } example) label += "\n" + example.Text;
        var row = new TreeViewItem { Header = EntryLabel(label), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = new VocabularySelection(entry.Id, sense.Id), Padding = new Thickness(8, 5, 8, 5), IsSelected = _session.Selection?.SenseId == sense.Id };
        row.Selected += (_, e) => { if (!_updating && e.OriginalSource == row) { SelectSense(sense.Id, null); e.Handled = true; } }; return row;
    }
    private TextBlock EntryLabel(string text)
    {
        var label = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = _session.Detailed ? 44 : 22 };
        label.SetBinding(FrameworkElement.MaxWidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = _entries, Converter = new SidebarWidth() }); return label;
    }
    private sealed class SidebarWidth : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width ? Math.Max(20, width - 55) : 20;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private void BuildMeaningLayout()
    {
        Columns(_showCategories); var doc = _session.Snapshot!.Document;
        if (_showCategories) { _categories.Present(doc.Classification, doc.Semantics.Nodes, _session.PreferredLanguages, _session.Category); Grid.SetColumn(_categories, 0); _content.Children.Add(_categories); }
        _meanings.Present(_session.MeaningItems, _session.Scoped, _session.Unclassified, _session.Destination, detailed: _session.Detailed); Grid.SetColumn(_meanings, _showCategories ? 2 : 0); _content.Children.Add(_meanings);
        var detail = Detail(); Grid.SetColumn(detail, _showCategories ? 4 : 2); _content.Children.Add(detail);
    }
    private FrameworkElement Detail()
    {
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        if (_session.MeaningMode && _session.Destination is { Kind: VocabularyMeaningDestinationKind.Node, NodeId: { } nodeId } && _session.Snapshot!.Document.Semantics.Nodes.FirstOrDefault(n => n.Id == nodeId) is { } node)
            MeaningDetail(panel, node);
        else if (_session.MeaningMode && _session.Destination is { Kind: VocabularyMeaningDestinationKind.Unclassified })
        { Heading(panel, "Other expressions"); foreach (var selection in _session.Unclassified) AddSenseCard(panel, selection); }
        else if ((!_session.MeaningMode || _session.Destination?.Kind == VocabularyMeaningDestinationKind.Sense) && _session.Selection is { } selected && _session.Entries.FirstOrDefault(e => e.Id == selected.EntryId) is { } entry && entry.Senses.FirstOrDefault(s => s.Id == selected.SenseId) is { } sense)
        { if (_session.MeaningMode) Breadcrumb(panel); SenseDetail(panel, entry, sense); }
        else if (_session.MeaningMode)
        {
            Heading(panel, "Browse by meaning"); panel.Children.Add(Disabled("New meaning", "Meaning editing is not available in this notebook yet."));
            if (_session.Unclassified.Count > 0) { var button = new Button { Content = $"Other expressions ({_session.Unclassified.Count})", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) }; button.Click += (_, _) => { _session.Destination = new(VocabularyMeaningDestinationKind.Unclassified); _session.Refresh(); }; panel.Children.Add(button); }
        }
        else panel.Children.Add(State("Select an expression"));
        return new ScrollViewer { Name = "frozenNotebookDetail", VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }
    private void Breadcrumb(StackPanel panel)
    {
        Guid? nodeId = _session.Destination?.NodeId; var node = _session.Snapshot!.Document.Semantics.Nodes.FirstOrDefault(n => n.Id == nodeId);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        var back = new Button { Name = "frozenMeaningBack", Content = "‹  " + (node is null ? "Other expressions" : VocabularyMeaningLabelPolicy.Title(node, _session.PreferredLanguages)), Padding = new Thickness(8, 4, 8, 4) };
        back.Click += (_, _) => { _session.Destination = node is null ? new(VocabularyMeaningDestinationKind.Unclassified) : new(VocabularyMeaningDestinationKind.Node, node.Id); _session.Refresh(); }; bar.Children.Add(back);
        if (node is not null)
        {
            var ids = _session.Snapshot.Document.Semantics.Mappings.Where(m => m.NodeId == node.Id).Select(m => m.SenseId).ToHashSet();
            var siblings = _session.Scoped.SelectMany(e => e.Senses.Where(s => ids.Contains(s.Id)).Select(s => (entry: e, sense: s))).ToArray();
            if (siblings.Length > 1) foreach (var sibling in siblings)
            {
                var button = new Button { Content = sibling.entry.Expression, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), IsEnabled = sibling.sense.Id != _session.Selection?.SenseId };
                button.Click += (_, _) => SelectSense(sibling.sense.Id, node.Id); bar.Children.Add(button);
            }
        }
        panel.Children.Add(bar);
    }
    private void SenseDetail(StackPanel panel, F.VocabularyEntry entry, F.VocabularySense sense)
    {
        Heading(panel, entry.Expression); panel.Children.Add(Caption(LanguageLabel(entry.Language) + "  ·  " + entry.Kind));
        if (SenseTitle(sense).Length > 0) panel.Children.Add(Paragraph(SenseTitle(sense), 18));
                foreach (var meaning in sense.StandaloneMeanings.Concat(sense.AlternateMeanings)) if (meaning.Text.Length > 0) panel.Children.Add(Caption(meaning.Text + (meaning.Language is { } language ? "  ·  " + language.Name() : "")));
        if (sense.Content is { Count: > 0 })
        {
            Section(panel, "Explanation");
            foreach (var block in sense.Content)
                if (block.Content is VocabularyBlockContent.Text text) panel.Children.Add(Paragraph(text.Value.Text));
                else if (block.Content is VocabularyBlockContent.Image image) panel.Children.Add(Caption("Image unavailable" + (image.Caption.Length > 0 ? "  ·  " + image.Caption : "")));
        }
        if (sense.Note.Length > 0) { Section(panel, "Notes"); panel.Children.Add(Paragraph(sense.Note)); }
        if (sense.Tags.Count > 0) panel.Children.Add(Caption(string.Join("  ·  ", sense.Tags)));
        if (sense.Examples.Count > 0) { Section(panel, "Examples"); foreach (var example in sense.Examples) { panel.Children.Add(Paragraph(example.Text)); if (example.Translation.Length > 0) panel.Children.Add(Caption(example.Translation)); foreach (var translation in example.TranslationVariants) panel.Children.Add(Caption(translation.Text)); } }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        foreach (string text in new[] { "Edit", "Delete", "Review", "Export" }) { var button = Disabled(text, "This action is not available in this notebook yet."); button.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(button); } panel.Children.Add(actions);
    }
    private void MeaningDetail(StackPanel panel, F.VocabularyMeaningNode node)
    {
        var doc = _session.Snapshot!.Document; Heading(panel, VocabularyMeaningLabelPolicy.Title(node, _session.PreferredLanguages)); panel.Children.Add(Caption(node.Kind.ToString()));
        if (node.Definition.Length > 0) panel.Children.Add(Paragraph(node.Definition, 18)); if (node.ScopeNote.Length > 0) panel.Children.Add(Caption(node.ScopeNote));
        var labels = node.Labels.Select(l => l.Text).Where(x => x != VocabularyMeaningLabelPolicy.Title(node, _session.PreferredLanguages)).Distinct().Take(4).ToArray(); if (labels.Length > 0) panel.Children.Add(Caption(string.Join("  ·  ", labels)));
                var categoryIds = doc.Classification.Memberships.Where(m => m.NodeId == node.Id && m.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(m => m.CategoryId).ToHashSet();
        var categoryNames = doc.Classification.Categories.Where(c => categoryIds.Contains(c.Id) && !c.IsRetired && !doc.Classification.Overrides.Any(o => o.CategoryId == c.Id && o.IsHidden))
            .Select(c => R.VocabularyClassificationPolicy.Title(c, doc.Classification, _session.PreferredLanguages)).ToArray();
        if (categoryNames.Length > 0) { Section(panel, "Categories"); panel.Children.Add(Caption(string.Join("  ·  ", categoryNames))); }
        Section(panel, "My expressions"); var ids = doc.Semantics.Mappings.Where(m => m.NodeId == node.Id).Select(m => m.SenseId).ToHashSet();
        var matches = _session.Scoped.SelectMany(e => e.Senses.Where(s => ids.Contains(s.Id)).Select(s => new VocabularySelection(e.Id, s.Id))).ToArray();
        if (matches.Length == 0) panel.Children.Add(Caption("No matching expressions")); else foreach (var selection in matches) AddSenseCard(panel, selection);
        var relations = VocabularyMeaningBrowserProjection.ConfirmedRelations(doc.Semantics).Where(r => r.SourceId == node.Id || r.TargetId == node.Id).ToArray();
        if (relations.Length > 0) { Section(panel, "Related meanings"); foreach (var relation in relations) { var target = doc.Semantics.Nodes.FirstOrDefault(n => n.Id == (relation.SourceId == node.Id ? relation.TargetId : relation.SourceId)); if (target is null) continue; var button = new Button { Content = relation.Kind + "  ·  " + VocabularyMeaningLabelPolicy.Title(target, _session.PreferredLanguages), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(10) }; button.Click += (_, _) => { _session.Category = "all"; _session.Destination = new(VocabularyMeaningDestinationKind.Node, target.Id); _session.Refresh(); }; panel.Children.Add(button); } }
        var cueIds = doc.Semantics.CueTargets.Where(x => x.NodeId == node.Id && x.Provenance.Status == R.VocabularySemanticStatus.Confirmed).Select(x => x.CueId).ToHashSet();
        var cues = doc.Semantics.Cues.Where(x => cueIds.Contains(x.Id)).Select(x => x.Text).ToArray();
        if (node.Provenance.Source.Length > 0 || node.Roles.Count > 0 || cues.Length > 0) { Section(panel, "Details"); if (cues.Length > 0) panel.Children.Add(Caption(string.Join(" · ", cues))); foreach (var role in node.Roles) panel.Children.Add(Caption(role.Name + "  " + role.Description)); if (node.Provenance.Source.Length > 0) panel.Children.Add(Caption(node.Provenance.Source)); }
        panel.Children.Add(Disabled("Edit meaning", "Meaning editing is not available in this notebook yet."));
    }
    private void AddSenseCard(StackPanel panel, VocabularySelection selection)
    {
        var entry = _session.Entries.FirstOrDefault(e => e.Id == selection.EntryId); var sense = entry?.Senses.FirstOrDefault(s => s.Id == selection.SenseId); if (entry is null || sense is null) return;
        var button = new Button { Content = entry.Expression + "  ·  " + SenseTitle(sense) + "  ·  " + LanguageLabel(entry.Language), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(12), Tag = selection };
                button.Click += (_, _) => SelectSense(sense.Id, _session.Destination?.NodeId); panel.Children.Add(button);
        if (_session.Destination?.NodeId is { } node && _session.Snapshot is { } snapshot)
        {
            foreach (var mapping in snapshot.Document.Semantics.Mappings.Where(m => m.NodeId == node && m.SenseId == sense.Id))
            {
                if (mapping.Provenance.Status == R.VocabularySemanticStatus.Proposed) panel.Children.Add(Caption("Suggested grouping"));
                if (mapping.PatternId is { } id && snapshot.Document.Semantics.Patterns.FirstOrDefault(p => p.Id == id) is { } pattern)
                { panel.Children.Add(Caption(pattern.Template)); if (pattern.Note.Length > 0) panel.Children.Add(Caption(pattern.Note)); }
                if (mapping.Nuance.Length > 0) panel.Children.Add(Caption(mapping.Nuance));
                if (mapping.Conditions.Length > 0) panel.Children.Add(Caption(mapping.Conditions));
                var example = sense.Examples.FirstOrDefault(e => mapping.EvidenceIds.Contains(e.Id));
                if (example is not null) { panel.Children.Add(Paragraph(example.Text)); if (example.Translation.Length > 0) panel.Children.Add(Caption(example.Translation)); }
                if (mapping.Provenance.Source.Length > 0) panel.Children.Add(Caption(mapping.Provenance.Source));
            }
        }
    }
    private static string SenseTitle(F.VocabularySense sense)
    {
        if (sense.Meaning.Length > 0) return sense.Meaning;
        foreach (var block in sense.Content ?? []) if (block.Content is VocabularyBlockContent.Text text && !string.IsNullOrWhiteSpace(text.Value.Text)) return text.Value.Text;
        if (sense.Examples.FirstOrDefault(e => e.Meaning.Length > 0) is { } example) return example.Meaning;
        return (sense.Content ?? []).Any(b => b.Content is VocabularyBlockContent.Image) ? "Image explanation" : "Meaning pending";
    }
    private static string LanguageLabel(LearningLanguage? language) => language?.Name() ?? "Unspecified";
    private static void Heading(Panel p, string value) => p.Children.Add(new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
    private static void Section(Panel p, string value) => p.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
    private static TextBlock Caption(string value) => new() { Text = value, FontSize = 13, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) };
    private static TextBox Paragraph(string value, double size = 15) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 6) };
    private static T? FindAncestor<T>(DependencyObject value) where T : DependencyObject { for (var p = value; p is not null; p = VisualTreeHelper.GetParent(p)) if (p is T found) return found; return null; }
}
