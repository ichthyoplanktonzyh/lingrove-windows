using Cotabby.App.Localization;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;
using VocabularyEntry = Cotabby.Core.Vocabulary.Frozen.VocabularyEntry;

namespace Cotabby.App.UI.Vocabulary;

// Windows notebook chrome currently has an English interface; study-language settings are separate.
internal static class VocabularyCategoryUiLanguage { internal const string Identifier = "en"; }

/// <summary>Child selection stays detached; nested category creation edits the same local draft.</summary>
internal sealed class FrozenVocabularyDraftPickerWindow : Window
{
    private sealed class Search : IVocabularyTextSearching { public bool Contains(string text, string term) => CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0; }
    private readonly ICanonicalVocabularySemanticStore _store;
    private readonly IReadOnlyList<LearningLanguage> _languages;
    private readonly IReadOnlyList<VocabularyEntry>? _entries;
    private readonly HashSet<Guid> _selected;
    private readonly Func<R.VocabularyClassificationIndex, IReadOnlySet<Guid>, Task>? _commit;
    private readonly bool _allowsMultiple;
    private readonly HashSet<string> _expanded = [];
    private readonly TextBox _query = new() { Name = "meaningDraftPickerSearch" };
    private readonly StackPanel _items = new();
    private readonly WrapPanel _chips = new() { Name = "meaningDraftSelectionChips", Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox _axis = new() { Name = "meaningDraftPickerAxis", ItemsSource = Enum.GetValues<R.VocabularyCategoryAxis>() };
    private FrozenVocabularyCategoryEditorWindow? _child;
    private TaskCompletionSource? _childDone, _retirement;
    private bool _retiring, _saving;
    private Task? _saveTask;
    private readonly TextBlock _failure = VocabularyNotebookTheme.Brush(new TextBlock { Name = "meaningDraftPickerError", TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    internal R.VocabularyClassificationIndex Classification { get; private set; }
    internal IReadOnlySet<Guid> Selection => _selected;
    internal bool Accepted { get; private set; }
    internal FrozenVocabularyDraftPickerWindow(ICanonicalVocabularySemanticStore store, R.VocabularyClassificationIndex classification, IReadOnlyList<LearningLanguage> languages, IReadOnlySet<Guid> selected, R.VocabularyCategoryAxis axis, IReadOnlyList<VocabularyEntry>? entries = null, Func<R.VocabularyClassificationIndex, IReadOnlySet<Guid>, Task>? commit = null, bool allowsMultiple = true)
    {
        _store = store; _commit = commit; _allowsMultiple = allowsMultiple; Classification = classification; _languages = languages; _selected = selected.ToHashSet(); _entries = entries; if (entries is null) _expanded.UnionWith(VocabularyClassificationProjection.ExpansionPaths(_selected, classification, true)); Title = entries is null ? InterfaceText.Get("Categories", "Vocabulary") : InterfaceText.Get("Add from vocabulary", "Vocabulary"); Width = 600; Height = 560; MinWidth = 500; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; VocabularyNotebookTheme.Follow(this);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root; var header = VocabularyInputControls.Heading(Title); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var cancel = new Button { Name = "meaningDraftPickerCancel", Content = InterfaceText.Get("Cancel"), IsCancel = true, MinWidth = 90 }; var save = new Button { Name = "meaningDraftPickerSave", Content = InterfaceText.Get("Save"), IsDefault = true, MinWidth = 90 }; cancel.Click += (_, _) => Close(); save.Click += (_, _) => _saveTask = SaveAsync(); var footer = VocabularyInputControls.Footer(cancel, save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); DockPanel.SetDock(_failure, Dock.Bottom); root.Children.Add(_failure);
        if (entries is null) { DockPanel.SetDock(_chips, Dock.Top); root.Children.Add(_chips); }
        VocabularyNotebookTheme.Input(_query); _query.Tag = entries is null ? "Search categories" : "Search vocabulary"; _query.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(_query, Dock.Top); root.Children.Add(_query);
        if (entries is null) { var toolbar = new DockPanel(); var create = new Button { Name = "meaningDraftNewCategory", Content = InterfaceText.Get("New category"), Margin = new Thickness(8, 0, 0, 8) }; VocabularyNotebookTheme.Button(create); create.Click += (_, _) => _ = CreateAsync(); DockPanel.SetDock(create, Dock.Right); toolbar.Children.Add(create); VocabularyNotebookTheme.Combo(_axis); _axis.SelectedItem = axis; _axis.SelectionChanged += (_, _) => Render(); toolbar.Children.Add(_axis); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar); }
        root.Children.Add(new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); _query.TextChanged += (_, _) => Render(); Closing += (_, e) => { if (_childDone is not null || _saving) e.Cancel = true; }; Render();
    }
    private void Render()
    {
        if (_retiring) return; _items.Children.Clear(); _chips.Children.Clear();
        if (_entries is not null)
        {
            foreach (var entry in _entries.Where(e => VocabularyFrozenEntrySearch.Matches(e, _query.Text, new Search()))) { _items.Children.Add(VocabularyInputControls.Heading(entry.Expression)); foreach (var sense in entry.Senses) AddChoice(sense.Meaning.Length == 0 ? "Meaning pending" : sense.Meaning, sense.Id, 0); } return;
        }
        var locale = VocabularyCategoryUiLanguage.Identifier; var query = _query.Text; foreach (var category in Classification.Categories.Where(c => _selected.Contains(c.Id))) { var title = string.Join(" › ", VocabularyClassificationProjection.Breadcrumb(category.Id, Classification).TakeLast(2).Select(c => R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(c, Classification), [locale]).Name)); var chip = new Button { Content = title + "  ×", Margin = new Thickness(0, 0, 6, 4), ToolTip = "Remove " + title }; VocabularyNotebookTheme.Button(chip); chip.Click += (_, _) => { _selected.Remove(category.Id); Render(); }; _chips.Children.Add(chip); } var rows = VocabularyClassificationProjection.CurrentRows(Classification, [], _expanded, new Search(), [locale], query, true);
        foreach (var row in rows.Where(r => query.Length > 0 || Equals(r.Category.Axis, _axis.SelectedItem)))
        {
            var line = new DockPanel { Margin = new Thickness(row.Depth * 16, 2, 0, 2) }; var expand = new Button { Content = _expanded.Contains(row.Id) ? "−" : "+", Width = 26, IsEnabled = row.HasChildren, ToolTip = InterfaceText.Get("Expand category", "Vocabulary") }; VocabularyNotebookTheme.Button(expand); expand.Click += (_, _) => { if (!_expanded.Add(row.Id)) _expanded.Remove(row.Id); Render(); }; DockPanel.SetDock(expand, Dock.Left); line.Children.Add(expand);
            var check = new CheckBox { Content = row.Title + (row.IsHidden ? " (hidden)" : ""), IsChecked = _selected.Contains(row.Category.Id), Margin = new Thickness(8, 5, 0, 5) }; VocabularyNotebookTheme.Brush(check, Control.ForegroundProperty, "TextPrimary"); check.Click += (_, _) => { if (check.IsChecked == true) _selected.Add(row.Category.Id); else _selected.Remove(row.Category.Id); Render(); }; line.Children.Add(check); _items.Children.Add(line);
        }
    }
    private void AddChoice(string text, Guid id, int depth) { var check = new CheckBox { Content = text, IsChecked = _selected.Contains(id), Margin = new Thickness(depth * 16, 5, 0, 5) }; VocabularyNotebookTheme.Brush(check, Control.ForegroundProperty, "TextPrimary"); check.Click += (_, _) => { if (check.IsChecked == true) { if (!_allowsMultiple) _selected.Clear(); _selected.Add(id); } else _selected.Remove(id); if (!_allowsMultiple) Render(); }; _items.Children.Add(check); }
    private async Task CreateAsync()
    {
        if (_retiring || _saving || _childDone is not null) return; var done = _childDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { var editor = _child = new FrozenVocabularyCategoryEditorWindow(_store, Classification, _languages, parentId: _selected.Count == 1 ? _selected.Single() : null, draftOnly: true, currentRules: true, initialAxis: _axis.SelectedItem is R.VocabularyCategoryAxis axis ? axis : R.VocabularyCategoryAxis.Content) { Owner = this }; try { editor.ShowDialog(); } finally { await editor.RetireAsync(); _child = null; } if (!_retiring && editor.DraftResult is { } result) { Classification = result; _selected.Add(editor.CategoryId); _axis.SelectedItem = result.Categories.Single(c => c.Id == editor.CategoryId).Axis; _expanded.UnionWith(VocabularyClassificationProjection.ExpansionPaths(_selected, result, true)); Render(); } }
        finally { _child = null; _childDone = null; done.TrySetResult(); }
    }
    private async Task SaveAsync()
    {
        if (_retiring || _saving || _childDone is not null) return; _saving = true; _failure.Text = ""; IsEnabled = false;
        try { if (_commit is not null) await _commit(Classification, _selected); Accepted = true; }
        catch (Exception error) { if (!_retiring) _failure.Text = error.Message; }
        finally { _saving = false; IsEnabled = true; }
        if (Accepted && !_retiring) Close();
    }
    internal Task RetireAsync() { Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement.Task; var done = _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously); _ = Finish(done); return done.Task; }
    private async Task Finish(TaskCompletionSource done) { _retiring = true; try { if (_child is { } child) await child.RetireAsync(); if (_childDone is { } pending) await pending.Task; if (_saveTask is { } save) await save; if (IsVisible) Close(); done.TrySetResult(); } catch (Exception e) { done.TrySetException(e); } }
}
