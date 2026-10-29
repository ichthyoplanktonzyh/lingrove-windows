using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.Core.ExpressionAssist;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>Native temporary description and candidate surface. The session owns all effects;
/// this view exposes intent only and never captures or writes a host field.</summary>
internal sealed class ExpressionAssistView : Border
{
    private readonly TextBox _query = new() { AcceptsReturn = false, TextWrapping = TextWrapping.Wrap,
        MinHeight = 42, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(10, 8, 10, 8) };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(2, 6, 2, 4) };
    private readonly TextBlock _footer = new() { FontSize = 11, Foreground = Brushes.DimGray, Margin = new(2, 8, 2, 1) };
    private IReadOnlyList<ExpressionAssistCandidate> _candidates = Array.Empty<ExpressionAssistCandidate>();
    private int _selected;
    private bool _composing;
    private long _compositionRevision;

    internal event Action<string, bool>? QueryChanged;
    internal event Action? Submit;
    internal event Action<ExpressionAssistCandidate>? Accept;
    internal event Action? Cancel;
    internal TextBox QueryBox => _query;
    internal bool IsComposing => _composing;

    internal ExpressionAssistView()
    {
        Width = 380; MaxWidth = 380; Padding = new Thickness(12);
        Background = Brushes.White; BorderBrush = Brushes.LightGray; BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        var body = new StackPanel(); Child = body;
        var heading = new TextBlock { Text = "Expression assistance", FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8) };
        body.Children.Add(heading);
        body.Children.Add(new TextBlock { Text = "Describe an expression or concept", Foreground = Brushes.DimGray,
            FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        AutomationProperties.SetName(_query, "Describe an expression or concept");
        _query.ToolTip = "Describe an expression or concept";
        body.Children.Add(_query);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        body.Children.Add(_status); body.Children.Add(_rows); body.Children.Add(_footer);
        _query.TextChanged += (_, _) => QueryChanged?.Invoke(_query.Text, _composing);
        TextCompositionManager.AddPreviewTextInputStartHandler(_query, CompositionStarted);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(_query, CompositionUpdated);
        TextCompositionManager.AddPreviewTextInputHandler(_query, CompositionCommitted);
        _query.PreviewKeyDown += HandleKeyDown;
        Update(null, null, false);
    }

    internal void SetQuery(string query) => _query.Text = query;
    internal bool FocusQuery() => _query.Focus();

    internal void Update(ExpressionAssistResult? result, string? message, bool loading)
    {
        _candidates = result?.Candidates ?? Array.Empty<ExpressionAssistCandidate>();
        _selected = 0;
        _status.Text = loading ? "Finding expressions…" : message ?? "";
        _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
        _status.Foreground = loading ? Brushes.DimGray : Brushes.DarkSlateBlue;
        RefreshRows();
    }

    private void RefreshRows()
    {
        _rows.Children.Clear();
        for (int i = 0; i < _candidates.Count; i++)
        {
            int index = i; var candidate = _candidates[i];
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = $"{(i == _selected ? "↵" : (i + 1).ToString())}  {candidate.Text}",
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(candidate.DictionaryForm) &&
                !string.Equals(candidate.DictionaryForm.Trim(), candidate.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                content.Children.Add(new TextBlock { Text = "Base form: " + candidate.DictionaryForm,
                    FontSize = 11, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new(20, 2, 0, 0) });
            if (!string.IsNullOrWhiteSpace(candidate.Hint))
                content.Children.Add(new TextBlock { Text = candidate.Hint, FontSize = 12, Foreground = Brushes.DimGray,
                    TextWrapping = TextWrapping.Wrap, Margin = new(20, 2, 0, 0) });
            var button = new Button { Content = content, Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8), Margin = new Thickness(0, 2, 0, 0), Background = i == _selected ? Brushes.AliceBlue : Brushes.White };
            AutomationProperties.SetName(button, candidate.Text + ". " + candidate.Hint);
            button.Click += (_, _) => { if (!_composing && index < _candidates.Count) Accept?.Invoke(_candidates[index]); };
            _rows.Children.Add(button);
        }
        _footer.Text = _candidates.Count > 1 ? "↑↓ Select    Enter / Tab Insert    Esc Cancel"
            : _candidates.Count == 1 ? "Enter / Tab Insert    Esc Cancel" : "Enter Search    Esc Cancel";
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
            RefreshRows(); e.Handled = true;
        }
    }
}
