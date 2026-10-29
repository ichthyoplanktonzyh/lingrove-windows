using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Microsoft.Win32;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>Read-only recovery notice. Its application owner validates the original foreground
/// host, supplies the captured caret and closes/replaces this window. No insertion or clipboard
/// operation happens until an explicit Copy click.</summary>
internal sealed class ExpressionAssistNoticeWindow : Window
{
    private readonly string? _candidate;
    private readonly Action<string> _copy;
    private readonly Button? _copyButton;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private ScreenRect _anchor;
    private bool _closed;

    internal ExpressionAssistNoticeWindow(string message, string? candidate) : this(message, candidate, Clipboard.SetText) { }
    internal ExpressionAssistNoticeWindow(string message, string? candidate, Action<string> copy)
    {
        _candidate = candidate; _copy = copy;
        Title = "Expression assistance"; Width = 400; MinWidth = 260; MaxHeight = 420;
        SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowActivated = false; ShowInTaskbar = false;
        Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual; FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        WindowTheme.Follow(this); ApplyContrast();
        void Changed(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try { Dispatcher.BeginInvoke(() => { if (!_closed) ApplyContrast(); }, DispatcherPriority.Loaded); }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { }
        }
        SystemEvents.UserPreferenceChanged += Changed;
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
        Closed += (_, _) => { _closed = true; _copiedTimer.Stop(); SystemEvents.UserPreferenceChanged -= Changed; };

        var card = new Border { Name = "expressionNoticeCard", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBg"); card.SetResourceReference(Border.BorderBrushProperty, "CardBorder"); Content = card;
        var layout = new DockPanel(); card.Child = layout;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var close = Button("Close", "expressionNoticeClose"); close.Click += (_, _) => Close(); buttons.Children.Add(close);
        if (candidate is not null)
        {
            _copyButton = Button("Copy", "expressionNoticeCopy"); _copyButton.Margin = new Thickness(8, 0, 0, 0); _copyButton.SetResourceReference(Control.BackgroundProperty, "NoticeAccent"); _copyButton.SetResourceReference(Control.BorderBrushProperty, "NoticeAccent"); _copyButton.SetResourceReference(Control.ForegroundProperty, "NoticeAccentText");
            _copyButton.Click += (_, _) => Copy(); buttons.Children.Add(_copyButton);
        }
        DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite); DockPanel.SetDock(_status, Dock.Bottom); layout.Children.Add(_status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 315 }; layout.Children.Add(scroll);
        var body = new StackPanel(); scroll.Content = body;
        var label = new TextBlock { Name = "expressionNoticeMessage", Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12 }; label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite); body.Children.Add(label);
        if (candidate is not null)
        {
            var text = new NonactivatingSelectableText { Name = "expressionNoticeCandidate", Text = candidate, DocumentIdentity = new object(), TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
            text.CopyRequested += (_, args) => { args.Handled = true; if (IsVisible && args.Lease.IsCurrent) CopyText(args.Lease.Text, false); };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary"); AutomationProperties.SetName(text, "Candidate: " + candidate); body.Children.Add(text);
        }
        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); if (_copyButton is not null) { _copyButton.Content = "Copy"; _copyButton.IsEnabled = true; } };
    }
    private Button Button(string text, string name)
    {
        var value = new Button { Content = text, Name = name, Focusable = false, MinWidth = 72, Padding = new Thickness(12, 5, 12, 5) };
        value.SetResourceReference(StyleProperty, "Btn"); return value;
    }
    private void Copy()
    {
        if (_candidate is null || _copyButton is null) return;
        CopyText(_candidate, true);
    }
    private void CopyText(string text, bool wholeCandidate)
    {
        try { _copy(text); _status.Text = ""; if (wholeCandidate && _copyButton is not null) { _copyButton.Content = "Copied ✓"; _copyButton.IsEnabled = false; _copiedTimer.Stop(); _copiedTimer.Start(); } }
        catch (ExternalException) { _status.Text = "Unable to copy. Try again."; }
        catch (InvalidOperationException) { _status.Text = "Unable to copy. Try again."; }
        catch (ArgumentException) { _status.Text = "Unable to copy. Try again."; }
        if (IsVisible) { UpdateLayout(); LearningPanelPlacement.Position(this, _anchor); }
    }
    private void ApplyContrast()
    {
        bool high = SystemParameters.HighContrast;
        Resources["NoticeAccent"] = high ? SystemColors.HighlightBrush : (Brush)FindResource("AccentBrush");
        Resources["NoticeAccentText"] = high ? SystemColors.HighlightTextBrush : Brushes.White;
        if (!high) return;
        Resources["CardBg"] = SystemColors.WindowBrush; Resources["CardBorder"] = SystemColors.WindowTextBrush;
        Resources["TextPrimary"] = SystemColors.WindowTextBrush; Resources["TextSecondary"] = SystemColors.WindowTextBrush;
        Resources["InputBg"] = SystemColors.WindowBrush; Resources["KeycapBg"] = SystemColors.WindowBrush;
    }
    internal void ShowAt(ScreenRect anchor)
    {
        Dispatcher.VerifyAccess(); _anchor = anchor; if (!IsVisible) Show(); UpdateLayout(); LearningPanelPlacement.Position(this, anchor);
    }
}
