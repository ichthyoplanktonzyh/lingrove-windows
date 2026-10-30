using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.Localization;

namespace Cotabby.App.UI;

/// <summary>The real final-page layout. Its body scrolls independently of the always-visible
/// navigation footer, so enlarged text and shorter windows retain access to every action.</summary>
internal sealed class WelcomeDonePage : Grid
{
    internal ScrollViewer Scroller { get; }
    internal StackPanel Body { get; }
    internal Button FinishButton { get; }
    internal Button BackButton { get; }
    internal TextBlock Keys { get; }
    internal ContentControl ReadinessHost { get; }
    internal event Action? FinishRequested;
    internal event Action? BackRequested;
    internal WelcomeDonePage()
    {
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        Body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 440, Margin = new Thickness(0, 10, 0, 16) };
        var check = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(26), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "✓", FontSize = 23, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        check.SetResourceReference(Border.BackgroundProperty, "GreenTint"); ((TextBlock)check.Child).SetResourceReference(TextBlock.ForegroundProperty, "GreenBrush");
        Body.Children.Add(check);
        Body.Children.Add(new TextBlock { Text = InterfaceText.Get("You're all set"), FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        Keys = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Margin = new Thickness(0, 6, 0, 0) };
        Keys.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); Body.Children.Add(Keys);
        ReadinessHost = new ContentControl { Margin = new Thickness(0, 10, 0, 0) }; Body.Children.Add(ReadinessHost);
        Body.Children.Add(new TextBlock { Text = InterfaceText.Get("Lingrove lives in your system tray"), FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var hint = new TextBlock { Text = InterfaceText.Get("Click the whale to pause suggestions, switch models, or open Settings."), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 430, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); Body.Children.Add(hint);
        Scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = Body, CanContentScroll = false };
        Children.Add(Scroller);
        var footer = new StackPanel { Margin = new Thickness(0, 6, 0, 0) }; Grid.SetRow(footer, 1);
        FinishButton = new Button { Content = InterfaceText.Get("Start Using Lingrove", "Learning"), Height = 40, FontSize = 14, Margin = new Thickness(24, 0, 24, 0) };
        FinishButton.SetResourceReference(StyleProperty, "AccentBtn"); FinishButton.Click += (_, _) => FinishRequested?.Invoke();
        BackButton = new Button { Content = InterfaceText.Get("Back"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        BackButton.SetResourceReference(StyleProperty, "LinkBtn"); BackButton.Click += (_, _) => BackRequested?.Invoke();
        footer.Children.Add(FinishButton); footer.Children.Add(BackButton); Children.Add(footer);
    }
}
