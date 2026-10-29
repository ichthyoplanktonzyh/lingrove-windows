using Cotabby.App.Localization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
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
    private readonly IVocabularyClipboardImageTransfer? _clipboard;
    private readonly StackPanel _explanation = new() { Name = "canonicalExplanationDropArea" };
    private readonly List<CanonicalVocabularyImageView> _imageViews = [];
    private WindowsVocabularyFileDrop? _fileDrop;
    private readonly F.VocabularyEntry _baseline;
    private F.VocabularyEntry _draft;
    private readonly Guid _senseId;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, VocabularyPreparedImage> _images = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _work = [];
    private readonly TextBox _expression = new() { Name = "canonicalEditExpression" }, _dictionary = new(), _meaning = new(), _note = new(), _tags = new();
    private readonly StackPanel _form = new(), _blocks = new(), _examples = new();
    private readonly TextBlock _failure = VocabularyNotebookTheme.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    private readonly Button _save = new() { Name = "canonicalEditSave", Content = InterfaceText.Get("Save"), IsDefault = true, MinWidth = 90 }, _cancel = new() { Content = InterfaceText.Get("Cancel"), IsCancel = true, MinWidth = 90 }, _addText = new() { Content = InterfaceText.Get("Add text") }, _addImage = new() { Content = InterfaceText.Get("Add image") }, _addExample = new() { Content = InterfaceText.Get("Add example") }, _paste = new() { Content = InterfaceText.Get("Paste image") };
    private bool _saving, _retiring, _allowClose, _importing, _rangesPending, _rangeFailed;
    private long _rangeEpoch;
    private readonly Dictionary<Guid, (Guid Id, string Text, string Expression, string Form)> _rangeInputs = [];
    private CancellationTokenSource? _rangeToken;
    private TaskCompletionSource? _retirement;
    internal F.VocabularyEntry Draft => _draft;
    internal Task? LastOperation { get; private set; }
    internal bool Saved { get; private set; }
    private F.VocabularySense? Sense => _draft.Senses.FirstOrDefault(s => s.Id == _senseId);

    internal FrozenVocabularyEntryEditorWindow(ICanonicalVocabularyEditorStore editor, ICanonicalVocabularyMediaStore media, F.VocabularyEntry entry, Guid senseId, IReadOnlyList<string>? suggestedTags = null, IVocabularyClipboardImageTransfer? clipboard = null)
    {
        _editor = editor; _media = media; _clipboard = clipboard; _baseline = entry; _draft = entry; _senseId = senseId;
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
        _form.Children.Add(_explanation); _explanation.Children.Add(VocabularyInputControls.Heading(InterfaceText.Get("Sense explanation"))); _explanation.Children.Add(_blocks);
        var blockActions = new WrapPanel(); foreach (var b in new[] { _addText, _addImage }) { VocabularyNotebookTheme.Button(b); b.Margin = new Thickness(0, 0, 8, 8); blockActions.Children.Add(b); } _explanation.Children.Add(blockActions);
        VocabularyNotebookTheme.Button(_paste); _paste.Margin = new Thickness(0, 0, 8, 8); blockActions.Children.Add(_paste); _paste.Click += (_, _) => PasteImage();
        CommandManager.AddPreviewCanExecuteHandler(_explanation, (_, e) => { if (e.Command == ApplicationCommands.Paste && _clipboard?.Capture().HasImage == true) { e.CanExecute = CanImport(); e.Handled = true; } });
        CommandManager.AddPreviewExecutedHandler(_explanation, (_, e) => { if (e.Command == ApplicationCommands.Paste && _clipboard?.Capture() is { HasImage: true } offer) { e.Handled = true; ImportClipboard(offer); } });
        SourceInitialized += (_, _) => AttachFileDrop(); Loaded += (_, _) => AttachFileDrop();
        _form.Children.Add(_examples); VocabularyNotebookTheme.Button(_addExample); _addExample.HorizontalAlignment = HorizontalAlignment.Left; _form.Children.Add(_addExample);
        _expression.TextChanged += (_, _) => { bool changed = !CanonicalStringComparer.Instance.Equals(_draft.Expression, _expression.Text); _draft = _draft with { Expression = _expression.Text }; title.Text = _expression.Text.Length == 0 ? _baseline.Expression : _expression.Text; if (changed) QueueRanges(Sense!.Examples); Update(); };
        _dictionary.TextChanged += (_, _) => _draft = _draft with { DictionaryForm = _dictionary.Text };
        _meaning.TextChanged += (_, _) => ChangeSense(s => s with { Meaning = _meaning.Text }); _note.TextChanged += (_, _) => ChangeSense(s => s with { Note = _note.Text });
        _save.Click += (_, _) => LastOperation = SaveAsync(); _cancel.Click += (_, _) => Close();
        _addText.Click += (_, _) => { ChangeSense(s => s with { Content = (s.Content ?? []).Append(VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new()))).ToArray() }); RenderBlocks(); };
        _addImage.Click += (_, _) => { var dialog = new OpenFileDialog { Title = InterfaceText.Get("Add image"), Filter = "Images|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif;*.ico;*.webp;*.heic;*.heif", Multiselect = false }; if (dialog.ShowDialog(this) == true) ImportImage(dialog.FileName); };
        _addExample.Click += (_, _) => { ChangeSense(s => s with { Examples = s.Examples.Append(new F.VocabularyExample(Guid.NewGuid(), "", "", [], R.VocabularyExampleCompleteness.Fragment, "", SourceDate.FromWindows(DateTimeOffset.UtcNow)) { SourceKind = R.VocabularySourceKind.Authored, TranslationLanguage = s.MeaningLanguage }).ToArray() }); RenderExamples(); };
        Closing += (_, e) => { if (_saving && !_allowClose) e.Cancel = true; }; Closed += (_, _) => { _retiring = true; _lifetime.Cancel(); _rangeToken?.Cancel(); _fileDrop?.Dispose(); _fileDrop = null; foreach (var view in _imageViews) view.Dispose(); };
        RenderBlocks(); RenderExamples(); Update();
    }
    private void ChangeSense(Func<F.VocabularySense, F.VocabularySense> change) { if (_retiring || _saving) return; _draft = _draft with { Senses = _draft.Senses.Select(s => s.Id == _senseId ? change(s) : s).ToArray() }; Update(); }
    private void ChangeExample(Guid id, Func<F.VocabularyExample, F.VocabularyExample> change) => ChangeSense(s => s with { Examples = s.Examples.Select(e => e.Id == id ? change(e) : e).ToArray() });
    private void ChangeBlock(Guid id, Func<VocabularyBlockContent, VocabularyBlockContent> change) => ChangeSense(s => s with { Content = (s.Content ?? []).Select(b => b.Id == id ? b with { Content = change(b.Content) } : b).ToArray() });
    private void Update() { bool ready = !_saving && !_retiring; _form.IsEnabled = ready; _cancel.IsEnabled = ready; _save.IsEnabled = ready && !_importing && !_rangesPending && !_rangeFailed && !string.IsNullOrWhiteSpace(_draft.Expression) && Sense is { } s && s.Examples.All(e => !string.IsNullOrWhiteSpace(e.Text)); _addText.IsEnabled = _addImage.IsEnabled = ready && !_importing && (Sense?.Content?.Count ?? 0) < VocabularyContentPolicy.MaximumBlocks; _paste.IsEnabled = _addImage.IsEnabled && _clipboard is not null; }
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
        _retiring = true; _lifetime.Cancel(); _rangeToken?.Cancel(); _fileDrop?.Dispose(); _fileDrop = null; foreach (var view in _imageViews) Track(view.RetireAsync()); Update();
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
        foreach (var view in _imageViews) Track(view.RetireAsync()); _imageViews.Clear(); _blocks.Children.Clear(); var blocks = Sense?.Content ?? [];
        foreach (var block in blocks) {
            var panel = new StackPanel(); var tools = new WrapPanel(); var up = Action("Move up", () => Move(block.Id, -1)); up.IsEnabled = block.Id != blocks[0].Id; var down = Action("Move down", () => Move(block.Id, 1)); down.IsEnabled = block.Id != blocks[^1].Id; tools.Children.Add(up); tools.Children.Add(down); tools.Children.Add(Action("Delete block", () => { ChangeSense(s => s with { Content = (s.Content ?? []).Where(b => b.Id != block.Id).ToArray() }); PruneImages(); RenderBlocks(); })); panel.Children.Add(tools);
            if (block.Content is VocabularyBlockContent.Text text) { var format = new WrapPanel(); var style = new ComboBox { ItemsSource = Enum.GetValues<VocabularyTextStyle>(), SelectedItem = text.Value.Style, Width = 130 }; VocabularyNotebookTheme.Combo(style); AutomationProperties.SetName(style, "Text style"); style.SelectionChanged += (_, _) => { if (style.SelectedItem is VocabularyTextStyle v) ChangeBlock(block.Id, c => c is VocabularyBlockContent.Text t ? new VocabularyBlockContent.Text(t.Value with { Style = v }) : c); }; format.Children.Add(style); foreach (bool bold in new[] { true, false }) { var toggle = new CheckBox { Content = bold ? "Bold" : "Italic", IsChecked = bold ? text.Value.Bold : text.Value.Italic, Margin = new Thickness(10, 3, 0, 0) }; toggle.Click += (_, _) => ChangeBlock(block.Id, c => c is VocabularyBlockContent.Text t ? new VocabularyBlockContent.Text(bold ? t.Value with { Bold = toggle.IsChecked == true } : t.Value with { Italic = toggle.IsChecked == true }) : c); format.Children.Add(toggle); } panel.Children.Add(format); panel.Children.Add(Field(text.Value.Text, "Explanation text", value => ChangeBlock(block.Id, c => c is VocabularyBlockContent.Text t ? new VocabularyBlockContent.Text(t.Value with { Text = value }) : c))); }
            else if (block.Content is VocabularyBlockContent.Image image) { _images.TryGetValue(image.Reference.Filename, out var draft); var view = new CanonicalVocabularyImageView(_media, image.Reference, draft, image.Caption); _imageViews.Add(view); panel.Children.Add(view); panel.Children.Add(Field(image.Caption, "Caption", value => { view.SetCaption(value); ChangeBlock(block.Id, c => c is VocabularyBlockContent.Image i ? i with { Caption = value } : c); })); }
            _blocks.Children.Add(Card(panel));
        } Update();
    }
    private void Move(Guid id, int offset) { ChangeSense(s => s with { Content = VocabularyEntryEditingPolicy.MoveBlock(s.Content, id, offset) }); RenderBlocks(); }
    private void PruneImages() { var names = VocabularyContentPolicy.Images(Sense?.Content ?? []).Select(i => i.Filename).ToHashSet(StringComparer.Ordinal); foreach (var key in _images.Keys.Where(k => !names.Contains(k)).ToArray()) _images.Remove(key); }
    private bool CanImport() => !_retiring && !_saving && !_importing && (Sense?.Content?.Count ?? 0) < VocabularyContentPolicy.MaximumBlocks;
    internal void ImportImage(string path) => BeginImport(async token => await _media.PrepareImageFileAsync(path, token));
    internal bool PasteImage() => _clipboard?.Capture() is { HasImage: true } offer && ImportClipboard(offer);
    private bool ImportClipboard(VocabularyClipboardImageOffer offer) => BeginImport(async token => { var bytes = await _clipboard!.ReadAsync(offer.Sequence, token); token.ThrowIfCancellationRequested(); return bytes is null ? null : await _media.PrepareImageAsync(bytes, token); });
    private bool BeginImport(Func<CancellationToken, Task<VocabularyPreparedImage?>> prepare)
    {
        if (!CanImport()) return false; _importing = true; _failure.Text = ""; Update(); Track(Import()); return true;
        async Task Import() { try { var prepared = await Task.Run(() => prepare(_lifetime.Token)); if (_retiring || prepared is null) return; var blocks = (Sense!.Content ?? []).Append(VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference))).ToArray(); VocabularyContentPolicy.Validate(blocks); _images[prepared.Reference.Filename] = prepared; ChangeSense(s => s with { Content = blocks }); RenderBlocks(); } catch (OperationCanceledException) { } catch { if (!_retiring) _failure.Text = InterfaceText.Get("Unable to add this image. Try a smaller image."); } finally { _importing = false; Update(); } }
    }
    private void AttachFileDrop()
    {
        if (_retiring || _fileDrop is not null || HwndSource.FromVisual(this) is not HwndSource source) return;
        _fileDrop = new(source, (path, point) => { if (IsEnabled && IsVisible && CanImport() && source.CompositionTarget is not null && source.RootVisual is Visual root && VocabularyEntryEditorWindow.ContainsDropPoint(_explanation, root, point, source.CompositionTarget.TransformFromDevice)) ImportImage(path); });
    }
    private void RenderExamples()
    {
        _examples.Children.Clear(); foreach (var example in Sense?.Examples ?? []) { var panel = new StackPanel(); panel.Children.Add(Label("Example sentence")); panel.Children.Add(Label("Original text")); panel.Children.Add(Field(example.Text, "Original text", value => { bool changed = !CanonicalStringComparer.Instance.Equals(Sense!.Examples.First(e => e.Id == example.Id).Text, value); ChangeExample(example.Id, e => e with { Text = value }); if (changed) QueueRanges([Sense!.Examples.First(e => e.Id == example.Id)]); })); panel.Children.Add(Label("Translation")); panel.Children.Add(Field(example.Translation, "Translation", value => ChangeExample(example.Id, e => e with { Translation = value }))); panel.Children.Add(Label("Meaning in this context")); panel.Children.Add(Field(example.Meaning, "Meaning in this context", value => ChangeExample(example.Id, e => e with { Meaning = value }))); panel.Children.Add(Label("Context type")); var completeness = new ComboBox { ItemsSource = new[] { new ContextChoice("Example sentence", R.VocabularyExampleCompleteness.Sentence), new ContextChoice("Context fragment", R.VocabularyExampleCompleteness.Fragment), new ContextChoice("Unspecified", R.VocabularyExampleCompleteness.Unknown) }, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = example.Completeness }; VocabularyNotebookTheme.Combo(completeness); AutomationProperties.SetName(completeness, "Context type"); completeness.SelectionChanged += (_, _) => { if (completeness.SelectedValue is R.VocabularyExampleCompleteness value) ChangeExample(example.Id, e => e with { Completeness = value }); }; panel.Children.Add(completeness); panel.Children.Add(Action("Delete example", () => { ChangeSense(s => s with { Examples = s.Examples.Where(e => e.Id != example.Id).ToArray() }); QueueRanges([]); RenderExamples(); })); _examples.Children.Add(Card(panel)); }
    }
}
