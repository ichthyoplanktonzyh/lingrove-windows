using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// One owned modal editor holds a detached full-entry draft and its fixed acknowledged baseline.
/// It edits one stable sense; untouched senses/media/review remain in that value. Cancel has no
/// side effects. Save borrows Store's three-way update, closing only after durable acknowledgement.
/// Lifetime cancellation retires presentation, never an already admitted archive transaction.
/// </summary>
public partial class VocabularyEntryEditorWindow : Window
{
    private readonly VocabularyStore _store;
    private readonly VocabularyEntry _baseline;
    private readonly Guid _senseId;
    private readonly CancellationTokenSource _lifetime = new();
    private VocabularyEntry _draft;
    private bool _saving, _retired, _forceClose;
    private sealed record ContextOption(VocabularyExampleCompleteness Value, string Label);
    internal Task LastOperation { get; private set; } = Task.CompletedTask;
    internal event Action<Task>? OperationStarted;
    internal VocabularyEntry Draft => _draft;
    private VocabularySense? Sense => _draft.Senses.FirstOrDefault(value => value.Id == _senseId);

    internal VocabularyEntryEditorWindow(VocabularyStore store, VocabularyEntry entry, Guid senseId)
    {
        _store = store; _baseline = entry; _draft = entry; _senseId = senseId; InitializeComponent(); WindowTheme.Follow(this);
        ExpressionText.Text = entry.Expression; DictionaryText.Text = entry.DictionaryForm;
        MeaningText.Text = Sense?.Meaning ?? ""; NoteText.Text = Sense?.Note ?? ""; TagsText.Text = string.Join(", ", Sense?.Tags ?? []);
        ExpressionText.TextChanged += (_, _) => { _draft = _draft with { Expression = ExpressionText.Text }; UpdateAdmission(); };
        DictionaryText.TextChanged += (_, _) => { _draft = _draft with { DictionaryForm = DictionaryText.Text }; UpdateAdmission(); };
        MeaningText.TextChanged += (_, _) => ChangeSense(value => value with { Meaning = MeaningText.Text });
        NoteText.TextChanged += (_, _) => ChangeSense(value => value with { Note = NoteText.Text });
        RenderBlocks(); RenderExamples(); UpdateAdmission();
        Closing += (_, args) => { if (_saving && !_forceClose) args.Cancel = true; };
        Closed += (_, _) =>
        {
            _retired = true; _lifetime.Cancel(); RetireImages();
            _ = LastOperation.ContinueWith(_ => _lifetime.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        };
    }
    private void ChangeSense(Func<VocabularySense, VocabularySense> change)
    {
        if (_saving || _retired) return;
        _draft = _draft with { Senses = _draft.Senses.Select(value => value.Id == _senseId ? change(value) : value).ToArray() };
        PruneImports();
        UpdateAdmission();
    }
    private void ChangeExample(Guid id, Func<VocabularyExample, VocabularyExample> change) =>
        ChangeSense(sense => sense with { Examples = sense.Examples.Select(value => value.Id == id ? change(value) : value).ToArray() });
    private void ChangeBlock(Guid id, Func<VocabularyBlockContent, VocabularyBlockContent> change) =>
        ChangeSense(sense => sense with { Content = (sense.Content ?? []).Select(block => block.Id == id ? block with { Content = change(block.Content) } : block).ToArray() });
    private void UpdateAdmission()
    {
        SaveButton.IsEnabled = !_retired && VocabularyEntryEditingPolicy.CanSave(_draft, _senseId, _saving, _importing);
        AddTextButton.IsEnabled = AddImageButton.IsEnabled = !_saving && !_retired && !_importing && (Sense?.Content?.Count ?? 0) < VocabularyContentPolicy.MaximumBlocks;
    }
    private void OnCancel(object sender, RoutedEventArgs args) { if (!_saving && !_retired) Close(); }
    private void OnSave(object sender, RoutedEventArgs args)
    {
        if (_retired || !VocabularyEntryEditingPolicy.CanSave(_draft, _senseId, _saving, _importing)) return;
        var tags = VocabularyEntryEditingPolicy.ParseTags(TagsText.Text);
        var value = _draft with { Senses = _draft.Senses.Select(sense => sense.Id == _senseId ? sense with { Tags = tags } : sense).ToArray() };
        _saving = true; FormContent.IsEnabled = false; CancelButton.IsEnabled = false; SaveButton.IsEnabled = false;
        FailureText.Visibility = Visibility.Collapsed; LastOperation = PersistAsync(value, _lifetime.Token); OperationStarted?.Invoke(LastOperation);
    }
    private async Task PersistAsync(VocabularyEntry value, CancellationToken token)
    {
        bool saved; string? failure;
        try { saved = await _store.UpdateAsync(value, _baseline, _importedImages).ConfigureAwait(false); failure = _store.ErrorMessage; }
        catch (Exception) { saved = false; failure = new VocabularyException(VocabularyFailure.InvalidContent).Message; }
        if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_retired) return;
                _saving = false;
                if (saved) Close();
                else
                {
                    FailureText.Text = failure ?? new VocabularyException(VocabularyFailure.InvalidContent).Message;
                    FailureText.Visibility = Visibility.Visible; FormContent.IsEnabled = true; CancelButton.IsEnabled = true; UpdateAdmission();
                }
            }, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess();
        if (!_retired) { _forceClose = true; Close(); }
        return LastOperation;
    }
    private TextBox Editor(string text, string label, Action<string> changed, bool multiline = true)
    {
        var box = new TextBox { Text = text, Style = (Style)FindResource("Input"), TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = multiline, MinHeight = multiline ? 50 : 0, Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetName(box, label); box.TextChanged += (_, _) => changed(box.Text); return box;
    }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 3) };
    private static Button ActionButton(string label, Action action) { var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0) }; button.Click += (_, _) => action(); return button; }
    private Border Card(UIElement child)
    {
        var border = new Border { Child = child, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 10) };
        border.SetResourceReference(BackgroundProperty, "CardBg"); return border;
    }
    private void RenderBlocks()
    {
        ClearImageViews();
        BlocksContent.Children.Clear(); var blocks = Sense?.Content ?? [];
        foreach (var block in blocks)
        {
            var panel = new StackPanel(); var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            var up = ActionButton("Move up", () => MoveBlock(block.Id, -1)); up.IsEnabled = block.Id != blocks[0].Id;
            var down = ActionButton("Move down", () => MoveBlock(block.Id, 1)); down.IsEnabled = block.Id != blocks[^1].Id;
            toolbar.Children.Add(up); toolbar.Children.Add(down);
            toolbar.Children.Add(ActionButton("Delete block", () => { ChangeSense(sense => sense with { Content = (sense.Content ?? []).Where(value => value.Id != block.Id).ToArray() }); RenderBlocks(); }));
            panel.Children.Add(toolbar);
            if (block.Content is VocabularyBlockContent.Text text)
            {
                var formatting = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
                var style = new ComboBox { ItemsSource = Enum.GetValues<VocabularyTextStyle>(), SelectedItem = text.Value.Style, Width = 130, Style = (Style)FindResource("Combo") };
                AutomationProperties.SetName(style, "Text style"); style.SelectionChanged += (_, _) =>
                { if (style.SelectedItem is VocabularyTextStyle selected) ChangeBlock(block.Id, value => value is VocabularyBlockContent.Text old ? new VocabularyBlockContent.Text(old.Value with { Style = selected }) : value); };
                formatting.Children.Add(style);
                foreach (bool bold in new[] { true, false })
                {
                    var toggle = new CheckBox { Content = bold ? "Bold" : "Italic", IsChecked = bold ? text.Value.Bold : text.Value.Italic, Margin = new Thickness(10, 3, 0, 0) };
                    toggle.Click += (_, _) => ChangeBlock(block.Id, value => value is VocabularyBlockContent.Text old
                        ? new VocabularyBlockContent.Text(bold ? old.Value with { Bold = toggle.IsChecked == true } : old.Value with { Italic = toggle.IsChecked == true }) : value);
                    formatting.Children.Add(toggle);
                }
                panel.Children.Add(formatting); panel.Children.Add(Editor(text.Value.Text, "Explanation text", changed =>
                    ChangeBlock(block.Id, value => value is VocabularyBlockContent.Text old ? new VocabularyBlockContent.Text(old.Value with { Text = changed }) : value)));
            }
            else if (block.Content is VocabularyBlockContent.Image image)
            {
                _importedImages.TryGetValue(image.Reference.Filename, out var draft);
                var view = new VocabularyImageView(_store, image.Reference, draft, image.Caption); _imageViews.Add(view);
                panel.Children.Add(view); panel.Children.Add(Editor(image.Caption, "Caption", changed =>
                {
                    ChangeBlock(block.Id, value => value is VocabularyBlockContent.Image old ? old with { Caption = changed } : value);
                    view.SetCaption(changed);
                }));
            }
            BlocksContent.Children.Add(Card(panel));
        }
        UpdateAdmission();
    }
    private void MoveBlock(Guid id, int offset) { ChangeSense(sense => sense with { Content = VocabularyEntryEditingPolicy.MoveBlock(sense.Content, id, offset) }); RenderBlocks(); }
    private void OnAddText(object sender, RoutedEventArgs args)
    {
        if (_saving || _retired || _importing || (Sense?.Content?.Count ?? 0) >= VocabularyContentPolicy.MaximumBlocks) return;
        ChangeSense(sense => sense with { Content = (sense.Content ?? []).Append(VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new()))).ToArray() }); RenderBlocks();
    }
    private void OnAddExample(object sender, RoutedEventArgs args)
    {
        if (_saving || _retired) return;
        ChangeSense(sense => sense with { Examples = sense.Examples.Append(new(Guid.NewGuid(), "", "", [], VocabularyExampleCompleteness.Fragment, "",
            DateTimeOffset.UtcNow, translationLanguage: sense.MeaningLanguage)).ToArray() }); RenderExamples();
    }
    private void RenderExamples()
    {
        ExamplesContent.Children.Clear();
        foreach (var example in Sense?.Examples ?? [])
        {
            var panel = new StackPanel(); panel.Children.Add(Label("Example sentence"));
            TextBox? translation = null, meaning = null; ComboBox? completeness = null; bool presenting = false;
            panel.Children.Add(Label("Original text")); panel.Children.Add(Editor(example.Text, "Original text", changed =>
            {
                ChangeExample(example.Id, value => VocabularyEntryEditingPolicy.ChangeOriginalText(value, changed));
                if (Sense?.Examples.FirstOrDefault(value => value.Id == example.Id) is not { } current) return;
                presenting = true;
                try { translation!.Text = current.Translation; meaning!.Text = current.Meaning; completeness!.SelectedValue = current.Completeness; }
                finally { presenting = false; }
            }));
            panel.Children.Add(Label("Translation")); translation = Editor(example.Translation, "Translation", changed => { if (!presenting) ChangeExample(example.Id, value => value with { Translation = changed }); }); panel.Children.Add(translation);
            panel.Children.Add(Label("Meaning in this context")); meaning = Editor(example.Meaning, "Meaning in this context", changed => { if (!presenting) ChangeExample(example.Id, value => value with { Meaning = changed }); }); panel.Children.Add(meaning);
            panel.Children.Add(Label("Context type")); completeness = new ComboBox { ItemsSource = new[] {
                new ContextOption(VocabularyExampleCompleteness.Sentence, "Example sentence"), new ContextOption(VocabularyExampleCompleteness.Fragment, "Context fragment") },
                DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = example.Completeness, Style = (Style)FindResource("Combo") };
            AutomationProperties.SetName(completeness, "Context type"); completeness.SelectionChanged += (_, _) =>
            { if (!presenting && completeness.SelectedValue is VocabularyExampleCompleteness selected) ChangeExample(example.Id, value => value with { Completeness = selected }); };
            panel.Children.Add(completeness);
            panel.Children.Add(ActionButton("Delete example", () => { ChangeSense(sense => sense with { Examples = sense.Examples.Where(value => value.Id != example.Id).ToArray() }); RenderExamples(); }));
            ExamplesContent.Children.Add(Card(panel));
        }
    }
}
