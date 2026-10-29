using Cotabby.App.Localization;
using LearningExplanationRecord = Cotabby.Core.LanguageLearning.LearningExplanationRecord;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.Learning;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using VocabularyEntry = Cotabby.Core.Vocabulary.Frozen.VocabularyEntry;
using VocabularySense = Cotabby.Core.Vocabulary.Frozen.VocabularySense;
using VocabularyExample = Cotabby.Core.Vocabulary.Frozen.VocabularyExample;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// One notebook occurrence borrows its window's stable explanation session. This adapts the Mac
/// contextual ExplanationSection/View: authored meaning leads, generated prose is a proposal in
/// Details, and only Replace asks the shared Store to adopt it. It owns controls/subscriptions,
/// never a model or editor. Same-input updates retain Details and selectable literal controls.
/// </summary>
internal sealed class CanonicalVocabularyExplanationRow : StackPanel, IDisposable
{
    private readonly ICanonicalVocabularyStore _store;
    private readonly CanonicalNotebookLearningSession _owner;
    private readonly LearningExplanationSession _session;
    private readonly string _key;
    private readonly Func<bool> _canAct;
    private readonly Action? _configure;
    private readonly Action<Task> _track;
    private readonly CancellationTokenSource _presentation = new();
    private readonly CancellationToken _presentationToken;
    private readonly TextBlock _heading = new() { FontSize = 12 };
    private readonly TextBlock _modelDestination = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
    private readonly Button _generate = new() { Width = 28, Height = 26, Padding = new Thickness(2) };
    private readonly TextBox _meaning = Literal(true), _proposal = Literal(true), _prose = Literal(false);
    private readonly Expander _details = new() { Header = InterfaceText.Get("Details"), Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _replace = new() { Content = InterfaceText.Get("Replace contextual meaning"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
    private LearningExplanationState? _displayed;
    private LearningExplanationInput? _input;
    private VocabularyExplanationDestination _destination;
    private string _authoredMeaning = "";
    private LearningLanguage? _authoredLanguage;
    private bool _standalone, _alwaysAuthored, _adopting;
    private volatile bool _disposed;
    private string? _adoptionError;
    internal LearningExplanationSession Session => _session;
    internal Task LastAdoption { get; private set; } = Task.CompletedTask;
    internal Task LastGeneration { get; private set; } = Task.CompletedTask;
    internal void Refresh() { Dispatcher.VerifyAccess(); Render(_session.State); }

    internal CanonicalVocabularyExplanationRow(ICanonicalVocabularyStore store, CanonicalNotebookLearningSession owner, string key,
        LearningExplanationSession session, Func<bool> canAct, Action<Task> track, Action? configure)
    {
        _store = store; _owner = owner; _key = key; _session = session; _canAct = canAct; _track = track; _configure = configure;
        _presentationToken = _presentation.Token;
        VocabularyNotebookTheme.Button(_generate);
        var header = new DockPanel(); DockPanel.SetDock(_generate, Dock.Right); header.Children.Add(_generate); header.Children.Add(_heading);
        var body = new StackPanel(); body.Children.Add(_proposal); body.Children.Add(_replace); body.Children.Add(_prose); _details.Content = body;
        Children.Add(header); Children.Add(new ScrollViewer { Content = _modelDestination, MaxHeight = 72, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Children.Add(_meaning); Children.Add(_details); Children.Add(_status);
        _modelDestination.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _heading.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _generate.Click += OnGenerate; _replace.Click += OnReplace; session.StateChanged += OnStateChanged;
    }
    internal void Update(VocabularyEntry entry, VocabularySense sense, VocabularyExample? example)
    {
        Dispatcher.VerifyAccess();
        _destination = new(entry.Id, sense.Id, example?.Id);
        _authoredMeaning = example?.Meaning ?? sense.Meaning;
        _authoredLanguage = example is null ? sense.MeaningLanguage : example.MeaningLanguage;
        _standalone = example is null;
        _alwaysAuthored = example?.MeaningOrigin == VocabularyMeaningOrigin.User;
        _heading.Text = example is null ? "General meaning" : "Meaning in this context";
        Render(_session.State);
    }
    private bool Current() => !_disposed && _canAct() && _owner.IsCurrent(_key, _session);
    private void OnStateChanged(LearningExplanationState state)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) { if (Current() && ReferenceEquals(state, _session.State)) Render(state); return; }
        // Core publishes from workers. A queued event can outlive the row or be superseded; it
        // cannot retarget the controls or resurrect a removed example after navigation.
        try { _ = Dispatcher.InvokeAsync(() => { if (Current() && ReferenceEquals(state, _session.State)) Render(state); }, DispatcherPriority.DataBind, _presentationToken); }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
    private void Render(LearningExplanationState state)
    {
        if (_disposed) return;
        if (_input != state.Input) { _input = state.Input; _details.IsExpanded = false; _adoptionError = null; }
        _displayed = state;
        _modelDestination.Text = LearningModelDestinationText.Explanation(_owner.Profile);
        var action = LearningExplanationCardPolicy.Action(state, _owner.IsConfigured);
        _generate.Content = state.IsGenerating ? "■" : "✦"; _generate.IsEnabled = action != LearningExplanationCardAction.None;
        string label = LearningExplanationCardPolicy.Label(state, _owner.IsConfigured);
        _generate.ToolTip = state.ErrorMessage is not null && _owner.IsConfigured && !state.IsGenerating ? label + "\n" + state.ErrorMessage : label;
        System.Windows.Automation.AutomationProperties.SetName(_generate, label);
        string? supplied = !_standalone && (_alwaysAuthored || state.Record is null) ? _authoredMeaning : null;
        Set(_meaning, supplied ?? state.Record?.Result.Meaning ?? "", supplied is null ? state.Record?.Language : _authoredLanguage);
        var record = state.Record;
        _details.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        // Swift String equality is canonical; equivalent composed/decomposed prose is not a
        // different proposal just because its UTF-16 representation differs on Windows.
        bool proposal = record is not null && supplied is not null && supplied.Normalize() != record.Result.Meaning.Normalize();
        Set(_proposal, proposal ? record!.Result.Meaning : "", record?.Language);
        _replace.Visibility = proposal ? Visibility.Visible : Visibility.Collapsed; _replace.IsEnabled = !_adopting;
        Set(_prose, record?.Result.Explanation ?? "", record?.Language);
        _status.Text = _adopting ? "Saving meaning…" : _adoptionError ?? (state.IsGenerating ? "Explaining…" : state.ErrorMessage ?? (_owner.HasUnavailableSavedRecord(_key) ? "A saved explanation has a date unavailable for display on Windows." : ""));
        _status.Visibility = _status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnGenerate(object sender, RoutedEventArgs args)
    {
        if (!Current() || _displayed is not { } state || !ReferenceEquals(state, _session.State)) return;
        var action = LearningExplanationCardPolicy.Action(state, _owner.IsConfigured);
        if (action == LearningExplanationCardAction.Stop) { _session.Cancel(state); return; }
        if (action == LearningExplanationCardAction.None) return;
        _owner.ActivateExplanation(_key, _session);
        // Preparing siblings can synchronously notify and navigate. The captured row and state
        // must still be current before opening configuration or requesting generation.
        if (!Current() || !ReferenceEquals(state, _session.State)) return;
        if (action == LearningExplanationCardAction.Configure) _configure?.Invoke();
        else LastGeneration = _session.ExplainAsync(refresh: action == LearningExplanationCardAction.Refresh, expectedState: state);
    }
    private void OnReplace(object sender, RoutedEventArgs args)
    {
        if (_adopting || !Current() || _displayed is not { Record: { } record } state || !ReferenceEquals(state, _session.State)) return;
        _adopting = true; _adoptionError = null; Render(state);
        var work = AdoptAsync(new(this), Dispatcher, _store, record, _destination, _presentationToken); LastAdoption = work; _track(work);
    }
    private static async Task AdoptAsync(WeakReference<CanonicalVocabularyExplanationRow> owner, Dispatcher dispatcher, ICanonicalVocabularyStore store,
        LearningExplanationRecord record, VocabularyExplanationDestination destination, CancellationToken token)
    {
        // Once admitted, the Store transaction survives hide/navigation. Only presentation is
        // cancelable; the containing window tracks this task and drains the durable operation.
        bool saved;
        try { saved = (await store.AdoptExplanationAsync(record, destination).ConfigureAwait(false)).Changed; }
        catch { saved = false; }
        if (token.IsCancellationRequested || dispatcher.HasShutdownStarted) return;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (!owner.TryGetTarget(out var row) || row._disposed) return;
                row._adopting = false;
                if (row._session.State.Input == record.Input) row._adoptionError = saved ? null : "The contextual meaning could not be updated.";
                if (row.Current()) row.Render(row._session.State);
            }, DispatcherPriority.DataBind, token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted) { }
    }
    private static TextBox Literal(bool bold)
    {
        var box = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent, FontSize = 14, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, 6, 0, 0) };
        box.SetResourceReference(Control.ForegroundProperty, "TextPrimary"); return box;
    }
    private static void Set(TextBox box, string text, LearningLanguage? language)
    {
        // Avoid replacing identical text: a saved annotation update must not discard selection.
        if (!string.Equals(box.Text, text, StringComparison.Ordinal)) box.Text = text;
        box.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        box.FlowDirection = language == LearningLanguage.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
    public void Dispose()
    {
        Dispatcher.VerifyAccess(); if (_disposed) return; _disposed = true;
        _session.StateChanged -= OnStateChanged; _presentation.Cancel(); _presentation.Dispose();
        _generate.Click -= OnGenerate; _replace.Click -= OnReplace;
    }
}
