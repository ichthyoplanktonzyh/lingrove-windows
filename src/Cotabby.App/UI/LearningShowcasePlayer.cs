using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.UI.Learning;
using Cotabby.App.Speech;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI;

/// <summary>One mounted example owns its scheduler, real card/session adapters and isolated data.
/// Its actions never use the live editor, AppHost Store, clipboard, model or network.</summary>
internal sealed class LearningShowcasePlayer : StackPanel, IAsyncDisposable
{
    private static readonly HashSet<LearningShowcasePlayer> MountedPlayers = [];
    private const string Source = "请随时告诉我项目的最新进展。";
    private const string Translation = "Please keep me posted on the project's progress.";
    private readonly LearningShowcasePlayback _playback = new();
    private readonly ComboBox _scenes = new(), _steps = new();
    private readonly Button _previous = new() { Content = "‹", ToolTip = "Previous step" }, _play = new() { Content = "▶", ToolTip = "Play demo" },
        _next = new() { Content = "›", ToolTip = "Next step" }, _replay = new() { Content = "↻", ToolTip = "Replay demo" };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, _description = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl _content = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private readonly Func<string> _trigger;
    private readonly bool _silentSpeech;
    private readonly Func<Cotabby.App.Settings.AppSettings> _current;
    private Scope? _scope;
    private bool _updating, _mounted;
    private long _epoch;
    private bool _disposed;
    internal Task Retirement { get; private set; } = Task.CompletedTask;
    internal Task Mounted { get; private set; } = Task.CompletedTask;
    internal ComboBox ScenePicker => _scenes;
    internal ComboBox StepPicker => _steps;
    internal Task StageWork => _scope?.Pending ?? Task.CompletedTask;
    internal FrameworkElement? SceneContent => _content.Content as FrameworkElement;

