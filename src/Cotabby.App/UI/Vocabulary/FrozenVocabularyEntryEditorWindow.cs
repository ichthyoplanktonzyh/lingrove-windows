using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using Microsoft.Win32;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>One modal owns a detached full-entry draft and borrows the canonical Store. Save is
/// acknowledged before dismissal; retirement joins every Store and range operation it started.</summary>
internal sealed class FrozenVocabularyEntryEditorWindow : Window
{
    private sealed record ContextChoice(string Label, R.VocabularyExampleCompleteness Value);
    private readonly ICanonicalVocabularyEditorStore _editor;
    private readonly ICanonicalVocabularyMediaStore _media;
    private readonly F.VocabularyEntry _baseline;
    private F.VocabularyEntry _draft;
    private readonly Guid _senseId;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, VocabularyPreparedImage> _images = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _work = [];
    private readonly TextBox _expression = new() { Name = "canonicalEditExpression" }, _dictionary = new(), _meaning = new(), _note = new(), _tags = new();
    private readonly StackPanel _form = new(), _blocks = new(), _examples = new();
    private readonly TextBlock _failure = VocabularyNotebookTheme.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    private readonly Button _save = new() { Name = "canonicalEditSave", Content = "Save", IsDefault = true, MinWidth = 90 }, _cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 90 }, _addText = new() { Content = "Add text" }, _addImage = new() { Content = "Add image" }, _addExample = new() { Content = "Add example" };
    private bool _saving, _retiring, _allowClose, _importing, _rangesPending, _rangeFailed;
    private long _rangeEpoch;
    private readonly Dictionary<Guid, (Guid Id, string Text, string Expression, string Form)> _rangeInputs = [];
    private CancellationTokenSource? _rangeToken;
    private TaskCompletionSource? _retirement;
    internal F.VocabularyEntry Draft => _draft;
    internal Task? LastOperation { get; private set; }
    internal bool Saved { get; private set; }
    private F.VocabularySense? Sense => _draft.Senses.FirstOrDefault(s => s.Id == _senseId);

    internal FrozenVocabularyEntryEditorWindow(ICanonicalVocabularyEditorStore editor, ICanonicalVocabularyMediaStore media, F.VocabularyEntry entry, Guid senseId, IReadOnlyList<string>? suggestedTags = null)
    {
        _editor = editor; _media = media; _baseline = entry; _draft = entry; _senseId = senseId;
        if (Sense is null) throw new ArgumentException("Selected sense is absent from the entry.", nameof(senseId));
        Title = "Edit vocabulary"; Width = 680; Height = 660; MinWidth = 540; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        VocabularyNotebookTheme.Follow(this); var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var title = new TextBlock { Text = entry.Expression, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        header.Children.Add(title); header.Children.Add(VocabularyInputControls.Caption((entry.Language?.Name() ?? "Unspecified") + "  ·  " + entry.Kind)); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = VocabularyInputControls.Footer(_cancel, _save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(_failure, Dock.Bottom); root.Children.Add(_failure);
        root.Children.Add(new ScrollViewer { Content = _form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _meaning.AcceptsReturn = _note.AcceptsReturn = true; _meaning.TextWrapping = _note.TextWrapping = TextWrapping.Wrap; _meaning.MinHeight = _note.MinHeight = 50;
        _expression.Text = entry.Expression; _dictionary.Text = entry.DictionaryForm; _meaning.Text = Sense!.Meaning; _note.Text = Sense.Note; _tags.Text = string.Join(", ", Sense.Tags);
        _form.Children.Add(VocabularyInputControls.Group(("Expression", _expression), ("Dictionary form", _dictionary), ("Core meaning", _meaning), ("Notes", _note), ("Tags", _tags)));
        var suggestions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; _form.Children.Add(suggestions);
        void RefreshTags()
        {
            suggestions.Children.Clear(); var existing = VocabularyEntryEditingPolicy.ParseTags(_tags.Text).Select(t => t.Trim('#')).ToHashSet(CanonicalStringComparer.Instance);
            foreach (string tag in (suggestedTags ?? []).Where(t => !existing.Contains(t)).Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance).Take(8))
                suggestions.Children.Add(Action("+ " + tag, () => { _tags.Text = string.IsNullOrWhiteSpace(_tags.Text) ? tag : _tags.Text.TrimEnd(' ', ',', '，') + ", " + tag; }));
        }
        _tags.TextChanged += (_, _) => RefreshTags(); RefreshTags();
        _form.Children.Add(VocabularyInputControls.Heading("Sense explanation")); _form.Children.Add(_blocks);
        var blockActions = new WrapPanel(); foreach (var b in new[] { _addText, _addImage }) { VocabularyNotebookTheme.Button(b); b.Margin = new Thickness(0, 0, 8, 8); blockActions.Children.Add(b); } _form.Children.Add(blockActions);
        var paste = new Button { Content = "Paste image", IsEnabled = false, ToolTip = "Image paste is not available in this editor yet.", Margin = new Thickness(0, 0, 8, 8) }; VocabularyNotebookTheme.Button(paste); blockActions.Children.Add(paste);
        _form.Children.Add(_examples); VocabularyNotebookTheme.Button(_addExample); _addExample.HorizontalAlignment = HorizontalAlignment.Left; _form.Children.Add(_addExample);
        _expression.TextChanged += (_, _) => { bool changed = !CanonicalStringComparer.Instance.Equals(_draft.Expression, _expression.Text); _draft = _draft with { Expression = _expression.Text }; title.Text = _expression.Text.Length == 0 ? _baseline.Expression : _expression.Text; if (changed) QueueRanges(Sense!.Examples); Update(); };
        _dictionary.TextChanged += (_, _) => _draft = _draft with { DictionaryForm = _dictionary.Text };
        _meaning.TextChanged += (_, _) => ChangeSense(s => s with { Meaning = _meaning.Text }); _note.TextChanged += (_, _) => ChangeSense(s => s with { Note = _note.Text });
        _save.Click += (_, _) => LastOperation = SaveAsync(); _cancel.Click += (_, _) => Close();
        _addText.Click += (_, _) => { ChangeSense(s => s with { Content = (s.Content ?? []).Append(VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new()))).ToArray() }); RenderBlocks(); };
        _addImage.Click += (_, _) => { var dialog = new OpenFileDialog { Title = "Add image", Filter = "Images|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif;*.ico;*.webp;*.heic;*.heif", Multiselect = false }; if (dialog.ShowDialog(this) == true) ImportImage(dialog.FileName); };
        _addExample.Click += (_, _) => { ChangeSense(s => s with { Examples = s.Examples.Append(new F.VocabularyExample(Guid.NewGuid(), "", "", [], R.VocabularyExampleCompleteness.Fragment, "", SourceDate.FromWindows(DateTimeOffset.UtcNow)) { SourceKind = R.VocabularySourceKind.Authored, TranslationLanguage = s.MeaningLanguage }).ToArray() }); RenderExamples(); };
        Closing += (_, e) => { if (_saving && !_allowClose) e.Cancel = true; }; Closed += (_, _) => { _retiring = true; _lifetime.Cancel(); _rangeToken?.Cancel(); };
        RenderBlocks(); RenderExamples(); Update();
    }
    private void ChangeSense(Func<F.VocabularySense, F.VocabularySense> change) { if (_retiring || _saving) return; _draft = _draft with { Senses = _draft.Senses.Select(s => s.Id == _senseId ? change(s) : s).ToArray() }; Update(); }
    private void ChangeExample(Guid id, Func<F.VocabularyExample, F.VocabularyExample> change) => ChangeSense(s => s with { Examples = s.Examples.Select(e => e.Id == id ? change(e) : e).ToArray() });
    private void ChangeBlock(Guid id, Func<VocabularyBlockContent, VocabularyBlockContent> change) => ChangeSense(s => s with { Content = (s.Content ?? []).Select(b => b.Id == id ? b with { Content = change(b.Content) } : b).ToArray() });
    private void Update() { bool ready = !_saving && !_retiring; _form.IsEnabled = ready; _cancel.IsEnabled = ready; _save.IsEnabled = ready && !_importing && !_rangesPending && !_rangeFailed && !string.IsNullOrWhiteSpace(_draft.Expression) && Sense is { } s && s.Examples.All(e => !string.IsNullOrWhiteSpace(e.Text)); _addText.IsEnabled = _addImage.IsEnabled = ready && !_importing && (Sense?.Content?.Count ?? 0) < VocabularyContentPolicy.MaximumBlocks; }
    private async Task SaveAsync()
    {
        if (!_save.IsEnabled) return; var tags = VocabularyEntryEditingPolicy.ParseTags(_tags.Text).Select(t => t.Trim().Trim('#')).Where(t => t.Length > 0).Distinct(CanonicalStringComparer.Instance).Order(CanonicalStringComparer.Instance).ToArray(); ChangeSense(s => s with { Tags = tags });
        _saving = true; _failure.Text = ""; Update();
        try { await _editor.UpdateEntryAsync(_draft, _baseline, _images, CancellationToken.None); Saved = true; }
        catch (Exception e) { if (!_retiring) _failure.Text = e.Message; }
        finally { _saving = false; Update(); }
        if (Saved && !_retiring) Close();
    }
    internal Task RetireAsync() { Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement.Task; var done = _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously); _ = RetireCore(done); return done.Task; }
    private async Task RetireCore(TaskCompletionSource done)
    {
        _retiring = true; _lifetime.Cancel(); _rangeToken?.Cancel(); Update();
        try { if (LastOperation is { } save) await save; while (_work.Count != 0) { await Task.WhenAll(_work.ToArray()); _work.RemoveWhere(t => t.IsCompleted); } _allowClose = true; if (IsVisible) Close(); _lifetime.Dispose(); done.TrySetResult(); }
        catch (Exception e) { done.TrySetException(e); }
    }
    private void Track(Task task) { _work.Add(task); _ = Remove(task); async Task Remove(Task t) { try { await t; } finally { _work.Remove(t); } } }
    private void QueueRanges(IEnumerable<F.VocabularyExample> examples)
    {
        foreach (var example in examples) _rangeInputs[example.Id] = (example.Id, example.Text, _draft.Expression, _draft.DictionaryForm);
        var live = Sense!.Examples.Select(e => e.Id).ToHashSet(); foreach (var id in _rangeInputs.Keys.Where(id => !live.Contains(id)).ToArray()) _rangeInputs.Remove(id);
        _rangeToken?.Cancel(); var token = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _rangeToken = token; long epoch = ++_rangeEpoch;
        var inputs = _rangeInputs.Values.ToArray(); _rangesPending = true; _rangeFailed = false; Update();
        Track(Compute()); async Task Compute() { try { var ranges = await Task.Run(() => inputs.ToDictionary(e => e.Id, e => VocabularyPolicy.ReconciledRanges(e.Text, e.Expression, e.Form, token.Token)), token.Token); if (!_retiring && epoch == _rangeEpoch) { ChangeSense(s => s with { Examples = s.Examples.Select(e => ranges.TryGetValue(e.Id, out var range) ? e with { Ranges = range } : e).ToArray() }); _rangeInputs.Clear(); _rangesPending = false; Update(); } } catch (OperationCanceledException) { } catch (Exception e) { if (!_retiring && epoch == _rangeEpoch) { _rangeFailed = true; _rangesPending = false; _failure.Text = e.Message; Update(); } } finally { if (_rangeToken == token) _rangeToken = null; token.Dispose(); } }
    }
    private TextBox Field(string value, string name, Action<string> changed, bool multiline = true) { var box = new TextBox { Text = value, TextWrapping = TextWrapping.Wrap, AcceptsReturn = multiline, MinHeight = multiline ? 50 : 0, Margin = new Thickness(0, 4, 0, 8) }; VocabularyNotebookTheme.Input(box); AutomationProperties.SetName(box, name); box.TextChanged += (_, _) => changed(box.Text); return box; }
    private static TextBlock Label(string value) => new() { Text = value, Margin = new Thickness(0, 8, 0, 3) };
    private static Button Action(string value, Action action) { var b = new Button { Content = value, Margin = new Thickness(0, 0, 6, 6) }; VocabularyNotebookTheme.Button(b); b.Click += (_, _) => action(); return b; }
    private static Border Card(UIElement child) { var b = new Border { Child = child, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) }; VocabularyNotebookTheme.Brush(b, Border.BackgroundProperty, "CardBg"); return VocabularyNotebookTheme.Brush(b, Border.BorderBrushProperty, "CardBorder"); }
    private void RenderBlocks()
    {
        _blocks.Children.Clear(); var blocks = Sense?.Content ?? [];
        foreach (var block in blocks) {
            var panel = new StackPanel(); var tools = new WrapPanel(); var up = Action("Move up", () => Move(block.Id, -1)); up.IsEnabled = block.Id != blocks[0].Id; var down = Action("Move down", () => Move(block.Id, 1)); down.IsEnabled = block.Id != blocks[^1].Id; tools.Children.Add(up); tools.Children.Add(down); tools.Children.Add(Action("Delete block", () => { ChangeSense(s => s with { Content = (s.Content ?? []).Where(b => b.Id != block.Id).ToArray() }); PruneImages(); RenderBlocks(); })); panel.Children.Add(tools);
            if (block.Content is VocabularyBlockContent.Text text) { var format = new WrapPanel(); var style = new ComboBox { ItemsSource = Enum.GetValues<VocabularyTextStyle>(), SelectedItem = text.Value.Style, Width = 130 }; VocabularyNotebookTheme.Combo(style); AutomationProperties.SetName(style, "Text style"); style.SelectionChanged += (_, _) => { if (style.SelectedItem is VocabularyTextStyle v) ChangeBlock(block.Id, c => c is VocabularyBlockContent.Text t ? new VocabularyBlockContent.Text(t.Value with { Style = v }) : c); }; format.Children.Add(style); foreach (bool bold in new[] { true, false }) { var toggle = new CheckBox { Content = bold ? "Bold" : "Italic", IsChecked = bold ? text.Value.Bold : text.Value.Italic, Margin = new Thickness(10, 3, 0, 0) }; toggle.Click += (_, _) => ChangeBlock(block.Id, c => c is VocabularyBlockContent.Text t ? new VocabularyBlockContent.Text(bold ? t.Value with { Bold = toggle.IsChecked == true } : t.Value with { Italic = toggle.IsChecked == true }) : c); format.Children.Add(toggle); } panel.Children.Add(format); panel.Children.Add(Field(text.Value.Text, "Explanation text", value => ChangeBlock(block.Id, c => c is VocabularyBlockContent.Text t ? new VocabularyBlockContent.Text(t.Value with { Text = value }) : c))); }
            else if (block.Content is VocabularyBlockContent.Image image) { var view = new Image { MaxHeight = 220, Stretch = Stretch.Uniform }; panel.Children.Add(view); var status = Label("Loading image…"); panel.Children.Add(status); Track(LoadImage(image.Reference, view, status)); panel.Children.Add(Field(image.Caption, "Caption", value => ChangeBlock(block.Id, c => c is VocabularyBlockContent.Image i ? i with { Caption = value } : c))); }
            _blocks.Children.Add(Card(panel));
        } Update();
    }
    private void Move(Guid id, int offset) { ChangeSense(s => s with { Content = VocabularyEntryEditingPolicy.MoveBlock(s.Content, id, offset) }); RenderBlocks(); }
    private void PruneImages() { var names = VocabularyContentPolicy.Images(Sense?.Content ?? []).Select(i => i.Filename).ToHashSet(StringComparer.Ordinal); foreach (var key in _images.Keys.Where(k => !names.Contains(k)).ToArray()) _images.Remove(key); }
    private async Task LoadImage(VocabularyImageReference reference, Image view, TextBlock status) { try { byte[] bytes = _images.TryGetValue(reference.Filename, out var image) ? image.CopyBytes() : await _media.ReadImageAsync(reference, _lifetime.Token); var bitmap = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(bytes, reference, _lifetime.Token)); if (!_retiring) { view.Source = bitmap; status.Visibility = Visibility.Collapsed; } } catch (OperationCanceledException) { } catch { if (!_retiring) status.Text = "Image unavailable"; } }
    internal void ImportImage(string path) { if (_retiring || _saving || _importing || (Sense?.Content?.Count ?? 0) >= VocabularyContentPolicy.MaximumBlocks) return; _importing = true; Update(); Track(Import()); async Task Import() { try { var prepared = await _media.PrepareImageFileAsync(path, _lifetime.Token); if (_retiring) return; var blocks = (Sense!.Content ?? []).Append(VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference))).ToArray(); VocabularyContentPolicy.Validate(blocks); _images[prepared.Reference.Filename] = prepared; ChangeSense(s => s with { Content = blocks }); RenderBlocks(); } catch (OperationCanceledException) { } catch { if (!_retiring) _failure.Text = "Unable to add this image. Try a smaller image."; } finally { _importing = false; Update(); } } }
    private void RenderExamples()
    {
        _examples.Children.Clear(); foreach (var example in Sense?.Examples ?? []) { var panel = new StackPanel(); panel.Children.Add(Label("Example sentence")); panel.Children.Add(Label("Original text")); panel.Children.Add(Field(example.Text, "Original text", value => { bool changed = !CanonicalStringComparer.Instance.Equals(Sense!.Examples.First(e => e.Id == example.Id).Text, value); ChangeExample(example.Id, e => e with { Text = value }); if (changed) QueueRanges([Sense!.Examples.First(e => e.Id == example.Id)]); })); panel.Children.Add(Label("Translation")); panel.Children.Add(Field(example.Translation, "Translation", value => ChangeExample(example.Id, e => e with { Translation = value }))); panel.Children.Add(Label("Meaning in this context")); panel.Children.Add(Field(example.Meaning, "Meaning in this context", value => ChangeExample(example.Id, e => e with { Meaning = value }))); panel.Children.Add(Label("Context type")); var completeness = new ComboBox { ItemsSource = new[] { new ContextChoice("Example sentence", R.VocabularyExampleCompleteness.Sentence), new ContextChoice("Context fragment", R.VocabularyExampleCompleteness.Fragment), new ContextChoice("Unspecified", R.VocabularyExampleCompleteness.Unknown) }, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = example.Completeness }; VocabularyNotebookTheme.Combo(completeness); AutomationProperties.SetName(completeness, "Context type"); completeness.SelectionChanged += (_, _) => { if (completeness.SelectedValue is R.VocabularyExampleCompleteness value) ChangeExample(example.Id, e => e with { Completeness = value }); }; panel.Children.Add(completeness); panel.Children.Add(Action("Delete example", () => { ChangeSense(s => s with { Examples = s.Examples.Where(e => e.Id != example.Id).ToArray() }); QueueRanges([]); RenderExamples(); })); _examples.Children.Add(Card(panel)); }
    }
}
