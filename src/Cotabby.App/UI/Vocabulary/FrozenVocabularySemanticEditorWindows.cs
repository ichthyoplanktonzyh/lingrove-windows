using Cotabby.App.Localization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using R = Cotabby.Core.Vocabulary.Resources;
using VocabularyEntry = Cotabby.Core.Vocabulary.Frozen.VocabularyEntry;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Modal semantic draft owns a baseline and borrows the canonical Store. Accepted saves
/// cannot be dismissed and retirement joins them before the notebook releases its owner.</summary>
internal abstract class FrozenVocabularySemanticFormWindow : Window
{
    protected readonly ICanonicalVocabularySemanticStore Store;
    protected readonly VocabularySemanticIndex Baseline;
    protected readonly StackPanel Fields = new();
    private readonly TextBlock _failure = VocabularyNotebookTheme.Brush(new TextBlock { Name = "semanticFormError", TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    private readonly Button _save = new() { Name = "semanticFormSave", Content = InterfaceText.Get("Save"), IsDefault = true, MinWidth = 90 }, _cancel = new() { Content = InterfaceText.Get("Cancel"), IsCancel = true, MinWidth = 90 };
    private bool _saving, _retiring;
    private TaskCompletionSource? _retirement;
    protected Window? ChildWindow;
    protected TaskCompletionSource? ChildDone;
    internal bool Saved { get; private set; }
    protected bool Retiring => _retiring;
    internal Task? LastOperation { get; private set; }
    protected abstract bool Valid { get; }
    protected abstract VocabularySemanticIndex Edited();
    protected virtual Task SubmitAsync() => Store.UpdateSemanticsAsync(Edited(), Baseline, CancellationToken.None);
    protected FrozenVocabularySemanticFormWindow(ICanonicalVocabularySemanticStore store, VocabularySemanticIndex baseline, string title)
    {
        Store = store; Baseline = baseline; Title = title; Width = 600; Height = 520; MinWidth = 500; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; VocabularyNotebookTheme.Follow(this);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root; var heading = VocabularyInputControls.Heading(title); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = VocabularyInputControls.Footer(_cancel, _save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); DockPanel.SetDock(_failure, Dock.Bottom); root.Children.Add(_failure);
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _save.Click += (_, _) => LastOperation = SaveAsync(); _cancel.Click += (_, _) => Close(); Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_saving || ChildDone is not null) e.Cancel = true; }
    protected void UpdateAdmission() { Fields.IsEnabled = !_saving && !_retiring && ChildDone is null; _cancel.IsEnabled = !_saving && !_retiring && ChildDone is null; _save.IsEnabled = !_saving && !_retiring && ChildDone is null && Valid; }
    private async Task SaveAsync()
    {
        if (!_save.IsEnabled) return; _saving = true; _failure.Text = ""; UpdateAdmission();
        try { await SubmitAsync(); Saved = true; }
        catch (Exception error) { if (!_retiring) _failure.Text = error.Message; }
        finally { _saving = false; UpdateAdmission(); }
        if (Saved && !_retiring) Close();
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement.Task; var done = _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously); _ = Finish(done); return done.Task;
    }
    private async Task Finish(TaskCompletionSource done)
    {
        _retiring = true; try { UpdateAdmission(); if (ChildWindow is FrozenVocabularyDraftPickerWindow picker) await picker.RetireAsync(); else ChildWindow?.Close(); if (ChildDone is { } child) await child.Task; if (LastOperation is { } task) await task; if (IsVisible) Close(); done.TrySetResult(); } catch (Exception e) { done.TrySetException(e); }
    }
}