    internal LearningShowcasePlayer(Func<string>? trigger = null, bool autoplayOnHover = true, bool silentSpeech = false, Func<Cotabby.App.Settings.AppSettings>? current = null)
    {
        _silentSpeech = silentSpeech;
        _current = current ?? (() => new Cotabby.App.Settings.AppSettings());
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
        _description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _scenes.SetResourceReference(StyleProperty, "Combo"); _steps.SetResourceReference(StyleProperty, "Combo");
        _steps.MaxWidth = 250;
        _trigger = trigger ?? (() => "??");
        _scenes.ItemsSource = LearningShowcaseCatalog.Scenes.Select(s => Cotabby.App.Localization.InterfaceText.Get(s.Title)).ToArray();
        System.Windows.Automation.AutomationProperties.SetName(_scenes, "Language learning scene");
        System.Windows.Automation.AutomationProperties.SetName(_steps, "Demo steps");
        Children.Add(_scenes);
        var shell = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        shell.Children.Add(new TextBlock { Text = "Interactive demo · sample content", FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
        var controls = new StackPanel(); controls.Children.Add(_title);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var control in new Control[] { _steps, _previous, _play, _next, _replay }) { control.Margin = new Thickness(3, 0, 0, 0); buttons.Children.Add(control); }
        foreach (var button in new[] { _previous, _play, _next, _replay }) button.SetResourceReference(StyleProperty, "Btn");
        controls.Children.Add(buttons); shell.Children.Add(controls);
        _description.Margin = new Thickness(0, 8, 0, 8); shell.Children.Add(_description);
        shell.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 0, 0, 8), Background = System.Windows.Media.Brushes.LightGray });
        shell.Children.Add(new ScrollViewer { Content = _content, MinHeight = 260, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Children.Add(shell);
        foreach (var button in new[] { _previous, _play, _next, _replay }) System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
        _scenes.SelectionChanged += (_, _) => { if (_updating || _scenes.SelectedIndex < 0) return; _playback.Select(_scenes.SelectedIndex); Stage(); };
        _steps.SelectionChanged += (_, _) => { if (_updating || _steps.SelectedIndex < 0) return; _playback.Go(_steps.SelectedIndex); Stage(); };
        _previous.Click += (_, _) => { _playback.Previous(); Stage(); };
        _next.Click += (_, _) => { _playback.Next(); Stage(); };
        _play.Click += (_, _) => { if (_playback.IsPlaying) _playback.Interact(); else _playback.Play(); RefreshControls(); };
        _replay.Click += (_, _) => { _playback.Replay(!SystemParameters.ClientAreaAnimation); Stage(); };
        _timer.Tick += (_, _) => { if (_playback.Tick()) Stage(); else RefreshControls(); };
        if (autoplayOnHover)
        {
            MouseEnter += (_, _) => { _playback.Hover(true, !SystemParameters.ClientAreaAnimation); RefreshControls(); };
            MouseLeave += (_, _) => { _playback.Hover(false, !SystemParameters.ClientAreaAnimation); RefreshControls(); };
        }
        Loaded += (_, _) => { if (!_disposed && !_mounted) { MountedPlayers.Add(this); Mounted = MountAsync(); } };
        Unloaded += (_, _) => Unmount();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { Unmount(); RefreshControls(); }
            else if (IsLoaded && !_disposed && !_mounted) { MountedPlayers.Add(this); Mounted = MountAsync(); }
        };
        RefreshControls();
    }

    private async Task MountAsync()
    {
        if (_mounted) return; _mounted = true; long epoch = ++_epoch;
        await Retirement;
        if (!_mounted || epoch != _epoch) return;
        Scope scope;
        try
        {
            scope = new Scope(Dispatcher, _trigger, _silentSpeech, _current); _scope = scope;
            scope.Interacted += Interact; scope.Inserted += () => { _playback.Go(3); Stage(); };
            scope.LookupRequested += () => { _playback.Select(3); Stage(); };
            scope.OpenSaved += selection => { _playback.Select(5); Stage(); scope.RevealSaved(selection); };
            scope.Failed += stage => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_scope, scope) && scope.IsStageCurrent(stage)) _description.Text = "This sample action could not be completed. Choose another step or replay the example."; });
            await scope.Store.PrepareAsync();
        }
        catch { if (_mounted && epoch == _epoch) _description.Text = "The sample notebook could not be prepared."; return; }
        if (_mounted && epoch == _epoch) Stage();
    }
    private void Unmount()
    {
        _mounted = false; ++_epoch; _playback.Pause(); _timer.Stop(); _content.Content = null;
        if (_scope is { } scope) { _scope = null; Retirement = scope.DisposeAsync().AsTask(); }
    }
    public async ValueTask DisposeAsync()
    {
        _disposed = true; Unmount();
        try { await Mounted; await Retirement; }
        finally { MountedPlayers.Remove(this); }
    }
    internal static async Task RetireAllAsync()
    {
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
        { await app.Dispatcher.InvokeAsync(RetireAllAsync).Task.Unwrap(); return; }
        await Task.WhenAll(MountedPlayers.ToArray().Select(player => player.DisposeAsync().AsTask()));
    }
    private void Interact() { _playback.Interact(); RefreshControls(); }
    private void RefreshControls()
    {
        _updating = true;
        try
        {
            _scenes.SelectedIndex = _playback.Scene;
            _steps.ItemsSource = _playback.CurrentScene.Steps.Select((s, i) => $"{i + 1}. {Cotabby.App.Localization.InterfaceText.Get(s.Title)}").ToArray(); _steps.SelectedIndex = _playback.Step;
            _title.Text = Cotabby.App.Localization.InterfaceText.Get(_playback.CurrentStep.Title); _description.Text = Cotabby.App.Localization.InterfaceText.Get(_playback.CurrentStep.Description);
            _previous.IsEnabled = _playback.Step > 0; _next.IsEnabled = _playback.Step + 1 < _playback.CurrentScene.Steps.Count;
            _play.IsEnabled = SystemParameters.ClientAreaAnimation; _play.Content = _playback.IsPlaying ? "Ⅱ" : "▶";
            _play.ToolTip = _playback.IsPlaying ? "Pause demo" : "Play demo";
            System.Windows.Automation.AutomationProperties.SetName(_play, (string)_play.ToolTip);
            if (_playback.IsPlaying && _mounted && IsVisible) _timer.Start(); else _timer.Stop();
        }
        finally { _updating = false; }
    }
    private void Stage()
    {
        RefreshControls(); if (_scope is not { } scope) return;
        _content.Content = scope.Stage(_playback.Scene, _playback.Step);
    }

    private sealed class Scope : IAsyncDisposable
    {
        internal readonly LearningShowcaseStore Store;
        private readonly LearningExpressionExample _expression;
        private readonly LearningPreviewWindow _preview = new(true);
        private readonly FrameworkElement _card;
        private readonly LearningShowcaseExplainer _explainer = new();
        private readonly LearningExplanationSession _explanation;
        private readonly LearningDictionarySession _dictionary;
        private readonly LearningDictionaryWindow _dictionaryView = new(true);
        private readonly FrameworkElement _dictionaryCard;
        private readonly StackPanel _writing = new();
        private readonly StackPanel _reading = new();
        private readonly TextBlock _readingText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
        private readonly ContentControl _readingCard = new();
        private readonly TextBlock _editor = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
        private readonly TextBlock _silentNote = new() { Text = "Silent sample: no audio is played.", FontSize = 11, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
        private readonly LearningSpeechController _speech;
        private readonly LearningShowcaseSpeech _speechOwner;
        private readonly LearningShowcaseVocabulary _vocabulary;
        private LearningShowcaseReview? _review;
        private readonly VocabularyCapture _capture = new(Translation, Translation, LearningLanguage.English, VocabularyEntryKind.Sentence,
            Source, LearningLanguage.Chinese);
        private readonly VocabularyCapture _phrase = new("keep me posted", "keep someone posted", LearningLanguage.English, VocabularyEntryKind.Pattern,
            "随时告知某人最新进展", LearningLanguage.Chinese,
            new VocabularyExample(Guid.NewGuid(), Translation, Source, [new(7, 14)], VocabularyExampleCompleteness.Sentence,
                "随时告知某人最新进展", DateTimeOffset.UnixEpoch, LearningLanguage.Chinese, meaningLanguage: LearningLanguage.Chinese));
        private readonly HashSet<Task> _work = [];
        internal event Action? Interacted, Inserted;
        internal event Action? LookupRequested;
        internal event Action<VocabularySelection>? OpenSaved;
        internal event Action<long>? Failed;
        internal bool IsStageCurrent(long stage) => !_closed && stage == _stageEpoch;
        internal void RevealSaved(VocabularySelection selection) => _vocabulary.Select(selection);
        private bool _closed;
        private bool _dictionaryActive;
        private bool _expanded;
        private long _dictionaryEpoch;
        private int _readingSource;
        private readonly Func<Cotabby.App.Settings.AppSettings> _current;
        private readonly Button _readingTranslate = new() { Content = "Translate sample", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4) };
        private long _stageEpoch;
        private Task _lastFailure = Task.CompletedTask;
        internal Task Pending { get { lock (_work) return Task.WhenAll(_work.Append(_lastFailure)); } }

        internal Scope(Dispatcher dispatcher, Func<string> trigger, bool silentSpeech, Func<Cotabby.App.Settings.AppSettings> current)
        {
            _current = current;
            Store = new(dispatcher); _explanation = new(_explainer); _dictionary = new(new LearningShowcaseDictionary(), _explainer);
            Store.Writing.OpenSelection = selection => { if (selection is { } target) OpenSaved?.Invoke(target); };
            _speechOwner = new LearningShowcaseSpeech(silentSpeech); _speech = _speechOwner.CreateController();
            _vocabulary = new(Store, () => (_review ??= new LearningShowcaseReview(Store, _speech, _phrase)).Feature, _explainer, new LearningShowcaseDictionary()); _vocabulary.Interacted += Interact;
            var expressionCaptures = new VocabularyCaptureFactory(new Cotabby.Win32.Text.IcuLearningLexicalText(),
                new Cotabby.Win32.Text.IcuLearningEvidenceSentenceTokenizer());
            _expression = new(trigger, Store.Writing, expressionCaptures, _speech);
            _expression.Interacted += Interact; _expression.Inserted += () => Inserted?.Invoke();
            _card = _preview.TakeExampleCard(); _preview.AttachSpeech(_speech); _writing.Children.Add(_editor); _writing.Children.Add(_silentNote); _writing.Children.Add(_card);
            var sources = new WrapPanel();
            string[] labels = ["Selected text", "Text under pointer", "Screen region"];
            for (int i = 0; i < labels.Length; i++)
            {
                int source = i; var button = new Button { Content = labels[i], Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(7, 4, 7, 4) };
                button.Click += (_, _) => { Interact(); ShowReading(source, false); }; sources.Children.Add(button);
            }
            _readingTranslate.Click += (_, _) => { Interact(); ShowReading(_readingSource, true); };
            _readingTranslate.SetResourceReference(StyleProperty, "Btn");
            _reading.Children.Add(sources); _reading.Children.Add(_readingText); _reading.Children.Add(_readingTranslate); _reading.Children.Add(_readingCard);
            _dictionaryCard = _dictionaryView.TakeExampleCard();
            _dictionaryView.AttachSession(_dictionary, () => !_closed && _dictionaryActive);
            _card.PreviewMouseDown += (_, _) => Interact(); _card.PreviewKeyDown += (_, _) => Interact();
            _dictionaryCard.PreviewMouseDown += (_, _) => Interact(); _dictionaryCard.PreviewKeyDown += (_, _) => Interact();
            _dictionary.StateChanged += state => dispatcher.BeginInvoke(() =>
            {
                if (_closed || !_dictionaryActive || !ReferenceEquals(state, _dictionary.State)) return;
                _dictionaryView.ShowState(state, ScreenRect.Empty); DescribeExample(_dictionaryView);
                if (_dictionaryView.FindName("AttributionText") is TextBlock attribution)
                    attribution.Text = "Fixed local sample entries · no dictionary service is contacted.";
                if (state.Unit is not null)
                {
                    var capture = state.SelectedWord is { } word ? WordCapture(word) : _phrase;
                    _dictionaryView.BindVocabulary(Store.Writing, capture, state.TargetIdentity, () => !_closed && _dictionaryActive);
                }
            });
            _dictionaryView.PageRequested += (page, id) => { Interact(); Track(_dictionary.SelectPageAsync(page, id)); };
            _dictionaryView.SourceRequested += (source, id) => { Interact(); Track(_dictionary.SelectSourceAsync(source, id)); };
            _dictionaryView.WordRequested += (word, id) => { Interact(); Track(_dictionary.SelectWordAsync(word, id)); };
            _dictionaryView.BackRequested += id => { Interact(); Track(_dictionary.ReturnToExpressionAsync(id)); };
            _dictionaryView.ExplanationRequested += (id, state) => { Interact(); Track(_dictionary.ExplainAsync(id, state, true)); };
            _dictionaryView.CloseRequested += id => { Interact(); _dictionary.Close(id); _dictionaryView.HideCard(); };
            _preview.ExpandRequested += _ => { Interact(); _expanded = !_expanded; _preview.ShowExpanded(_expanded); };
            _preview.DismissRequested += () => { Interact(); _card.Visibility = Visibility.Collapsed; };
            _preview.ReplaceRequested += _ => { Interact(); _editor.Text = "谢谢你的帮助。" + Translation + "我们下周再确认。"; _preview.ShowReplacementState(false, false, null); };
            _preview.ExplanationRequested += (documentId, state) => { Interact(); _ = _explanation.ExplainAsync(expectedState: state); };
            _preview.LookupRequested += (unit, document, anchor) => { Interact(); LookupRequested?.Invoke(); };
            _explanation.StateChanged += state => dispatcher.BeginInvoke(() => { if (!_closed) { _preview.ShowExplanationState("example", state, true, ScreenRect.Empty); DescribeExample(_preview); } });
        }
        private void Interact() { ++_dictionaryEpoch; ++_stageEpoch; Interacted?.Invoke(); }
        internal FrameworkElement Stage(int scene, int step)
        {
            long stage = ++_stageEpoch;
            lock (_work) _lastFailure = Task.CompletedTask;
            CloseMenu(_preview); CloseMenu(_dictionaryView);
            _explanation.Cancel();
            _speech.Stop();
            if (scene != 1) _expression.Suspend();
            if (scene != 3 && _dictionaryActive) { _dictionaryActive = false; ++_dictionaryEpoch; _dictionary.Close(); _dictionaryView.HideCard(); }
            if (scene == 1) { _expression.SetStep(step); return _expression; }
            if (scene == 3)
            {
                _dictionaryActive = true; long epoch = ++_dictionaryEpoch;
                Track(StageDictionaryAsync(step, epoch)); return _dictionaryCard;
            }
            if (scene == 2)
            {
                if (_writing.Children.Contains(_card)) _writing.Children.Remove(_card);
                _readingCard.Content = _card;
                int source = step >= 4 ? 2 : step >= 2 ? 1 : 0;
                ShowReading(source, step is 1 or 3 or 5 or 6); _expanded = step == 6; _preview.ShowExpanded(_expanded); return _reading;
            }
            if (scene == 5) { Track(ShowVocabularyAsync()); return _vocabulary; }
            if (scene == 6)
            {
                _review ??= new LearningShowcaseReview(Store, _speech, _phrase);
                Track(_review.ShowStepAsync(step)); return _review;
            }
            _readingCard.Content = null;
            if (!_writing.Children.Contains(_card)) _writing.Children.Add(_card);
            _silentNote.Visibility = Visibility.Collapsed;
            _editor.Text = "谢谢你的帮助。" + Source + "我们下周再确认。"; _card.Visibility = Visibility.Visible;
            var state = new LearningPreviewState { RequestId = "example", Source = Source, Result = Translation,
                Status = LearningPreviewStatus.Ready, Options = new() { SourceLanguage = LearningLanguage.Chinese, TargetLanguage = LearningLanguage.English },
                UnitStage = LearningUnitStage.Ready, Units = [ExampleUnit()] };
            _preview.ShowState(state, ScreenRect.Empty, false); _expanded = scene == 4 ? step == 2 : step is 1 or 2;
            _preview.ShowExpanded(_expanded); _preview.ShowReplacementState(scene == 0, false, null);
            _preview.BindVocabulary(Store.Writing, _capture, "example", () => !_closed);
            var input = new LearningExplanationInput(Source, Source, LearningLanguage.Chinese, VocabularyEntryKind.Sentence, Source, [new(0, Source.Length)]);
            _explanation.SetTarget(input); _preview.ShowExplanationState("example", _explanation.State, true, ScreenRect.Empty);
            DescribeExample(_preview);
            if (scene == 0 && step == 2) Track(_explanation.ExplainAsync());
            if (scene == 0 && step is 3 or 4) Track(SaveExampleAsync(_capture, _preview, stage, step == 4));
            if (scene == 0 && step is 7 or 8) Track(DemonstrateEditAsync());
            return _writing;
            async Task ShowVocabularyAsync()
            {
                await _vocabulary.RefreshAsync();
                if (!_closed && stage == _stageEpoch) _vocabulary.ShowStep(step);
            }
            async Task DemonstrateEditAsync()
            {
                await Task.Delay(1500);
                if (_closed || stage != _stageEpoch) return;
                if (step == 7) _editor.Text = "谢谢你的帮助。" + Translation + "我们下周再确认。";
                else { _card.Visibility = Visibility.Collapsed; _explanation.Cancel(); _speech.Stop(); }
            }
        }
        private static void CloseMenu(Window window)
        { if (window.FindName("FavoriteButton") is Control { ContextMenu: { } menu }) menu.IsOpen = false; }
        private static void DescribeExample(Window view)
        { if (view.FindName("ExplanationDestination") is TextBlock label) label.Text = "Sample explanation · fixed local content"; }
        private async Task SaveExampleAsync(VocabularyCapture capture, Window view, long stage, bool showMenu)
        {
            await Store.Writing.SaveAsync(capture);
            if (_closed || stage != _stageEpoch || !showMenu || view.FindName("FavoriteButton") is not Cotabby.App.UI.Vocabulary.VocabularyFavoriteButton button) return;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await button.LastOperation;
        }
        private void ShowReading(int source, bool show)
        {
            _readingSource = source;
            var settings = _current();
            var binding = source == 1 ? settings.LearningReadingPointerShortcut : source == 2 ? settings.LearningReadingScreenshotShortcut : settings.LearningReadingShortcut;
            _readingTranslate.Content = binding.IsBound ? binding.Label : "Translate sample";
            string sourceText = source switch { 1 => "posted", 2 => Translation + "\nI'll send an update tomorrow.", _ => Translation };
            string translated = source switch { 1 => "获知最新情况", 2 => Source + "\n我明天会发送更新。", _ => Source };
            _readingText.Text = source == 1 ? "Thanks for your help. " + Translation : sourceText;
            _card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            _preview.ShowState(new LearningPreviewState { RequestId = "reading-sample", Source = sourceText, Result = translated,
                Status = LearningPreviewStatus.Ready, Options = new() { SourceLanguage = LearningLanguage.English, TargetLanguage = LearningLanguage.Chinese } }, ScreenRect.Empty, false);
            _preview.ShowReplacementState(false, false, null);
        }
        private async Task StageDictionaryAsync(int step, long epoch)
        {
            var unit = ExampleUnit();
            if (_dictionary.State.Unit is null)
                await _dictionary.OpenAsync(unit, "example", LearningLanguage.English, Translation,
                    new LearningExplanationInput("keep me posted", "keep someone posted", LearningLanguage.English,
                        VocabularyEntryKind.Pattern, Translation, [new(7, 14)], LearningExplanationContext.Sentence));
            if (_closed || !_dictionaryActive || epoch != _dictionaryEpoch) return;
            var identity = _dictionary.State.TargetIdentity;
            await _dictionary.SelectPageAsync(step is 2 or 3 ? LearningDictionaryPage.Dictionary : LearningDictionaryPage.Contextual, identity);
            if (_closed || !_dictionaryActive || epoch != _dictionaryEpoch) return;
            if (step == 1 && _dictionary.State.Explanation is { } explanation)
                await _dictionary.ExplainAsync(identity, explanation, true);
            if (_closed || !_dictionaryActive || epoch != _dictionaryEpoch) return;
            if (step == 3) await _dictionary.SelectSourceAsync(DictionarySource.System, identity);
            if (_closed || !_dictionaryActive || epoch != _dictionaryEpoch) return;
            if (step is 4 or 5) await SaveExampleAsync(_phrase, _dictionaryView, _stageEpoch, step == 5);
        }
        private static LearningUnit ExampleUnit() => new(0, "keep me posted", "keep someone posted", LearningUnitKind.Pattern,
            [new(7, 14)], [new("keep", new(7, 4)), new("me", new(12, 2)), new("posted", new(15, 6))]);
        private static VocabularyCapture WordCapture(LearningDictionaryWord word)
        {
            var example = new VocabularyExample(Guid.NewGuid(), Translation, Source, [new(word.Range.Location, word.Range.Length)],
                VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch, LearningLanguage.Chinese);
            return new(word.Text, word.Text, LearningLanguage.English, VocabularyEntryKind.Word, Example: example);
        }
        private void Track(Task task)
        {
            long stage = _stageEpoch;
            lock (_work) _work.Add(task);
            _ = task.ContinueWith(t =>
            {
                _ = t.Exception; bool report;
                lock (_work) { report = t.IsFaulted && !_closed && stage == _stageEpoch; if (report) _lastFailure = t; _work.Remove(t); }
                if (report) Failed?.Invoke(stage);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        public async ValueTask DisposeAsync()
        {
            if (_closed) return; _closed = true; CloseMenu(_preview); CloseMenu(_dictionaryView); _preview.RetireExample(); _dictionary.Close(); _dictionaryView.HideCard();
            var errors = new List<Exception>();
            async Task Join(Func<ValueTask> dispose) { try { await dispose(); } catch (Exception error) { errors.Add(error); } }
            await Join(_dictionary.DisposeAsync); await Join(_explanation.DisposeAsync); await Join(_expression.DisposeAsync);
            Task[] work; lock (_work) work = _work.ToArray(); try { await Task.WhenAll(work); } catch { }
            await Join(_vocabulary.DisposeAsync);
            if (_review is not null) await Join(_review.DisposeAsync);
            await Join(_speech.DisposeAsync); await Join(_speechOwner.RetireAsync); await Join(Store.DisposeAsync);
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }
}
