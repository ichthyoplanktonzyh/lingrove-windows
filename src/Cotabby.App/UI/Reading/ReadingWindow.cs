using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.Reading;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Reading;

/// <summary>Read-only selection card. It owns presentation only; capture and provider operations
/// remain with the app session and its borrowed owners.</summary>
internal sealed partial class ReadingWindow : Window, IReadingPresentation
{
    private readonly TextBlock _heading = new() { FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly VocabularyFavoriteButton _favorite = new();
    private readonly TextBlock _destination = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _source = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, Margin = new(0, 6, 0, 6) };
    private readonly Button _dictionary = new() { Content = "Dictionary", Padding = new(10, 4, 10, 4), Visibility = Visibility.Collapsed };
    private readonly Button _details = new() { Content = "Explain usage", Padding = new(10, 4, 10, 4), Margin = new(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _configure = new() { Content = "Configure model", Padding = new(10, 4, 10, 4), Margin = new(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _retry = new() { Content = "Retry", Padding = new(10, 4, 10, 4), Margin = new(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _copy = new() { Content = "Copy result", Padding = new(10, 4, 10, 4), Margin = new(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _copySource = new() { Content = "Copy source", Padding = new(10, 4, 10, 4), Margin = new(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private ScreenRect _anchor;
    internal event Action? DismissRequested;
    internal event Action? DictionaryRequested;
    internal event Action? DetailsRequested;
    internal event Action? ConfigureRequested;
    internal event Action? RetryRequested;
    internal event Action<bool>? CopyRequested;
    internal void SetDetailsExpanded(bool expanded) => _details.Content = expanded ? "Hide details" : "Explain usage";

    internal ReadingWindow()
    {
        Width = 400; SizeToContent = SizeToContent.Height; MaxHeight = 520;
        ShowActivated = false; ShowInTaskbar = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; AllowsTransparency = true; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        WindowTheme.Follow(this);
        _heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _source.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _result.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        _destination.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var stack = new StackPanel();
        var heading = new DockPanel(); DockPanel.SetDock(_favorite, Dock.Right); heading.Children.Add(_favorite); heading.Children.Add(_heading);
        _favorite.FailureChanged += message => { if (message is not null) _status.Text = message; };
        stack.Children.Add(heading); stack.Children.Add(_destination); stack.Children.Add(_source); InitializeSpeech(stack);
        stack.Children.Add(_status); stack.Children.Add(_progress); stack.Children.Add(_result);
        _dictionary.Click += (_, _) => DictionaryRequested?.Invoke();
        _details.Click += (_, _) => DetailsRequested?.Invoke();
        _configure.Click += (_, _) => ConfigureRequested?.Invoke();
        _retry.Click += (_, _) => RetryRequested?.Invoke();
        _copy.Click += (_, _) => CopyRequested?.Invoke(false);
        _copySource.Click += (_, _) => CopyRequested?.Invoke(true);
        foreach (var button in new[] { _dictionary, _details, _configure, _retry, _copy, _copySource }) { button.Focusable = false; button.SetResourceReference(StyleProperty, "Btn"); }
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_dictionary); actions.Children.Add(_details); actions.Children.Add(_retry);
        actions.Children.Add(_configure); actions.Children.Add(_copySource); actions.Children.Add(_copy); stack.Children.Add(actions);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Padding = new(12, 4, 12, 4) };
        close.Focusable = false; close.SetResourceReference(StyleProperty, "Btn");
        close.Click += (_, _) => DismissRequested?.Invoke(); stack.Children.Add(close);
        var card = new Border { CornerRadius = new(10), BorderThickness = new(1), Padding = new(16),
            Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        card.SetResourceReference(Border.BackgroundProperty, "CardBg"); card.SetResourceReference(Border.BorderBrushProperty, "CardBorder"); Content = card;
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
        UpdateSpeech(state);
        _dictionary.Visibility = state.Route.IsTranslation ? Visibility.Collapsed : Visibility.Visible;
        _details.Visibility = !state.Route.IsTranslation && state.Status == ReadingStatus.Ready ? Visibility.Visible : Visibility.Collapsed;
        _configure.Visibility = !state.Route.IsTranslation && state.Status == ReadingStatus.Error ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = !state.Route.IsTranslation && state.Status == ReadingStatus.Error ? Visibility.Visible : Visibility.Collapsed;
        _copy.Visibility = state.Status == ReadingStatus.Ready && !string.IsNullOrWhiteSpace(state.Text) ? Visibility.Visible : Visibility.Collapsed;
        _copySource.Visibility = !string.IsNullOrWhiteSpace(state.Selection.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (IsVisible) { UpdateLayout(); LearningPanelPlacement.Position(this, _anchor); }
    }
    internal void ShowCopyOutcome(bool copied) => _status.Text = copied ? "Copied" : "Could not copy. Try again.";
    internal void BindVocabulary(IWritingVocabularyStore store, VocabularyCapture capture, Func<bool> current, Func<Task<bool>> preflight) =>
        _favorite.Bind(store, capture, current, preflight);
    internal void ClearVocabulary() => _favorite.Clear();
    internal Task FavoriteWork => _favorite.LastOperation;
    public void ShowCaptureError(string message)
    {
        _heading.Text = "Selected text unavailable"; _source.Text = ""; _destination.Text = "";
        _result.Text = ""; _status.Text = message; _progress.Visibility = Visibility.Collapsed; ClearSpeech();
        Show(); UpdateLayout(); LearningPanelPlacement.Position(this, ScreenRect.Empty);
    }
}
