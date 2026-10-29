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
    private readonly ICanonicalVocabularySemanticStore _store;
    protected readonly VocabularySemanticIndex Baseline;
    protected readonly StackPanel Fields = new();
    private readonly TextBlock _failure = VocabularyNotebookTheme.Brush(new TextBlock { Name = "semanticFormError", TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    private readonly Button _save = new() { Name = "semanticFormSave", Content = "Save", IsDefault = true, MinWidth = 90 }, _cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 90 };
    private bool _saving, _retiring;
    private TaskCompletionSource? _retirement;
    internal bool Saved { get; private set; }
    internal Task? LastOperation { get; private set; }
    protected abstract bool Valid { get; }
    protected abstract VocabularySemanticIndex Edited();
    protected FrozenVocabularySemanticFormWindow(ICanonicalVocabularySemanticStore store, VocabularySemanticIndex baseline, string title)
    {
        _store = store; Baseline = baseline; Title = title; Width = 600; Height = 520; MinWidth = 500; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; VocabularyNotebookTheme.Follow(this);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root; var heading = VocabularyInputControls.Heading(title); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = VocabularyInputControls.Footer(_cancel, _save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); DockPanel.SetDock(_failure, Dock.Bottom); root.Children.Add(_failure);
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _save.Click += (_, _) => LastOperation = SaveAsync(); _cancel.Click += (_, _) => Close(); Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_saving) e.Cancel = true; }
    protected void UpdateAdmission() { Fields.IsEnabled = !_saving && !_retiring; _cancel.IsEnabled = !_saving && !_retiring; _save.IsEnabled = !_saving && !_retiring && Valid; }
    private async Task SaveAsync()
    {
        if (!_save.IsEnabled) return; _saving = true; _failure.Text = ""; UpdateAdmission();
        try { await _store.UpdateSemanticsAsync(Edited(), Baseline, CancellationToken.None); Saved = true; }
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
        _retiring = true; try { UpdateAdmission(); if (LastOperation is { } task) await task; if (IsVisible) Close(); done.TrySetResult(); } catch (Exception e) { done.TrySetException(e); }
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
    internal Guid NodeId => _node.Id;
    protected override bool Valid => !string.IsNullOrWhiteSpace(_title.Text);
    internal FrozenVocabularyMeaningEditorWindow(ICanonicalVocabularySemanticStore store, VocabularySemanticIndex baseline, IReadOnlyList<LearningLanguage> languages, VocabularyMeaningNode? node = null, VocabularySelection? linking = null)
        : base(store, baseline, "Group by meaning")
    {
        _isNew = node is null; _linking = linking; _node = node ?? new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("") { Language = languages.Count > 0 ? languages[0] : null }]);
        _label = VocabularyMeaningLabelPolicy.Label(_node.Labels, languages) ?? _node.Labels[0]; _title.Text = _label.Text; _definition.Text = _node.Definition;
        Fields.Children.Add(VocabularyInputControls.Caption("What do you want to express?"));
        Fields.Children.Add(VocabularyInputControls.Group(("Name", _title), ("Description (optional)", _definition)));
        Fields.Children.Add(VocabularyInputControls.Caption("Label language: " + (_label.Language?.Name() ?? "Unspecified")));
        if (_isNew)
        {
            _kind.ItemsSource = new[] { new KindChoice("Things and situations", R.VocabularyMeaningNodeKind.Concept), new KindChoice("Requests, suggestions and attitudes", R.VocabularyMeaningNodeKind.Function) }; _kind.DisplayMemberPath = "Label"; _kind.SelectedValuePath = "Value"; _kind.SelectedValue = _node.Kind;
            Fields.Children.Add(VocabularyInputControls.Group(("Use this to describe", _kind)));
        }
        _title.TextChanged += (_, _) => UpdateAdmission(); UpdateAdmission();
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
    internal FrozenVocabularyMappingEditorWindow(ICanonicalVocabularySemanticStore store, VocabularySemanticIndex baseline, IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<LearningLanguage> languages, Guid? fixedNode = null, Guid? fixedSense = null, R.VocabularyMeaningMapping? original = null)
        : base(store, baseline, "Group by meaning")
    {
        _entries = entries; _original = original; Guid? senseId = original?.SenseId ?? fixedSense; Guid? nodeId = original?.NodeId ?? fixedNode;
        _node.ItemsSource = new[] { new Choice("Choose a meaning", null) }.Concat(baseline.Nodes.Select(n => new Choice(VocabularyMeaningLabelPolicy.Title(n, languages), n.Id))).ToArray();
        _sense.ItemsSource = new[] { new Choice("Choose an expression", null) }.Concat(entries.SelectMany(e => e.Senses.Select(s => new Choice(e.Expression + " · " + (e.Language?.Name() ?? "Unspecified") + " · " + (s.Meaning.Length == 0 ? "Meaning pending" : s.Meaning), s.Id)))).ToArray();
        foreach (var box in new[] { _node, _sense }) { box.DisplayMemberPath = "Label"; box.SelectedValuePath = "Value"; }
        _node.SelectedValue = nodeId; _sense.SelectedValue = senseId; if (nodeId is null) _node.SelectedIndex = 0; if (senseId is null) _sense.SelectedIndex = 0;
        _node.IsEnabled = fixedNode is null; _sense.IsEnabled = fixedSense is null && original is null;
        _conditions.Text = original?.Conditions ?? ""; _nuance.Text = original?.Nuance ?? "";
        Fields.Children.Add(VocabularyInputControls.Group(("Expression", _sense), ("Meaning group", _node)));
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
    protected override VocabularySemanticIndex Edited()
    {
        var node = Baseline.Nodes.First(n => n.Id == (Guid)_node.SelectedValue); var sense = (Guid)_sense.SelectedValue;
        var mapping = VocabularySemanticFormPolicy.Classify(_original ?? new R.VocabularyMeaningMapping(node.Id, sense), node, _conditions.Text, _nuance.Text);
        var mappings = Baseline.Mappings.Any(m => m.Id == mapping.Id) ? Baseline.Mappings.Select(m => m.Id == mapping.Id ? mapping : m).ToArray() : Baseline.Mappings.Append(mapping).ToArray();
        return Baseline with { Mappings = mappings };
    }
}
