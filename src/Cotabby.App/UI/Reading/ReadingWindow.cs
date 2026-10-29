using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.Reading;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;

namespace Cotabby.App.UI.Reading;

/// <summary>Read-only selection card. It owns presentation only; capture and provider operations
/// remain with the app session and its borrowed owners.</summary>
internal sealed class ReadingWindow : Window, IReadingPresentation
{
    private readonly TextBlock _heading = new() { FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _destination = new() { FontSize = 11, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _source = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly TextBlock _status = new() { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, Margin = new(0, 6, 0, 6) };
    private ScreenRect _anchor;
    internal event Action? DismissRequested;

    internal ReadingWindow()
    {
        Width = 400; SizeToContent = SizeToContent.Height; MaxHeight = 520;
        ShowInTaskbar = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; AllowsTransparency = true; Topmost = true;
        var stack = new StackPanel();
        stack.Children.Add(_heading); stack.Children.Add(_destination); stack.Children.Add(_source);
        stack.Children.Add(_status); stack.Children.Add(_progress); stack.Children.Add(_result);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Padding = new(12, 4, 12, 4) };
        close.Click += (_, _) => DismissRequested?.Invoke(); stack.Children.Add(close);
        Content = new Border { CornerRadius = new(10), BorderBrush = Brushes.LightGray, BorderThickness = new(1),
            Background = Brushes.White, Padding = new(14), Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
    }

    public async Task ShowAsync(ReadingState state)
    {
        _anchor = state.Selection.Anchor;
        Update(state); Show(); UpdateLayout(); LearningPanelPlacement.Position(this, _anchor);
        // The destination is presented before the session invokes an endpoint provider.
        await Dispatcher.Yield(DispatcherPriority.Render);
    }
    public void Update(ReadingState state)
    {
        _heading.Text = state.Route.IsTranslation ? "Selected text translation" : "Selected word or phrase";
        _destination.Text = state.Destination;
        _source.Text = state.Selection.Text;
        _status.Text = state.Status switch
        {
            ReadingStatus.Loading => "Preparing…", ReadingStatus.Translating => "Translating…",
            ReadingStatus.Lookup => "Looking up…", ReadingStatus.Ready => "Ready",
            _ => state.Message ?? "Could not complete the request.",
        };
        _progress.Visibility = state.Status is ReadingStatus.Loading or ReadingStatus.Translating or ReadingStatus.Lookup
            ? Visibility.Visible : Visibility.Collapsed;
        _result.Text = state.Text;
        if (IsVisible) { UpdateLayout(); LearningPanelPlacement.Position(this, _anchor); }
    }
    public void ShowCaptureError(string message)
    {
        _heading.Text = "Selected text unavailable"; _source.Text = ""; _destination.Text = "";
        _result.Text = ""; _status.Text = message; _progress.Visibility = Visibility.Collapsed;
        Show(); UpdateLayout(); LearningPanelPlacement.Position(this, ScreenRect.Empty);
    }
}