/// <summary>Edits exactly the originally displayed label identity and language.</summary>
internal sealed class FrozenVocabularyMeaningEditorWindow : FrozenVocabularySemanticFormWindow
{
    private sealed record KindChoice(string Label, R.VocabularyMeaningNodeKind Value) { public override string ToString() => Label; }
    private readonly VocabularyMeaningNode _node;
    private readonly R.VocabularyMeaningLabel _label;
    private readonly VocabularySelection? _linking;
    private readonly TextBox _title = new() { Name = "canonicalMeaningName" }, _definition = new() { Name = "canonicalMeaningDescription", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
    private readonly ComboBox _kind = new() { Name = "canonicalMeaningKind" };
    private readonly bool _isNew;
    private readonly VocabularyMeaningDraft? _draft;
    private readonly IReadOnlyList<VocabularyEntry> _entries;
    private readonly IReadOnlyList<LearningLanguage> _languages;
    private readonly TextBlock _categorySummary = VocabularyInputControls.Caption(""), _expressionSummary = VocabularyInputControls.Caption("");
    internal Guid NodeId => _node.Id;
    protected override bool Valid => !string.IsNullOrWhiteSpace(_title.Text);
    internal FrozenVocabularyMeaningEditorWindow(ICanonicalVocabularySemanticStore store, VocabularySemanticIndex baseline, IReadOnlyList<LearningLanguage> languages, VocabularyMeaningNode? node = null, VocabularySelection? linking = null, R.VocabularyClassificationIndex? classification = null, IReadOnlyList<VocabularyEntry>? entries = null, Guid? categoryId = null)
        : base(store, baseline, "Group by meaning")
    {
        _languages = languages; _entries = entries ?? []; _draft = classification is not null && store is ICanonicalVocabularyMeaningOrganizationStore ? new(baseline, classification, languages, node, categoryId, linking?.SenseId) : null;
        _isNew = node is null; _linking = linking; _node = _draft?.Original ?? node ?? new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("") { Language = languages.Count > 0 ? languages[0] : null }]);
        _label = VocabularyMeaningLabelPolicy.Label(_node.Labels, languages) ?? _node.Labels[0]; _title.Text = _label.Text; _definition.Text = _node.Definition;
        Fields.Children.Add(VocabularyInputControls.Caption(InterfaceText.Get("What do you want to express?")));
        Fields.Children.Add(VocabularyInputControls.Group(("Name", _title), ("Description (optional)", _definition)));
        Fields.Children.Add(VocabularyInputControls.Caption("Label language: " + (_label.Language?.Name() ?? "Unspecified")));
        if (_isNew)
        {
            _kind.ItemsSource = new[] { new KindChoice("Things and situations", R.VocabularyMeaningNodeKind.Concept), new KindChoice("Requests, suggestions and attitudes", R.VocabularyMeaningNodeKind.Function) }; _kind.DisplayMemberPath = "Label"; _kind.SelectedValuePath = "Value"; _kind.SelectedValue = _node.Kind;
            Fields.Children.Add(VocabularyInputControls.Group(("Use this to describe", _kind)));
        }
        if (_draft is not null)
        {
            Fields.Children.Add(VocabularyInputControls.Heading(InterfaceText.Get("Categories"))); Fields.Children.Add(_categorySummary);
            var categories = new Button { Name = "meaningDraftCategories", Content = "Choose categories", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 12) }; VocabularyNotebookTheme.Button(categories); categories.Click += (_, _) => _ = ChooseAsync(false); Fields.Children.Add(categories);
            if (_isNew) { Fields.Children.Add(VocabularyInputControls.Heading(InterfaceText.Get("My expressions"))); Fields.Children.Add(_expressionSummary); if (linking is null) { var expressions = new Button { Name = "meaningDraftExpressions", Content = InterfaceText.Get("Add from vocabulary"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 12) }; VocabularyNotebookTheme.Button(expressions); expressions.Click += (_, _) => _ = ChooseAsync(true); Fields.Children.Add(expressions); } }
            RefreshDraftSummaries();
        }
        _title.TextChanged += (_, _) => UpdateAdmission(); UpdateAdmission();
    }
    private void RefreshDraftSummaries()
    {
        if (_draft is null) return; var locale = VocabularyCategoryUiLanguage.Identifier;
        var names = _draft.Classification.Categories.Where(c => _draft.CategoryIds.Contains(c.Id)).Select(c => string.Join(" › ", VocabularyClassificationProjection.Breadcrumb(c.Id, _draft.Classification).TakeLast(2).Select(part => R.VocabularyCategoryLocalizationPolicy.Resolve(R.VocabularyClassificationPolicy.Effective(part, _draft.Classification), [locale]).Name))).ToArray(); _categorySummary.Text = names.Length == 0 ? "Uncategorized meanings" : string.Join(" · ", names);
        _expressionSummary.Text = string.Join("\n", _entries.SelectMany(e => e.Senses.Where(s => _draft.SenseIds.Contains(s.Id)).Select(s => e.Expression + " · " + s.Meaning)));
    }
    private async Task ChooseAsync(bool expressions)
    {
        if (Retiring || ChildDone is not null || _draft is null) return; var done = ChildDone = new(TaskCreationOptions.RunContinuationsAsynchronously); UpdateAdmission();
        try
        {
            var selectedKind = _kind.SelectedValue is R.VocabularyMeaningNodeKind value ? value : _node.Kind; var axis = selectedKind == R.VocabularyMeaningNodeKind.Function ? R.VocabularyCategoryAxis.Function : R.VocabularyCategoryAxis.Content;
            var picker = new FrozenVocabularyDraftPickerWindow(Store, _draft.Classification, _languages, expressions ? _draft.SenseIds : _draft.CategoryIds, axis, expressions ? _entries : null) { Owner = this }; ChildWindow = picker;
            try { picker.ShowDialog(); } finally { await picker.RetireAsync(); ChildWindow = null; }
            if (!Retiring && picker.Accepted) { var target = expressions ? _draft.SenseIds : _draft.CategoryIds; target.Clear(); target.UnionWith(picker.Selection); if (!expressions) { _draft.Classification = picker.Classification; var axes = _draft.Classification.Categories.Where(c => target.Contains(c.Id)).Select(c => c.Axis).Distinct().ToArray(); if (_isNew && axes.Length == 1) _kind.SelectedValue = axes[0] == R.VocabularyCategoryAxis.Function ? R.VocabularyMeaningNodeKind.Function : R.VocabularyMeaningNodeKind.Concept; } RefreshDraftSummaries(); }
        }
        finally { ChildWindow = null; ChildDone = null; try { UpdateAdmission(); } finally { done.TrySetResult(); } }
    }
    protected override async Task SubmitAsync()
    {
        if (_draft is null || Store is not ICanonicalVocabularyMeaningOrganizationStore store) { await base.SubmitAsync(); return; }
        var kind = _kind.SelectedValue is R.VocabularyMeaningNodeKind value ? value : _node.Kind; var indexes = _draft.Indexes(_title.Text, _definition.Text, kind, SourceDate.FromWindows(DateTimeOffset.UtcNow));
        await store.UpdateMeaningOrganizationAsync(indexes.Semantics, _draft.SemanticBaseline, indexes.Classification, _draft.ClassificationBaseline, CancellationToken.None);
    }
    protected override VocabularySemanticIndex Edited()
    {
        var value = VocabularySemanticFormPolicy.Rename(_node, _title.Text, _definition.Text, _label.Id, SourceDate.FromWindows(DateTimeOffset.UtcNow));
        if (_isNew && _kind.SelectedValue is R.VocabularyMeaningNodeKind kind) value = value with { Kind = kind };
        var nodes = Baseline.Nodes.Any(n => n.Id == value.Id) ? Baseline.Nodes.Select(n => n.Id == value.Id ? value : n).ToArray() : Baseline.Nodes.Append(value).ToArray();
        var mappings = Baseline.Mappings;
        if (_linking is { } link && !mappings.Any(m => m.NodeId == value.Id && m.SenseId == link.SenseId)) mappings = mappings.Append(new R.VocabularyMeaningMapping(value.Id, link.SenseId) { Relation = value.Kind == R.VocabularyMeaningNodeKind.Concept ? R.VocabularyMappingRelation.ExpressesContent : R.VocabularyMappingRelation.RealizesFunction }).ToArray();
        return Baseline with { Nodes = nodes, Mappings = mappings };
    }
}

