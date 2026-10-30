using Cotabby.App.Localization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.Core.ExpressionAssist;
using Cotabby.App.UI.Vocabulary;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>Native temporary description and candidate surface. The session owns all effects;
/// this view exposes intent only and never captures or writes a host field.</summary>
internal sealed class ExpressionAssistView : Border
{
    private readonly TextBox _query = new() { AcceptsReturn = false, TextWrapping = TextWrapping.Wrap,
        MinHeight = 42, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(10, 8, 10, 8) };
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _candidateScroll = new() { MaxHeight = 210,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly ScrollViewer _outerScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(2, 6, 2, 4) };
    private readonly TextBlock _destination = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly TextBlock _footer = new() { FontSize = 11, Margin = new(2, 8, 2, 1) };
    private readonly TextBlock _speechDestination = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(2, 6, 2, 0) };
    private readonly TextBlock _toolStatus = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(2, 4, 2, 0) };
    private readonly List<CandidateActions> _actions = [];
    private ExpressionAssistCandidateTools? _tools;
    private sealed record CandidateActions(int Index, ExpressionAssistCandidate Candidate, Button? Speak, VocabularyFavoriteButton? Favorite)
    { internal string? Failure { get; set; } }
    private IReadOnlyList<ExpressionAssistCandidate> _candidates = Array.Empty<ExpressionAssistCandidate>();
    private int _selected;
    private bool _composing;
    private long _compositionRevision;

    internal event Action<string, bool>? QueryChanged;
    internal event Action? Submit;
    internal event Action<ExpressionAssistCandidate>? Accept;
    internal event Action? Cancel;
    internal event Action? ToolInteracted;
    internal TextBox QueryBox => _query;
    internal bool IsComposing => _composing;

    internal ExpressionAssistView()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/ExpressionAssist/ExpressionAssistResources.xaml", UriKind.Relative) });
        Width = 380; MaxWidth = 380; Padding = new Thickness(12);
        SetResourceReference(BackgroundProperty, "CardBg"); SetResourceReference(BorderBrushProperty, "CardBorder"); BorderThickness = new Thickness(1);
        SetResourceReference(TextElement.ForegroundProperty, "TextPrimary");
        CornerRadius = new CornerRadius(10);
        var body = new StackPanel(); _outerScroll.Content = body; Child = _outerScroll;
        var heading = new TextBlock { Text = InterfaceText.Get("Expression assistance"), FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _destination.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _footer.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _query.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        _query.SetResourceReference(Control.BackgroundProperty, "InputBg");
        _query.SetResourceReference(Control.BorderBrushProperty, "CardBorder");
        _query.CaretBrush = SystemColors.HighlightBrush;
        body.Children.Add(heading);
        body.Children.Add(new ScrollViewer { Content = _destination, MaxHeight = 72,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8) });
        var prompt = new TextBlock { Text = InterfaceText.Get("Describe an expression or concept"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        prompt.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); body.Children.Add(prompt);
        AutomationProperties.SetName(_query, InterfaceText.Get("Describe an expression or concept", "Learning"));
        _query.ToolTip = InterfaceText.Get("Describe an expression or concept", "Learning");
        body.Children.Add(_query);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _candidateScroll.Content = _rows;
        body.Children.Add(_status); body.Children.Add(_candidateScroll); body.Children.Add(_speechDestination); body.Children.Add(_toolStatus); body.Children.Add(_footer);
        _speechDestination.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _toolStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        AutomationProperties.SetLiveSetting(_toolStatus, AutomationLiveSetting.Polite);
        _query.TextChanged += (_, _) => QueryChanged?.Invoke(_query.Text, _composing);
        TextCompositionManager.AddPreviewTextInputStartHandler(_query, CompositionStarted);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(_query, CompositionUpdated);
        TextCompositionManager.AddPreviewTextInputHandler(_query, CompositionCommitted);
        _query.PreviewKeyDown += HandleKeyDown;
        Update(null, null, false);
    }

    internal void SetQuery(string query) => _query.Text = query;
    internal void SetDestination(string text) { _destination.Text = text; _destination.ToolTip = text; }
    internal bool FocusQuery() => _query.Focus();
    internal void AttachTools(ExpressionAssistCandidateTools tools)
    {
        Dispatcher.VerifyAccess();
        if (_tools is not null) throw new InvalidOperationException("Candidate tools are already attached.");
        _tools = tools; tools.Changed += RefreshTools; RefreshRows(false);
    }
    internal void DetachTools()
    {
        Dispatcher.VerifyAccess();
        if (_tools is not null) _tools.Changed -= RefreshTools;
        _tools = null; RefreshRows(false);
    }

    /// <summary>Retire the previous native input lifetime without publishing a synthetic edit.
    /// Advancing the revision also makes a queued completion from that lifetime inert.</summary>
    internal void ResetComposition()
    {
        ++_compositionRevision;
        _composing = false;
    }

    internal void Update(ExpressionAssistResult? result, string? message, bool loading)
    {
        _candidates = result?.Candidates ?? Array.Empty<ExpressionAssistCandidate>();
        _selected = 0;
        _status.Text = loading ? InterfaceText.Get("Finding expressions…", "Learning") : message ?? "";
        _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
        _status.SetResourceReference(TextBlock.ForegroundProperty, loading ? "TextSecondary" : "TextPrimary");
        RefreshRows(false);
    }

    private void RefreshRows(bool navigate)
    {
        foreach (var row in _actions) row.Favorite?.Clear(); _actions.Clear();
        _rows.Children.Clear();
        for (int i = 0; i < _candidates.Count; i++)
        {
            int index = i; var candidate = _candidates[i];
            string baseForm = Cotabby.Core.Text.FoundationText.TrimWhitespaceAndNewlines(candidate.DictionaryForm ?? "");
            bool showsBaseForm = baseForm.Length > 0 && !string.Equals(baseForm, candidate.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            string baseFormLabel = showsBaseForm ? "Base form: " + baseForm : "";
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = $"{(i == _selected ? "↵" : (i + 1).ToString())}  {candidate.Text}",
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (showsBaseForm)
                content.Children.Add(new TextBlock { Text = baseFormLabel,
                    FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(20, 2, 0, 0) });
            if (!string.IsNullOrWhiteSpace(candidate.Hint))
                content.Children.Add(new TextBlock { Text = candidate.Hint, FontSize = 12,
                    TextWrapping = TextWrapping.Wrap, Margin = new(20, 2, 0, 0) });
            foreach (var line in content.Children.OfType<TextBlock>())
                line.SetResourceReference(TextBlock.ForegroundProperty, content.Children.IndexOf(line) == 0 ? "TextPrimary" : "TextSecondary");
            var button = new Button { Content = content, Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8), Margin = new Thickness(0, 2, 0, 0), BorderThickness = new Thickness(1) };
            button.SetResourceReference(StyleProperty, "ExpressionInsertButton");
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
            AutomationProperties.SetName(button, string.Join(". ", new[] { candidate.Text, baseFormLabel, candidate.Hint }
                .Where(part => !string.IsNullOrWhiteSpace(part))));
            button.Click += (_, _) => { if (!_composing && index < _candidates.Count) Accept?.Invoke(_candidates[index]); };
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(button);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 5, 5, 0) };
            Grid.SetColumn(actions, 1); row.Children.Add(actions);
            Button? speak = null; VocabularyFavoriteButton? favorite = null;
            if (_tools?.Speech is not null)
            {
                speak = new Button { Content = "🔊", Width = 28, Height = 28, Padding = new Thickness(3), Focusable = false };
                speak.SetResourceReference(StyleProperty, "ExpressionActionButton");
                speak.Click += (_, _) => { if (!_composing) { ToolInteracted?.Invoke(); _tools?.ToggleSpeech(index, candidate); } };
                actions.Children.Add(speak);
            }
            if (_tools?.Vocabulary is not null)
            {
                favorite = new VocabularyFavoriteButton { ToggleSavedOnClick = true };
                favorite.Click += (_, _) => { ToolInteracted?.Invoke(); _tools?.Track(favorite.LastOperation); };
                actions.Children.Add(favorite);
            }
            var state = new CandidateActions(index, candidate, speak, favorite); _actions.Add(state);
            if (favorite is not null) favorite.FailureChanged += failure => { state.Failure = failure; RefreshToolStatus(); };
            var border = new Border { Child = row, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new(0, 2, 0, 0) };
            border.SetResourceReference(BackgroundProperty, i == _selected ? TryFindResource("ExpressionSelected") is Brush ? "ExpressionSelected" : "KeycapBg" : "CardBg");
            border.SetResourceReference(BorderBrushProperty, i == _selected ? TryFindResource("ExpressionSelectedBorder") is Brush ? "ExpressionSelectedBorder" : "AccentBrush" : "CardBorder");
            _rows.Children.Add(border);
            if (i == _selected)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (index != _selected || !ReferenceEquals(border.Parent, _rows)) return;
                    double top = border.TranslatePoint(new Point(), _rows).Y;
                    if (top < _candidateScroll.VerticalOffset) _candidateScroll.ScrollToVerticalOffset(top);
                    else if (top + border.ActualHeight > _candidateScroll.VerticalOffset + _candidateScroll.ViewportHeight)
                        _candidateScroll.ScrollToVerticalOffset(top + border.ActualHeight - _candidateScroll.ViewportHeight);
                    if (navigate)
                    {
                        _candidateScroll.UpdateLayout();
                        double visibleTop = border.TranslatePoint(new Point(), _outerScroll).Y;
                        if (visibleTop < 0) _outerScroll.ScrollToVerticalOffset(_outerScroll.VerticalOffset + visibleTop);
                        else if (visibleTop + border.ActualHeight > _outerScroll.ViewportHeight)
                            _outerScroll.ScrollToVerticalOffset(_outerScroll.VerticalOffset + visibleTop + border.ActualHeight - _outerScroll.ViewportHeight);
                    }
                });
        }
        _footer.Text = _candidates.Count > 1 ? InterfaceText.Get("↑↓ Select    Enter / Tab Insert    Esc Cancel", "Learning")
            : _candidates.Count == 1 ? InterfaceText.Get("Enter / Tab Insert    Esc Cancel", "Learning") : InterfaceText.Get("Enter Search    Esc Cancel", "Learning");
        RefreshTools();
    }

    private void RefreshTools()
    {
        Dispatcher.VerifyAccess();
        foreach (var row in _actions)
        {
            if (row.Speak is { } speak)
            {
                var speech = _tools?.Speech; bool active = speech?.IsPlaying(_tools!.Target(row.Index)) == true;
                speak.Content = active ? "■" : "🔊";
                speak.SetResourceReference(Control.ForegroundProperty, active ? "AccentBrush" : "TextSecondary");
                string label = active ? "Stop speaking expression" : "Play expression aloud";
                speak.ToolTip = speech?.Error(_tools!.Target(row.Index)) ?? label;
                speak.IsEnabled = !_composing && _tools?.Current(row.Index, row.Candidate) == true;
                AutomationProperties.SetName(speak, label); AutomationProperties.SetItemStatus(speak,
                    speech?.IsPreparing(_tools!.Target(row.Index)) == true ? "Preparing…" : active ? "Playing" : "");
            }
            if (row.Favorite is { } favorite)
            {
                if (_tools is { Vocabulary: { } store } tools && tools.Capture(row.Index, row.Candidate) is { } capture)
                    favorite.Bind(store, capture, () => !_composing && tools.Current(row.Index, row.Candidate),
                        () => tools.ValidateAsync(row.Index, row.Candidate));
                else favorite.Clear();
            }
        }
        RefreshToolStatus();
    }
    private void RefreshToolStatus()
    {
        var speech = _tools?.Speech;
        _speechDestination.Text = _candidates.Count > 0 ? speech?.DestinationDescription ?? "" : "";
        _speechDestination.Visibility = _speechDestination.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _toolStatus.Text = _tools?.PreparationError ?? _actions.Select(row => row.Failure).FirstOrDefault(error => error is not null)
            ?? (_tools is { } tools ? _actions.Select(row => speech?.Error(tools.Target(row.Index))).FirstOrDefault(error => error is not null) : null) ?? "";
        _toolStatus.Visibility = _toolStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CompositionStarted(object sender, TextCompositionEventArgs e) => SetComposing(true);
    private void CompositionUpdated(object sender, TextCompositionEventArgs e) => SetComposing(true);
    private void CompositionCommitted(object sender, TextCompositionEventArgs e)
    {
        if (!_composing) return;
        long revision = ++_compositionRevision;
        // TextBox applies the committed text after PreviewTextInput. Do not issue a request for
        // the previous value between that event and the control's own text update.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_compositionRevision != revision) return;
            SetComposing(false);
        });
    }
    private void SetComposing(bool value)
    {
        if (value) ++_compositionRevision;
        if (_composing == value) return;
        _composing = value;
        RefreshTools();
        QueryChanged?.Invoke(_query.Text, value);
    }

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (_composing) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int index = e.Key - Key.D1;
            if (index < _candidates.Count) { Accept?.Invoke(_candidates[index]); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Escape) { Cancel?.Invoke(); e.Handled = true; }
        else if (e.Key is Key.Enter or Key.Return)
        {
            if (_candidates.Count > 0) Accept?.Invoke(_candidates[_selected]); else Submit?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && _candidates.Count > 0) { Accept?.Invoke(_candidates[_selected]); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down && _candidates.Count > 0)
        {
            _selected = (_selected + (e.Key == Key.Down ? 1 : -1) + _candidates.Count) % _candidates.Count;
            RefreshRows(true); e.Handled = true;
        }
    }
}
