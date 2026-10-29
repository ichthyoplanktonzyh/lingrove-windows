using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.UI.Vocabulary;

internal sealed class FrozenVocabularyCategoryChoice(string label, Guid id, HashSet<Guid> selected) : INotifyPropertyChanged
{
    public string Label { get; } = label;
    public Guid Id { get; } = id;
    public bool Checked { get => selected.Contains(Id); set { if (value) selected.Add(Id); else selected.Remove(Id); PropertyChanged?.Invoke(this, new(nameof(Checked))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal static DataTemplate Template()
    {
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 5, 8, 5)); check.SetValue(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")); check.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Label))); check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(Checked)) { Mode = BindingMode.TwoWay }); return new DataTemplate { VisualTree = check };
    }
}

/// <summary>Classification sheets borrow one canonical Store and retain their own baseline. The
/// nested create sheet and its post-save read remain owned until completion on both close paths.</summary>
internal abstract class FrozenVocabularyClassificationWindow : Window
{
    protected readonly ICanonicalVocabularySemanticStore Store;
    protected R.VocabularyClassificationIndex Baseline;
    protected readonly StackPanel Fields = new();
    protected readonly TextBlock Failure = VocabularyNotebookTheme.Brush(new TextBlock { Name = "classificationError", TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    private readonly Button _save = new() { Name = "classificationSave", Content = "Save", IsDefault = true, MinWidth = 90 }, _cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 90 };
    private bool _saving, _retiring;
    private TaskCompletionSource? _retirement;
    protected FrozenVocabularyClassificationWindow? Child;
    protected TaskCompletionSource? ChildDone;
    protected readonly CancellationTokenSource Lifetime = new();
    internal FrozenVocabularyImportSnapshot? Receipt { get; private set; }
    internal Task? LastOperation { get; private set; }
    protected bool Retiring => _retiring;
    protected abstract bool Valid { get; }
    protected abstract R.VocabularyClassificationIndex Edited();
    protected FrozenVocabularyClassificationWindow(ICanonicalVocabularySemanticStore store, R.VocabularyClassificationIndex baseline, string title)
    {
        Store = store; Baseline = baseline; Title = title; Width = 600; Height = 560; MinWidth = 500; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; VocabularyNotebookTheme.Follow(this);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root; var heading = VocabularyInputControls.Heading(title); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = VocabularyInputControls.Footer(_cancel, _save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); DockPanel.SetDock(Failure, Dock.Bottom); root.Children.Add(Failure);
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _save.Click += (_, _) => LastOperation = SaveAsync(); _cancel.Click += (_, _) => Close(); Closing += OnClosing; Closed += (_, _) => { _retiring = true; Lifetime.Cancel(); };
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_saving || ChildDone is not null) e.Cancel = true; }
    protected void UpdateAdmission() { Fields.IsEnabled = !_saving && !_retiring && ChildDone is null; _cancel.IsEnabled = !_saving && !_retiring && ChildDone is null; _save.IsEnabled = !_saving && !_retiring && ChildDone is null && Valid; }
    private async Task SaveAsync()
    {
        if (!_save.IsEnabled) return; _saving = true; Failure.Text = ""; UpdateAdmission();
        try { Receipt = await Store.UpdateClassificationAsync(Edited(), Baseline, CancellationToken.None); }
        catch (Exception error) { if (!_retiring) Failure.Text = error.Message; }
        finally { _saving = false; UpdateAdmission(); }
        if (Receipt is not null && !_retiring) Close();
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement.Task; var done = _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously); _ = Finish(done); return done.Task;
    }
    private async Task Finish(TaskCompletionSource done)
    {
        _retiring = true; Lifetime.Cancel();
        try { UpdateAdmission(); if (Child is { } child) await child.RetireAsync(); if (ChildDone is { } pending) await pending.Task; if (LastOperation is { } operation) await operation; if (IsVisible) Close(); Lifetime.Dispose(); done.TrySetResult(); }
        catch (Exception error) { done.TrySetException(error); }
    }
}

/// <summary>Edits a custom category and parents, or only a local overlay for a bundled category.</summary>
internal sealed class FrozenVocabularyCategoryEditorWindow : FrozenVocabularyClassificationWindow
{
    private sealed record AxisChoice(string Label, R.VocabularyCategoryAxis Value) { public override string ToString() => Label; }
    private readonly IReadOnlyList<LearningLanguage> _languages;
    private readonly R.VocabularyCategory _original;
    private readonly R.VocabularyMeaningLabel _label;
    private readonly bool _isNew;
    private readonly HashSet<Guid> _parents;
    private readonly TextBox _name = new() { Name = "categoryName" }, _definition = new() { Name = "categoryDescription", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 55 }, _search = new() { Name = "categoryParentSearch" };
    private readonly ComboBox _axis = new() { Name = "categoryAxis" };
    private readonly CheckBox _visible = new() { Name = "categoryVisible", Content = "Visible" };
    private readonly ListBox _parentList = new() { Name = "categoryParents", Height = 130 };
    internal Guid CategoryId => _original.Id;
    protected override bool Valid => !string.IsNullOrWhiteSpace(_name.Text);
    internal FrozenVocabularyCategoryEditorWindow(ICanonicalVocabularySemanticStore store, R.VocabularyClassificationIndex baseline, IReadOnlyList<LearningLanguage> languages, R.VocabularyCategory? category = null, Guid? parentId = null)
        : base(store, baseline, category is null ? "New category" : "Edit category")
    {
        _languages = Array.AsReadOnly(languages.ToArray()); var parent = baseline.Categories.FirstOrDefault(c => c.Id == parentId);
        _isNew = category is null; _original = category ?? new R.VocabularyCategory(Guid.NewGuid(), parent?.Axis ?? R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("") { Language = languages.Count > 0 ? languages[0] : null }]);
        var effective = R.VocabularyClassificationPolicy.Effective(_original, baseline); _label = VocabularyMeaningLabelPolicy.Label(effective.Labels, languages) ?? effective.Labels[0];
        _name.Text = _label.Text; _definition.Text = effective.Definition; _visible.IsChecked = !baseline.Overrides.Any(o => o.CategoryId == _original.Id && o.IsHidden);
        _parents = category is null ? parentId is { } id ? [id] : [] : baseline.Parents.Where(p => p.CategoryId == _original.Id).Select(p => p.ParentId).ToHashSet();
        _axis.ItemsSource = new[] { new AxisChoice("Content meanings", R.VocabularyCategoryAxis.Content), new AxisChoice("Communicative functions", R.VocabularyCategoryAxis.Function) }; _axis.DisplayMemberPath = "Label"; _axis.SelectedValuePath = "Value"; _axis.SelectedValue = _original.Axis;
        Fields.Children.Add(VocabularyInputControls.Group(("Category name", _name), ("Description (optional)", _definition)));
        Fields.Children.Add(VocabularyInputControls.Caption("Label language: " + (_label.Language?.Name() ?? "Unspecified")));
        if (_isNew) Fields.Children.Add(VocabularyInputControls.Group(("Category type", _axis)));
        _visible.Margin = new Thickness(2, 6, 0, 12); VocabularyNotebookTheme.Brush(_visible, Control.ForegroundProperty, "TextPrimary"); Fields.Children.Add(_visible);
        if (_original.FrameworkId is not null) Fields.Children.Add(VocabularyInputControls.Caption("Built-in definition and parents remain unchanged; your name, description and visibility are local overrides."));
        else
        {
            Fields.Children.Add(VocabularyInputControls.Heading("Parent categories")); _search.Tag = "Search categories"; VocabularyNotebookTheme.Input(_search); System.Windows.Automation.AutomationProperties.SetName(_search, "Search categories"); Fields.Children.Add(_search); System.Windows.Automation.AutomationProperties.SetName(_parentList, "Parent categories"); VocabularyNotebookTheme.Brush(_parentList, Control.BackgroundProperty, "CardBg"); VocabularyNotebookTheme.Brush(_parentList, Control.ForegroundProperty, "TextPrimary"); VocabularyNotebookTheme.Brush(_parentList, Control.BorderBrushProperty, "CardBorder"); _parentList.ItemTemplate = FrozenVocabularyCategoryChoice.Template(); VirtualizingPanel.SetVirtualizationMode(_parentList, VirtualizationMode.Recycling); Fields.Children.Add(_parentList);
            _search.TextChanged += (_, _) => RenderParents();
        }
        _axis.SelectionChanged += (_, _) => { if (_isNew) { _parents.Clear(); RenderParents(); } }; _name.TextChanged += (_, _) => UpdateAdmission(); RenderParents(); UpdateAdmission();
    }
    private R.VocabularyCategoryAxis Axis => _axis.SelectedValue is R.VocabularyCategoryAxis value ? value : _original.Axis;
    private void RenderParents()
    {
        var excluded = R.VocabularyClassificationPolicy.Descendants(_original.Id, Baseline);
        _parentList.ItemsSource = Baseline.Categories.Where(c => c.Axis == Axis && !c.IsRetired && !excluded.Contains(c.Id))
            .Select(c => (category: c, title: R.VocabularyClassificationPolicy.Title(c, Baseline, _languages)))
            .Where(item => CultureInfo.CurrentCulture.CompareInfo.IndexOf(item.title, _search.Text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .Select(item => new FrozenVocabularyCategoryChoice(item.title, item.category.Id, _parents)).ToArray();
    }
    protected override R.VocabularyClassificationIndex Edited() => VocabularyClassificationFormPolicy.EditCategory(Baseline, _original, _label, _name.Text, _definition.Text, Axis, _parents, _visible.IsChecked != true);
}