/// <summary>Mapping edits preserve restricted pattern, cited evidence and identity.</summary>
internal sealed class FrozenVocabularyMappingEditorWindow : FrozenVocabularySemanticFormWindow
{
    private sealed record Choice(string Label, Guid? Value) { public override string ToString() => Label; }
    private readonly IReadOnlyList<VocabularyEntry> _entries;
    private readonly R.VocabularyMeaningMapping? _original;
    private readonly ComboBox _node = new() { Name = "canonicalMappingNode" }, _sense = new() { Name = "canonicalMappingSense" };
    private readonly TextBox _conditions = new() { Name = "canonicalMappingConditions", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 55 }, _nuance = new() { Name = "canonicalMappingNuance", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 55 };
    protected override bool Valid => _node.SelectedValue is Guid node && Baseline.Nodes.Any(n => n.Id == node) && _sense.SelectedValue is Guid sense && _entries.Any(e => e.Senses.Any(s => s.Id == sense));
    internal FrozenVocabularyMappingEditorWindow(ICanonicalVocabularySemanticStore store, VocabularySemanticIndex baseline, IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<LearningLanguage> languages, Guid? fixedNode = null, Guid? fixedSense = null, R.VocabularyMeaningMapping? original = null, bool currentRules = false)
        : base(store, baseline, "Group by meaning")
    {
        _entries = entries; _original = original; Guid? senseId = original?.SenseId ?? fixedSense; Guid? nodeId = original?.NodeId ?? fixedNode;
        _node.ItemsSource = new[] { new Choice("Choose a meaning", null) }.Concat(baseline.Nodes.Select(n => new Choice(VocabularyMeaningLabelPolicy.Title(n, languages), n.Id))).ToArray();
        _sense.ItemsSource = new[] { new Choice("Choose an expression", null) }.Concat(entries.SelectMany(e => e.Senses.Select(s => new Choice(e.Expression + " · " + (e.Language?.Name() ?? "Unspecified") + " · " + (s.Meaning.Length == 0 ? "Meaning pending" : s.Meaning), s.Id)))).ToArray();
        foreach (var box in new[] { _node, _sense }) { box.DisplayMemberPath = "Label"; box.SelectedValuePath = "Value"; }
        _node.SelectedValue = nodeId; _sense.SelectedValue = senseId; if (nodeId is null) _node.SelectedIndex = 0; if (senseId is null) _sense.SelectedIndex = 0;
        _node.IsEnabled = fixedNode is null; _sense.IsEnabled = fixedSense is null && original is null;
        _conditions.Text = original?.Conditions ?? ""; _nuance.Text = original?.Nuance ?? "";
        if (currentRules && senseId is null) { var choose = new Button { Name = "mappingChooseExpression", Content = InterfaceText.Get("Choose an expression"), HorizontalAlignment = HorizontalAlignment.Left }; VocabularyNotebookTheme.Button(choose); choose.Click += (_, _) => _ = ChooseExpressionAsync(choose, entries, languages); Fields.Children.Add(VocabularyInputControls.Group(("Expression", choose), ("Meaning group", _node))); }
        else Fields.Children.Add(VocabularyInputControls.Group(("Expression", _sense), ("Meaning group", _node)));
        var context = VocabularyInputControls.Caption(""); Fields.Children.Add(context);
        void UpdateContext()
        {
            var selected = _sense.SelectedValue is Guid id ? entries.SelectMany(e => e.Senses).FirstOrDefault(s => s.Id == id) : null;
            var selectedNode = _node.SelectedValue is Guid nodeId ? baseline.Nodes.FirstOrDefault(n => n.Id == nodeId) : null;
            context.Text = string.Join("\n", new[] { selected?.Examples.FirstOrDefault()?.Text, selectedNode?.Definition }.Where(x => !string.IsNullOrEmpty(x)));
            context.Visibility = context.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        if (original?.PatternId is { } patternId && baseline.Patterns.FirstOrDefault(p => p.Id == patternId) is { } pattern) Fields.Children.Add(VocabularyInputControls.Caption("Used in this wording: " + pattern.Template + (pattern.Note.Length > 0 ? "\n" + pattern.Note : "")));
        Fields.Children.Add(VocabularyInputControls.Group(("Usage conditions", _conditions), ("Nuance", _nuance)));
        _node.SelectionChanged += (_, _) => { UpdateContext(); UpdateAdmission(); }; _sense.SelectionChanged += (_, _) => { UpdateContext(); UpdateAdmission(); }; UpdateContext(); UpdateAdmission();
    }
    private async Task ChooseExpressionAsync(Button button, IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<LearningLanguage> languages)
    {
        if (Retiring || ChildDone is not null) return; var done = ChildDone = new(TaskCreationOptions.RunContinuationsAsynchronously); UpdateAdmission();
        try { var selected = _sense.SelectedValue is Guid id ? new HashSet<Guid> { id } : []; var picker = new FrozenVocabularyDraftPickerWindow(Store, new(), languages, selected, R.VocabularyCategoryAxis.Content, entries, allowsMultiple: false) { Owner = this }; ChildWindow = picker; try { picker.ShowDialog(); } finally { await picker.RetireAsync(); ChildWindow = null; } if (!Retiring && picker.Accepted) { _sense.SelectedValue = picker.Selection.Cast<Guid?>().FirstOrDefault(); var entry = entries.FirstOrDefault(e => e.Senses.Any(s => s.Id == (Guid?)_sense.SelectedValue)); var sense = entry?.Senses.FirstOrDefault(s => s.Id == (Guid?)_sense.SelectedValue); button.Content = entry is null ? "Choose an expression" : entry.Expression + " · " + (sense?.Meaning ?? ""); } }
        finally { ChildWindow = null; ChildDone = null; try { UpdateAdmission(); } finally { done.TrySetResult(); } }
    }
    protected override VocabularySemanticIndex Edited()
    {
        var node = Baseline.Nodes.First(n => n.Id == (Guid)_node.SelectedValue); var sense = (Guid)_sense.SelectedValue;
        var mapping = VocabularySemanticFormPolicy.Classify(_original ?? new R.VocabularyMeaningMapping(node.Id, sense), node, _conditions.Text, _nuance.Text);
        var mappings = Baseline.Mappings.Any(m => m.Id == mapping.Id) ? Baseline.Mappings.Select(m => m.Id == mapping.Id ? mapping : m).ToArray() : Baseline.Mappings.Append(mapping).ToArray();
        return Baseline with { Mappings = mappings };
    }
}
