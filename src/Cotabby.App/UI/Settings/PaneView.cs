using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI.Controls;
using Cotabby.App.Localization;
using Cotabby.App.Settings;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Cotabby.App.UI.Settings;

/// <summary>Renders a pane as a scrolling column of cards.</summary>
internal sealed class PaneView : ScrollViewer
{
    private SettingsRow? _revealed;
    private readonly List<Action> _visibilityRefreshes = [];
    private readonly Dictionary<SettingsRow, (Grid Host, Border Pulse)> _rowViews = [];
    private DispatcherTimer? _arrivalTimer;
    private Border? _pulse;
    private long _arrivalGeneration;
    internal SettingsRow? HighlightedRow { get; private set; }
    internal static readonly TimeSpan ArrivalDuration = TimeSpan.FromSeconds(2.4);
    public PaneView(SettingsPane pane, SettingsContext context) : this(pane, () => context.Settings, context.OnRefresh) { }
    /// <summary>Borrow the existing owner; a component fixture may supply isolated values.
    /// Search reveal changes presentation visibility only, never the selected backend/settings.</summary>
    internal PaneView(SettingsPane pane, Func<AppSettings> settings, Action<Action> onRefresh)
    {
        Pane = pane;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        var title = Ui.Text(InterfaceText.Get(pane.Title));
        title.FontSize = 20;
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(2, 0, 0, 16);

        var column = new StackPanel { Margin = new Thickness(28, 22, 28, 8), MaxWidth = 660, Children = { title } };
        foreach (var card in pane.Cards) column.Children.Add(CardView(card, settings, onRefresh));
        Content = column;
        Unloaded += (_, _) => { ++_arrivalGeneration; StopPulse(); };
    }

    public SettingsPane Pane { get; }

    internal void Reveal(SettingsRow row)
    {
        if (!Pane.Cards.Any(card => card.Rows.Any(value => ReferenceEquals(value, row)))) throw new ArgumentException("The row belongs to another pane.", nameof(row));
        StopPulse(); long generation = ++_arrivalGeneration;
        _revealed = row; foreach (var refresh in _visibilityRefreshes) refresh();
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _arrivalGeneration || !ReferenceEquals(_revealed, row) || !IsVisible) return;
            UpdateLayout();
            var point = row.View.TranslatePoint(new Point(), this);
            ScrollToVerticalOffset(Math.Max(0, VerticalOffset + point.Y - (ViewportHeight - row.View.ActualHeight) / 2));
            HighlightedRow = row; _pulse = _rowViews[row].Pulse; _pulse.BeginAnimation(OpacityProperty, null); _pulse.Opacity = 1;
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = ArrivalDuration };
            _arrivalTimer = timer;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (generation == _arrivalGeneration && ReferenceEquals(timer, _arrivalTimer)) StopPulse(fade: true);
            };
            timer.Start();
        }, DispatcherPriority.Loaded);
    }
    internal void ClearReveal()
    { ++_arrivalGeneration; StopPulse(); if (_revealed is null) return; _revealed = null; foreach (var refresh in _visibilityRefreshes) refresh(); }
    private void StopPulse(bool fade = false)
    {
        _arrivalTimer?.Stop(); _arrivalTimer = null;
        if (_pulse is { } pulse)
        {
            _pulse = null; pulse.BeginAnimation(OpacityProperty, null); pulse.Opacity = 0;
            if (fade && SystemParameters.ClientAreaAnimation) pulse.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450)) { FillBehavior = FillBehavior.Stop,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            if (fade) _pulse = pulse;
        }
        HighlightedRow = null;
    }
    private StackPanel CardView(SettingsCard card, Func<AppSettings> readSettings, Action<Action> onRefresh)
    {
        var rows = new StackPanel();
        var separators = new List<Border>();
        foreach (var row in card.Rows)
        {
            separators.Add(Ui.Separator());
            rows.Children.Add(separators[^1]);
            var pulse = new Border { Opacity = 0, IsHitTestVisible = false, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            pulse.SetResourceReference(Border.BackgroundProperty, "AccentTint"); pulse.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            var host = new Grid { Children = { row.View, pulse } }; _rowViews.Add(row, (host, pulse)); rows.Children.Add(host);
        }

        var view = new StackPanel();
        if (card.Header is { } header) view.Children.Add(Ui.Text(InterfaceText.Get(header), "CardHeader"));
        view.Children.Add(new Border { Padding = new Thickness(14, 3, 14, 3), Child = rows }.Styled("Card"));

        void RefreshVisibility()
        {
            var settings = readSettings();
            view.Visibility = Shown(card.Visible?.Invoke(settings) != false || card.Rows.Any(row => ReferenceEquals(row, _revealed)));
            bool anyAbove = false;
            for (int i = 0; i < card.Rows.Length; i++)
            {
                var row = card.Rows[i];
                bool visible = row.Visible?.Invoke(settings) != false || ReferenceEquals(row, _revealed);
                row.View.Visibility = Shown(visible);
                _rowViews[row].Host.Visibility = Shown(visible);
                row.View.IsEnabled = row.Enabled?.Invoke(settings) != false;
                separators[i].Visibility = Shown(visible && anyAbove);
                anyAbove |= visible;
            }
        }
        _visibilityRefreshes.Add(RefreshVisibility); onRefresh(RefreshVisibility);
        return view;
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
