using Cotabby.App.Localization;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Cotabby.App.Review;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Cotabby.Inference.Speech;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Review;

/// <summary>A window-local preparation draft. All asynchronous work is admitted by the app's
/// shared review feature; closing cancels presentation work and drains accepted work before the
/// borrowed session is retired. This window owns neither the Store nor the controller.</summary>
internal sealed partial class CanonicalAnkiReviewWindow : Window
{
    private sealed record Option(string Label, Guid? Id) { public override string ToString() => Label; }
    private sealed record PlanOption(string Label, ReviewPlan Plan) { public override string ToString() => Label; }
    private readonly CanonicalAnkiReviewSession _session;
    private readonly Func<Func<CancellationToken, Task>, Task> _admit;
    private readonly Action _configure;
    private readonly IVocabularyClipboardImageTransfer? _clipboard;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _form = new(), _preview = new(), _conflicts = new();
    private readonly ComboBox _binding = new(), _example = new(), _preserved = new(), _deck = new(), _cardType = new(), _side = new();
    private readonly TextBox _deckText = new();
    private readonly TextBox _answer = new() { AcceptsReturn = true, MinHeight = 55, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _recognition = new() { Content = InterfaceText.Get("Recognition card") }, _gap = new() { Content = InterfaceText.Get("Gap card") },
        _notes = new() { Content = InterfaceText.Get("Include notes") }, _senseContent = new() { Content = InterfaceText.Get("Include sense explanation") },
        _explanation = new() { Content = InterfaceText.Get("Include AI explanation") };
    private readonly CheckBox _expressionAudio = new() { Name = "canonicalExpressionAudio", Content = InterfaceText.Get("Expression audio") }, _sentenceAudio = new() { Name = "canonicalSentenceAudio", Content = InterfaceText.Get("Example audio") };
    private readonly Button _previewExpressionAudio = new() { Content = InterfaceText.Get("Preview audio") }, _previewSentenceAudio = new() { Content = InterfaceText.Get("Preview audio") }, _regenerateExpressionAudio = new() { Content = InterfaceText.Get("Regenerate audio") }, _regenerateSentenceAudio = new() { Content = InterfaceText.Get("Regenerate audio") };
    private ReviewSpeechDisclosure? _shownSpeech;
    private readonly TextBlock _speechDestination = new() { Name = "canonicalReviewSpeechDestination", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    internal TextBlock SpeechDestination => _speechDestination;
    private readonly TextBlock _destination = new() { TextWrapping = TextWrapping.Wrap }, _state = new() { TextWrapping = TextWrapping.Wrap }, _validation = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _resetFields = new() { Content = InterfaceText.Get("Use notebook fields again") };
    internal Button SaveButton => _save;
    internal CheckBox ExpressionAudioToggle => _expressionAudio;
    internal Button PreviewExpressionAudio => _previewExpressionAudio;
    internal Button SendButton => _send;
    internal Button ClearAudioButton => _clearAudio;
    internal TextBox AnswerBox => _answer;
    internal TextBlock StateText => _state;
    internal TextBlock ValidationText => _validation;
    internal StackPanel ConflictsPanel => _conflicts;
    internal StackPanel PreviewPanel => _preview;
    internal ComboBox SideBox => _side;
    internal Button SettingsButton => _settings;
    private readonly Button _save = new() { Content = InterfaceText.Get("Save"), Name = "reviewSave", MinWidth = 90 },
        _send = new() { Content = InterfaceText.Get("Add to Anki"), Name = "reviewSend", IsDefault = true, MinWidth = 110 },
        _cancel = new() { Content = InterfaceText.Get("Cancel"), IsCancel = true, MinWidth = 90 },
        _settings = new() { Content = InterfaceText.Get("Review settings") }, _clearAudio = new() { Content = "Remove audio requests" },
        _reload = new() { Content = "Refresh connection" }, _updateTemplate = new() { Content = InterfaceText.Get("Update Lingrove template") };
    private readonly Dictionary<string, bool> _choices = new(StringComparer.Ordinal);
    private Task? _operation, _retirement;
    private Task _previewTask = Task.CompletedTask;
    private bool _closeWhenDone, _configureWhenDone;
    private bool _updating, _closing, _allowClose, _started, _retired;
    private readonly bool _embeddedExample;
    private readonly bool _allowExternalMedia;
    private long _previewVersion;
        private ulong _seenRevision;
    private string? _loadedAddress, _windowError, _windowNotice;
    private IReadOnlyList<AnkiFieldConflict>? _seenConflicts;
    internal Task? LastOperation => _operation;
    internal Task PreviewOperation => _previewTask;
    internal Guid? CardId => _session.CardId;

    internal CanonicalAnkiReviewWindow(CanonicalAnkiReviewSession session, Func<Func<CancellationToken, Task>, Task> admit, Action configure, IVocabularyClipboardImageTransfer? clipboard = null, bool embeddedExample = false, bool allowExternalMedia = true)
    {
        if (session.CardId is null && session.Plan.Layout is not null) throw new NotSupportedException("Custom review card layout preparation is not available yet.");
        _session = session; _clipboard = clipboard; _admit = admit; _configure = configure; _embeddedExample = embeddedExample;
        _allowExternalMedia = allowExternalMedia && !embeddedExample;
        Title = "Prepare review"; Width = 720; Height = 760; MinWidth = 560; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        VocabularyNotebookTheme.Follow(this);
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var heading = VocabularyInputControls.Heading("Prepare review"); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(_speechDestination, Dock.Top); root.Children.Add(_speechDestination);
        if (_session.Audio.Speech is ISpeechDestinationDisclosure disclosure) disclosure.PropertyChanged += SpeechDestinationChanged;
        RefreshSpeechDestination();
        var footer = VocabularyInputControls.Footer(_settings, _cancel, _save, _send); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var deckControl = new StackPanel(); deckControl.Children.Add(_deck); deckControl.Children.Add(_deckText); VocabularyNotebookTheme.Input(_deckText); VocabularyNotebookTheme.Combo(_deck);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _form }; root.Children.Add(scroll);
        var destination = VocabularyInputControls.Group(("Destination", _destination), ("Association", _binding), ("Deck", deckControl), ("", _reload));
        _side.ItemsSource = new[] { "Front", "Back" }; _side.SelectedIndex = 0;
        if (session.CardId is not null) { BuildLayoutControls(); _form.Children.Add(_layoutPanel); _form.Children.Add(destination); }
        else
        {
            _form.Children.Add(destination);
            _form.Children.Add(VocabularyInputControls.Group(("Review example", _example), ("Preserved plan", _preserved), ("Review answer", _answer), ("", _recognition), ("", _gap), ("", _notes), ("", _senseContent), ("", _explanation), ("", _resetFields), ("", _clearAudio), ("", _updateTemplate)));
        }
        int audioIndex = _form.Children.Count;
        if (_session.Audio.Speech is null) _form.Children.Add(VocabularyInputControls.Caption("Review audio is unavailable. Remove any requested audio before sending."));
        _cardType.ItemsSource = new[] { "Recognition card", "Gap card" }; _cardType.SelectedIndex = 0;
        _form.Children.Add(VocabularyInputControls.Heading(InterfaceText.Get("Card preview")));
        if (session.CardId is null) _form.Children.Add(VocabularyInputControls.Group(("Card type", _cardType), ("Side", _side)));
        _form.Children.Add(_preview);
        _form.Children.Add(_conflicts); _form.Children.Add(_validation); _form.Children.Add(_state);
        VocabularyNotebookTheme.Brush(_state, TextBlock.ForegroundProperty, "OrangeBrush");
        if (_session.CardId is null)
        {
            var expressionRow = new WrapPanel(); expressionRow.Children.Add(_expressionAudio); expressionRow.Children.Add(_previewExpressionAudio); expressionRow.Children.Add(_regenerateExpressionAudio);
            var sentenceRow = new WrapPanel(); sentenceRow.Children.Add(_sentenceAudio); sentenceRow.Children.Add(_previewSentenceAudio); sentenceRow.Children.Add(_regenerateSentenceAudio);
            _form.Children.Insert(audioIndex, VocabularyInputControls.Heading(InterfaceText.Get("Audio"))); _form.Children.Insert(audioIndex + 1, VocabularyInputControls.Group(("", expressionRow), ("", sentenceRow)));
            foreach (var button in new[] { _previewExpressionAudio, _previewSentenceAudio, _regenerateExpressionAudio, _regenerateSentenceAudio }) { VocabularyNotebookTheme.Button(button); button.Margin = new Thickness(8, 0, 0, 4); }
            void Toggle(ReviewAudioKind kind, CheckBox box) { if (_updating) return; var p = _session.Plan; var kinds = (p.AudioKinds ?? new HashSet<ReviewAudioKind>()).ToHashSet(); if (box.IsChecked == true) kinds.Add(kind); else kinds.Remove(kind); var overrides = new Dictionary<string, string>(p.FieldOverrides); if (_session.Mapping.TryGetValue(kind.Field(), out var field)) overrides.Remove(field); _session.Plan = p with { AudioKinds = kinds, FieldOverrides = overrides }; Fill(); RefreshDraft(); }
            _expressionAudio.Click += (_, _) => Toggle(ReviewAudioKind.Expression, _expressionAudio); _sentenceAudio.Click += (_, _) => Toggle(ReviewAudioKind.Sentence, _sentenceAudio);
            _previewExpressionAudio.Click += (_, _) => Start(token => _session.PrepareAudioAsync(ReviewAudioKind.Expression, true, token: token));
            _previewSentenceAudio.Click += (_, _) => Start(token => _session.PrepareAudioAsync(ReviewAudioKind.Sentence, true, token: token));
            _regenerateExpressionAudio.Click += (_, _) => Start(token => _session.PrepareAudioAsync(ReviewAudioKind.Expression, false, true, token));
            _regenerateSentenceAudio.Click += (_, _) => Start(token => _session.PrepareAudioAsync(ReviewAudioKind.Sentence, false, true, token));
        }
        foreach (var button in new[] { _reload, _clearAudio, _resetFields, _updateTemplate }) VocabularyNotebookTheme.Button(button);
        _binding.SelectionChanged += (_, _) => { if (_updating) return; _session.SelectBinding((_binding.SelectedItem as Option)?.Id); _choices.Clear(); Start(LoadAsync); };
        _example.SelectionChanged += (_, _) => { if (_updating) return; var id = (_example.SelectedItem as Option)?.Id; var p = _session.Plan; var audio = p.AudioKinds?.Where(k => id is not null || k != ReviewAudioKind.Sentence).ToHashSet(); var (entry, sense) = Current(); var example = sense.Examples.FirstOrDefault(e => e.Id == id); bool canGap = example is not null && ReviewCardPolicy.Gap(example.Text, example.Ranges, entry.Kind) is not null; _session.Plan = p with { ExampleId = id, Gap = p.Gap && canGap, FieldOverrides = new Dictionary<string, string>(), AudioKinds = audio }; Fill(); RefreshDraft(); };
        _cardType.SelectionChanged += (_, _) => { if (!_updating) RefreshDraft(); }; _side.SelectionChanged += (_, _) => { if (!_updating) { if (_session.CardId is not null) RenderLayout(); RefreshDraft(); } };
        _preserved.SelectionChanged += (_, _) => { if (!_updating && _preserved.SelectedItem is PlanOption plan) { if (plan.Plan.Layout is not null) { _state.Text = "Custom review layouts are not available yet."; return; } _session.RestorePlan(plan.Plan); _previewVersion++; Fill(); RefreshDraft(); } };
        _answer.TextChanged += (_, _) => { if (!_updating) { _session.Plan = _session.Plan with { Answer = _answer.Text }; RefreshDraft(); } };
        _deckText.TextChanged += (_, _) => { if (!_updating) { _session.Deck = _deckText.Text; RefreshDraft(); } };
        _deck.SelectionChanged += (_, _) => { if (!_updating && _deck.SelectedItem is string value) { _session.Deck = value; RefreshDraft(); } };
        Hook(_recognition, p => p with { Recognition = _recognition.IsChecked == true }); Hook(_gap, p => p with { Gap = _gap.IsChecked == true });
        Hook(_notes, p => p with { IncludeNotes = _notes.IsChecked == true }); Hook(_senseContent, p => p with { IncludeSenseContent = _senseContent.IsChecked == true }); Hook(_explanation, p => p with { IncludeExplanation = _explanation.IsChecked == true });
        _resetFields.Click += (_, _) => { _session.Plan = _session.Plan with { FieldOverrides = new Dictionary<string, string>() }; RefreshDraft(); };
        _clearAudio.Click += (_, _) =>
        {
            var p = _session.Plan; var overrides = new Dictionary<string, string>(p.FieldOverrides);
            if (p.AudioKinds?.Contains(ReviewAudioKind.Expression) == true && _session.Mapping.TryGetValue(ReviewField.ExpressionAudio, out var expression)) overrides.Remove(expression);
            if (p.AudioKinds?.Contains(ReviewAudioKind.Sentence) == true && _session.Mapping.TryGetValue(ReviewField.SentenceAudio, out var sentence)) overrides.Remove(sentence);
            _session.Plan = p with { AudioKinds = new HashSet<ReviewAudioKind>(), FieldOverrides = overrides }; _clearAudio.Visibility = Visibility.Collapsed; RefreshDraft();
        };
        _reload.Click += (_, _) => Start(LoadAsync); _updateTemplate.Click += (_, _) => Start(async token => { await _session.UpdateTemplateAsync(token); await LoadAsync(token); });
        _save.Click += (_, _) => Start(async token => { if (await _session.SaveAsync(token)) CloseAfterOperation(); else await PresentAsync(token); });
        _settings.Click += (_, _) => Start(async token => { if (await _session.SaveAsync(token)) { _configureWhenDone = true; CloseAfterOperation(); } else await PresentAsync(token); });
        _send.Click += (_, _) => Start(async token => { if (await _session.SendAsync(_session.MissingNote, cancellationToken: token)) CloseAfterOperation(); else await PresentAsync(token); });
        _cancel.Click += (_, _) => Close();
        SourceInitialized += (_, _) => AttachLayoutFileDrop();
        Loaded += (_, _) => { _session.PresentAudioDisclosure(_shownSpeech); AttachLayoutFileDrop(); if (!_started) { _started = true; Start(LoadAsync); } };
        Closing += OnClosing; _seenRevision = session.Snapshot.Revision; session.PropertyChanged += SessionChanged; session.Audio.PropertyChanged += AudioChanged; session.Controller.Settings.PropertyChanged += SettingsChanged; Closed += (_, _) => RetirePresentation();
        Fill();
    }
    private void SpeechDestinationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closing || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) RefreshSpeechDestination();
        else _ = Dispatcher.BeginInvoke(() => { if (!_closing) RefreshSpeechDestination(); });
    }
    private void RefreshSpeechDestination()
    {
        var shown = ReviewSpeechDisclosure.Capture(_session.Audio.Speech);
        if (Equals(_shownSpeech, shown) && _speechDestination.Text.Length != 0) return;
        _shownSpeech = shown;
        _speechDestination.Text = shown is null ? "Speech for review audio: unavailable. Configure speech before preparing audio." : "Speech for review audio: " + shown.Destination;
        _session.PresentAudioDisclosure(IsVisible ? shown : null);
        if (_started && !_closing) RefreshDraft();
    }
    private void AudioChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_closing && _session.Audio.Generating is { } kind) _state.Text = "Preparing " + kind.Label() + "…";
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closing || e.PropertyName is not (nameof(AnkiReviewSettings.Address) or nameof(AnkiReviewSettings.Model) or nameof(AnkiReviewSettings.Mapping))) return;
        _loadedAddress = null; Fill(); _validation.Text = "Refresh the Anki connection before sending."; UpdateEnabled();
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closing || _session.Snapshot.Revision <= _seenRevision) return;
        _seenRevision = _session.Snapshot.Revision;
        try { Fill(); RefreshDraft(); }
        catch (InvalidOperationException) { _validation.Text = "This sense is no longer available."; UpdateEnabled(); }
    }
    private void Hook(CheckBox box, Func<ReviewPlan, ReviewPlan> update)
    { box.Checked += Change; box.Unchecked += Change; void Change(object sender, RoutedEventArgs e) { if (!_updating) { _session.Plan = update(_session.Plan); RefreshDraft(); } } }
    private (F.VocabularyEntry Entry, F.VocabularySense Sense) Current()
    {
        var entry = _session.Snapshot.Document.CollectedEntries.First(e => e.Id == _session.EntryId);
        return (entry, entry.Senses.First(s => s.Id == _session.SenseId));
    }
    private void Fill()
    {
        _updating = true;
        try
        {
            var (entry, sense) = Current(); var p = _session.Plan; var review = _session.CardId is { } cardId ? sense.ReviewCards?.FirstOrDefault(r => r.Plan.Id == cardId) : sense.Review;
            _binding.Items.Clear(); _binding.Items.Add(new Option(review?.Bindings.Count > 0 ? "Choose…" : "New association", null));
            foreach (var b in review?.Bindings ?? []) _binding.Items.Add(new Option($"{b.Profile} · {(b.NoteId?.ToString() ?? "Ready to send")}", b.Id));
            _binding.SelectedItem = _binding.Items.Cast<Option>().FirstOrDefault(o => o.Id == _session.BindingId);
            _example.Items.Clear(); _example.Items.Add(new Option("No context", null));
            foreach (var e in sense.Examples) _example.Items.Add(new Option(e.Text, e.Id));
            _example.SelectedItem = _example.Items.Cast<Option>().FirstOrDefault(o => o.Id == p.ExampleId);
            _preserved.Items.Clear(); foreach (var plan in review?.PreservedPlans ?? []) _preserved.Items.Add(new PlanOption(plan.Answer ?? "Preserved plan", plan));
            if (_preserved.Items.Count == 0) { _preserved.Items.Add("No preserved plans"); _preserved.SelectedIndex = 0; _preserved.IsEnabled = false; } else _preserved.IsEnabled = true;
            _deck.Items.Clear(); foreach (string deck in (_session.Catalog?.Decks ?? []).Append(_session.Deck).Distinct()) _deck.Items.Add(deck); _deck.SelectedItem = _session.Deck; _deckText.Text = _session.Deck; _deck.Visibility = _session.Catalog is null ? Visibility.Collapsed : Visibility.Visible; _deckText.Visibility = _session.Catalog is null ? Visibility.Visible : Visibility.Collapsed;
            _deck.IsEnabled = _deckText.IsEnabled = _session.Binding?.NoteId is null;
            string answer = p.Answer ?? ""; if (p.Answer is null) { try { answer = CanonicalReviewCardPolicy.Content(entry, sense, p with { Recognition = true, Gap = false }).Answer; } catch (AnkiReviewException) { } } _answer.Text = answer; _recognition.IsChecked = p.Recognition; _recognition.IsEnabled = !_session.HasExistingCard(ReviewField.Recognition); _gap.IsChecked = p.Gap; var selectedExample = sense.Examples.FirstOrDefault(e => e.Id == p.ExampleId); bool canGap = selectedExample is not null && ReviewCardPolicy.Gap(selectedExample.Text, selectedExample.Ranges, entry.Kind) is not null; _gap.IsEnabled = !_session.HasExistingCard(ReviewField.GapPrompt) && (canGap || p.Gap); _notes.IsChecked = p.IncludeNotes; _senseContent.IsChecked = p.IncludeSenseContent == true; _explanation.IsChecked = p.IncludeExplanation;
            _destination.Text = $"{AnkiArchiveEndpointPolicy.Normalize(_session.Controller.Settings.Address) ?? "Invalid Anki address"}\nProfile: {(_loadedAddress == _session.Controller.Settings.Address ? _session.Catalog?.Profile : null) ?? "Not loaded"}\nNote type: {_session.Model}";
            _resetFields.Visibility = p.FieldOverrides.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _clearAudio.Visibility = (p.AudioKinds?.Count ?? 0) > 0 && _session.Audio.Speech is null ? Visibility.Visible : Visibility.Collapsed;
            _expressionAudio.IsChecked = p.AudioKinds?.Contains(ReviewAudioKind.Expression) == true; _sentenceAudio.IsChecked = p.AudioKinds?.Contains(ReviewAudioKind.Sentence) == true;
            _sentenceAudio.IsEnabled = selectedExample is not null || _sentenceAudio.IsChecked == true;
            _previewExpressionAudio.Visibility = _regenerateExpressionAudio.Visibility = _expressionAudio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            _previewSentenceAudio.Visibility = _regenerateSentenceAudio.Visibility = _sentenceAudio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            _updateTemplate.Visibility = _session.Model == AnkiReviewPolicy.PresetName && !_session.CurrentPresetTemplate && !_session.Loading ? Visibility.Visible : Visibility.Collapsed;
            if (_session.CardId is not null) RenderLayout();
            _state.Text = _windowError ?? _session.Message ?? _windowNotice ?? ""; _send.Content = _session.MissingNote ? "Re-add to Anki" : _session.Binding?.NoteId is null ? "Add to Anki" : "Update Anki";
            if (!ReferenceEquals(_seenConflicts, _session.Conflicts)) { _choices.Clear(); _seenConflicts = _session.Conflicts; }
            _conflicts.Children.Clear(); foreach (var conflict in _session.Conflicts)
            {
                _conflicts.Children.Add(VocabularyInputControls.Heading(InterfaceText.Get("Anki fields changed")));
                _conflicts.Children.Add(new TextBlock { Text = $"{conflict.Id}\nLingrove: {conflict.Local}\nAnki: {conflict.Remote}", TextWrapping = TextWrapping.Wrap });
                var choice = new ComboBox { ItemsSource = new[] { "Choose…", "Keep Lingrove", "Keep Anki" }, SelectedIndex = _choices.TryGetValue(conflict.Id, out bool remote) ? remote ? 2 : 1 : 0 };
                choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex > 0) { bool value = choice.SelectedIndex == 2; _choices[conflict.Id] = value; _session.SetKeepRemote(conflict.Id, value); UpdateEnabled(); } };
                _conflicts.Children.Add(choice);
            }
        }
        finally { _updating = false; }
        UpdateEnabled();
    }
    private void UpdateEnabled()
    {
        bool ready = !_closing && _operation is null && !_session.Busy; _form.IsEnabled = ready; _save.IsEnabled = ready; _settings.IsEnabled = ready; _cancel.IsEnabled = !_closing;
        _send.IsEnabled = ready && !_session.Loading && _validation.Text.Length == 0 && _session.Conflicts.All(c => _choices.ContainsKey(c.Id));
    }
    private void RefreshDraft()
    {
        _previewVersion++; _validation.Text = "Updating preview…"; UpdateEnabled();
        if (_closing) return;
        long requested = _previewVersion; var previous = _previewTask;
        _previewTask = Refresh();
        async Task Refresh()
        {
            try
            {
                await previous; if (requested != _previewVersion || _closing) return;
                await _admit(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(featureToken, _lifetime.Token); await PresentAsync(linked.Token); });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_closing) { _validation.Text = error.Message; UpdateEnabled(); } }
        }
    }
    private async Task LoadAsync(CancellationToken token)
    {
        string address = _session.Controller.Settings.Address, model = _session.Model; var mapping = _session.Controller.Settings.EffectiveMapping.ToDictionary(p => p.Key, p => p.Value);
        _loadedAddress = null; await _session.LoadMetadataAsync(token);
        if (_session.Message is null && address == _session.Controller.Settings.Address && model == _session.Model && _session.Catalog is not null
            && mapping.Count == _session.Controller.Settings.EffectiveMapping.Count && mapping.All(p => _session.Controller.Settings.EffectiveMapping.TryGetValue(p.Key, out var value) && p.Value == value)) _loadedAddress = address;
        Fill(); await PresentAsync(token);
    }
    private async Task PresentAsync(CancellationToken token)
    {
        long version = _previewVersion; if (_session.CardId is not null) { await PresentLayoutAsync(version, token); return; }
        string? validation = await _session.ValidationMessageAsync(token); ReviewCardContent? content = null; try { content = await _session.ContentAsync(token); } catch (AnkiReviewException error) { validation ??= error.Message; }
        if (version != _previewVersion || _closing) return;
        _validation.Text = validation ?? (_loadedAddress == _session.Controller.Settings.Address ? "" : "Refresh the Anki connection before sending."); _preview.Children.Clear(); if (content is null) { UpdateEnabled(); return; }
        void Line(string label, string value) { if (value.Length != 0) _preview.Children.Add(new TextBlock { Text = label + ": " + value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) }); }
        if ((!_session.CurrentPresetTemplate && !_session.LegacyPresetTemplate) || _session.Plan.FieldOverrides.Count > 0)
        {
            try { var fields = AnkiReviewPolicy.Fields(content, _session.Binding?.OriginSenseId ?? _session.SenseId, _session.Mapping, _session.Plan.FieldOverrides, AnkiReviewPolicy.AudioFields(_session.Plan)); foreach (var field in fields.OrderBy(p => p.Key)) Line(field.Key, field.Value); }
            catch (AnkiReviewException error) { _validation.Text = error.Message; }
            UpdateEnabled(); return;
        }
        bool gap = _cardType.SelectedIndex == 1, back = _side.SelectedIndex == 1;
        if (gap && !content.Gap || !gap && !content.Recognition) Line("Card", "This card is not enabled.");
        else
        {
            if (gap) { Line("Gap", content.GapPrompt); if (_session.LegacyPresetTemplate) Line("Answer", content.Answer); }
            else Line(content.Sentence.Length == 0 ? "Expression" : "Example", content.Sentence.Length == 0 ? content.Expression : content.Sentence);
            if (back) { if (gap) Line("Example", content.Sentence); Line("Answer", content.Answer); }
        }
        if (!back || gap && !content.Gap || !gap && !content.Recognition) { UpdateEnabled(); return; }
        foreach (var block in content.SenseContent)
        {
            if (block.Content is VocabularyBlockContent.Text text) Line("Explanation", text.Value.Text);
            else if (block.Content is VocabularyBlockContent.Image image)
            {
                try { byte[] bytes = await _session.Store.ReadImageAsync(image.Reference, token); if (version != _previewVersion || _closing) return; using var stream = new MemoryStream(bytes); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); _preview.Children.Add(new Image { Source = bitmap, MaxWidth = 360, MaxHeight = 180, HorizontalAlignment = HorizontalAlignment.Left }); Line("Caption", image.Caption); }
                catch (OperationCanceledException) { throw; }
                catch { Line("Image", "Image unavailable. Add it again before sending."); }
            }
        }
        Line("Expression", content.Expression); Line("Translation", content.Translation); Line("Notes", content.Notes); Line("AI explanation", content.Explanation);
        UpdateEnabled();
    }
    private void Start(Func<CancellationToken, Task> action)
    {
        if (_closing || _operation is not null) return;
        _windowError = null; _windowNotice = null; var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _operation = completion.Task; UpdateEnabled();
        _ = Run(); async Task Run()
        {
            try { await _admit(async featureToken => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(featureToken, _lifetime.Token); await action(linked.Token); }); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_closing) { _windowError = error.Message; _state.Text = error.Message; _validation.Text = error.Message; } }
            finally { _operation = null; completion.TrySetResult(); if (_closeWhenDone && !_closing) { _ = RetireAsync(); } else if (!_closing) { try { Fill(); } catch (InvalidOperationException) { _validation.Text = "This sense is no longer available."; } UpdateEnabled(); } }
        }
    }
    private void CloseAfterOperation() { if (!_embeddedExample) _closeWhenDone = true; }
    internal FrameworkElement TakeExampleCard()
    {
        if (!_embeddedExample || Content is not FrameworkElement card) throw new InvalidOperationException();
        Content = null; _cancel.Visibility = _settings.Visibility = Visibility.Collapsed;
        card.Margin = new Thickness(0);
        foreach (string key in new[] { "NotebookInput", "NotebookAccentBtn", "NotebookCardBtn" })
            if (Resources[key] is Style style) card.Resources[key] = style;
        if (card is DockPanel dock && dock.Children.Count > 0) dock.Children[0].Visibility = Visibility.Collapsed;
        _form.Children.Remove(_preview); _form.Children.Insert(0, _preview);
        card.Loaded += (_, _) => { if (!_started) { _started = true; Start(LoadAsync); } };
        return card;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true; if (!_closing) _ = RetireAsync();
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess();
        if (_retirement is not null) return _retirement;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retirement = completion.Task;
        _ = RetireCoreAsync(completion);
        return completion.Task;
    }
    private async Task RetireCoreAsync(TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        _closing = true;
        try { _lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
        try { UpdateEnabled(); } catch (Exception error) { errors.Add(error); }
        try { if (_operation is { } operation) await operation; } catch (Exception error) { errors.Add(error); }
        try { await _previewTask; } catch (Exception error) { errors.Add(error); }
        bool closed = false;
        try { _allowClose = true; Close(); closed = true; } catch (Exception error) { errors.Add(error); }
        try { RetirePresentation(); } catch (Exception error) { errors.Add(error); }
        try { if (closed && _configureWhenDone) _configure(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count == 0) completion.TrySetResult(); else completion.TrySetException(errors);
    }
    private void RetirePresentation()
    {
        if (_retired) return; _retired = true;
        if (_session.Audio.Speech is ISpeechDestinationDisclosure disclosure) disclosure.PropertyChanged -= SpeechDestinationChanged;
        _session.PropertyChanged -= SessionChanged; _session.Audio.PropertyChanged -= AudioChanged; _session.Controller.Settings.PropertyChanged -= SettingsChanged;
        _layoutDrop?.Dispose(); _layoutDrop = null; _session.Dispose(); _lifetime.Dispose();
    }
}
