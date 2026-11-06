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
internal sealed class LearningShowcasePlayer : StackPanel
{
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
    private Scope? _scope;
    private bool _updating, _mounted;
    private long _epoch;
    internal Task Retirement { get; private set; } = Task.CompletedTask;
    internal Task Mounted { get; private set; } = Task.CompletedTask;
    internal ComboBox ScenePicker => _scenes;
    internal ComboBox StepPicker => _steps;

    internal LearningShowcasePlayer(Func<string>? trigger = null, bool autoplayOnHover = true)
    {
        _trigger = trigger ?? (() => "??");
        _scenes.ItemsSource = LearningShowcaseCatalog.Scenes.Select(s => s.Title).ToArray();
        System.Windows.Automation.AutomationProperties.SetName(_scenes, "Language learning scene");
        System.Windows.Automation.AutomationProperties.SetName(_steps, "Demo steps");
        Children.Add(_scenes);
        var shell = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        shell.Children.Add(new TextBlock { Text = "Interactive demo · sample content", FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
        var controls = new DockPanel(); DockPanel.SetDock(_title, Dock.Left); controls.Children.Add(_title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var control in new Control[] { _steps, _previous, _play, _next, _replay }) { control.Margin = new Thickness(3, 0, 0, 0); buttons.Children.Add(control); }
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
        Loaded += (_, _) => { Mounted = MountAsync(); };
        Unloaded += (_, _) => Unmount();
        IsVisibleChanged += (_, _) => { if (!IsVisible) { _playback.Pause(); RefreshControls(); } };
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
            scope = new Scope(Dispatcher, _trigger); _scope = scope;
            scope.Interacted += Interact; scope.Inserted += () => { _playback.Go(3); Stage(); };
            scope.LookupRequested += () => { _playback.Select(3); Stage(); };
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
    private void Interact() { _playback.Interact(); RefreshControls(); }
    private void RefreshControls()
    {
        _updating = true;
        try
        {
            _scenes.SelectedIndex = _playback.Scene;
            _steps.ItemsSource = _playback.CurrentScene.Steps.Select((s, i) => $"{i + 1}. {s.Title}").ToArray(); _steps.SelectedIndex = _playback.Step;
            _title.Text = _playback.CurrentStep.Title; _description.Text = _playback.CurrentStep.Description;
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
        private readonly VocabularyCapture _capture = new(Translation, Translation, LearningLanguage.English, VocabularyEntryKind.Sentence,
            Source, LearningLanguage.Chinese);
        private readonly VocabularyCapture _phrase = new("keep me posted", "keep someone posted", LearningLanguage.English, VocabularyEntryKind.Pattern,
            "随时告知某人最新进展", LearningLanguage.Chinese,
            new VocabularyExample(Guid.NewGuid(), Translation, Source, [new(7, 14)], VocabularyExampleCompleteness.Sentence,
                "随时告知某人最新进展", DateTimeOffset.UnixEpoch, LearningLanguage.Chinese, meaningLanguage: LearningLanguage.Chinese));
        private readonly HashSet<Task> _work = [];
        internal event Action? Interacted, Inserted;
        internal event Action? LookupRequested;
        private bool _closed;
        private bool _dictionaryActive;
        private bool _expanded;
        private long _dictionaryEpoch;
        private int _readingSource;

        internal Scope(Dispatcher dispatcher, Func<string> trigger)
        {
            Store = new(dispatcher); _explanation = new(_explainer); _dictionary = new(new LearningShowcaseDictionary(), _explainer);
            _speech = new LearningShowcaseSpeech().CreateController();
            _expression = new(trigger); _expression.Interacted += Interact; _expression.Inserted += () => Inserted?.Invoke();
            _card = _preview.TakeExampleCard(); _preview.AttachSpeech(_speech); _writing.Children.Add(_editor); _writing.Children.Add(_silentNote); _writing.Children.Add(_card);
            var sources = new WrapPanel();
            string[] labels = ["Selected text", "Text under pointer", "Screen region"];
            for (int i = 0; i < labels.Length; i++)
            {
                int source = i; var button = new Button { Content = labels[i], Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(7, 4, 7, 4) };
                button.Click += (_, _) => { Interact(); ShowReading(source, false); }; sources.Children.Add(button);
            }
            var translate = new Button { Content = "Translate sample", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4) };
            translate.Click += (_, _) => { Interact(); ShowReading(_readingSource, true); };
            _reading.Children.Add(sources); _reading.Children.Add(_readingText); _reading.Children.Add(translate); _reading.Children.Add(_readingCard);
            _dictionaryCard = _dictionaryView.TakeExampleCard();
            _card.PreviewMouseDown += (_, _) => Interact(); _card.PreviewKeyDown += (_, _) => Interact();
            _dictionaryCard.PreviewMouseDown += (_, _) => Interact(); _dictionaryCard.PreviewKeyDown += (_, _) => Interact();
            _dictionary.StateChanged += state => dispatcher.BeginInvoke(() =>
            {
                if (_closed || !_dictionaryActive || !ReferenceEquals(state, _dictionary.State)) return;
                _dictionaryView.ShowState(state, ScreenRect.Empty);
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
            _explanation.StateChanged += state => dispatcher.BeginInvoke(() => { if (!_closed) _preview.ShowExplanationState("example", state, true, ScreenRect.Empty); });
        }
        private void Interact() { ++_dictionaryEpoch; Interacted?.Invoke(); }
        internal FrameworkElement Stage(int scene, int step)
        {
            _explanation.Cancel();
            _speech.Stop();
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
            if (scene is not (0 or 4)) return new TextBlock { Text = "This example is not yet available.", TextWrapping = TextWrapping.Wrap };
            _readingCard.Content = null;
            if (!_writing.Children.Contains(_card)) _writing.Children.Add(_card);
            _silentNote.Visibility = scene == 4 ? Visibility.Visible : Visibility.Collapsed;
            _editor.Text = "谢谢你的帮助。" + Source + "我们下周再确认。"; _card.Visibility = Visibility.Visible;
            var state = new LearningPreviewState { RequestId = "example", Source = Source, Result = Translation,
                Status = LearningPreviewStatus.Ready, Options = new() { SourceLanguage = LearningLanguage.Chinese, TargetLanguage = LearningLanguage.English },
                UnitStage = LearningUnitStage.Ready, Units = [ExampleUnit()] };
            _preview.ShowState(state, ScreenRect.Empty, false); _expanded = scene == 4 ? step == 2 : step is 1 or 2;
            _preview.ShowExpanded(_expanded); _preview.ShowReplacementState(scene == 0, false, null);
            _preview.BindVocabulary(Store.Writing, _capture, "example", () => !_closed);
            var input = new LearningExplanationInput(Source, Source, LearningLanguage.Chinese, VocabularyEntryKind.Sentence, Source, [new(0, Source.Length)]);
            _explanation.SetTarget(input); _preview.ShowExplanationState("example", _explanation.State, true, ScreenRect.Empty);
            if (scene == 0 && step == 2) _ = _explanation.ExplainAsync();
            if (scene == 0 && step is 3 or 4) Track(Store.Writing.SaveAsync(_capture));
            return _writing;
        }
        private void ShowReading(int source, bool show)
        {
            _readingSource = source;
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
            if (step == 3) await _dictionary.SelectSourceAsync(DictionarySource.System, identity);
            if (_closed || !_dictionaryActive || epoch != _dictionaryEpoch) return;
            if (step is 4 or 5) await Store.Writing.SaveAsync(_phrase);
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
            lock (_work) _work.Add(task);
            _ = task.ContinueWith(t => { _ = t.Exception; lock (_work) _work.Remove(t); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        public async ValueTask DisposeAsync()
        {
            if (_closed) return; _closed = true; _preview.RetireExample(); _dictionary.Close(); _dictionaryView.HideCard();
            await _dictionary.DisposeAsync(); await _explanation.DisposeAsync();
            Task[] work; lock (_work) work = _work.ToArray(); try { await Task.WhenAll(work); } catch { }
            await _speech.DisposeAsync(); await Store.DisposeAsync();
        }
    }
}
