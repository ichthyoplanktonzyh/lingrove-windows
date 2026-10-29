using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI;
using Cotabby.Core.Onboarding;

namespace Cotabby.App.Wpf.Tests;
[Collection("WPF views")]
public sealed class WelcomeDonePageTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(560, 640, 1.0, "normal")]
    [InlineData(560, 640, 1.5, "large-text")]
    [InlineData(440, 450, 1.5, "short-large-text")]
    public Task WholePageKeepsFooterAndRowsReachable(int width, int height, double scale, string name) => dispatcher.RunAsync(async () =>
    {
        var view = new WelcomeReadinessView(_ => Task.FromResult(Enum.GetValues<ReadinessTask>().Select(t => new ReadinessRow(t, "example model", OnboardingModelStatus.Configured)).ToArray()), _ => { });
        var page = new WelcomeDonePage { Margin = new Thickness(28, 16, 28, 22) };
        page.Keys.Text = "Use your shortcuts to accept suggestions while writing."; page.ReadinessHost.Content = view;
        page.Body.LayoutTransform = new ScaleTransform(scale, scale);
        int finish = 0, back = 0; page.FinishRequested += () => finish++; page.BackRequested += () => back++;
        var window = new Window { Width = width, Height = height, Content = page, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        window.Resources["LinkBtn"] = window.Resources["Btn"];
        window.Resources["GreenTint"] = new SolidColorBrush(Color.FromArgb(31, 34, 197, 94));
        window.SetResourceReference(Window.BackgroundProperty, "WindowBg"); window.SetResourceReference(Window.ForegroundProperty, "TextPrimary");
        try
        {
            window.Show(); view.Refresh(); await Task.Delay(50); window.UpdateLayout();
            Assert.True(page.FinishButton.IsVisible); Assert.True(page.BackButton.IsVisible);
            Assert.True(page.FinishButton.TranslatePoint(new Point(0, page.FinishButton.ActualHeight), window).Y <= height);
            Assert.True(page.BackButton.TranslatePoint(new Point(0, page.BackButton.ActualHeight), window).Y <= height);
            for (int i = 0; i < 4; i++)
            {
                var row = (Button)view.Children[i]; row.BringIntoView(); await Task.Delay(25); window.UpdateLayout();
                var top = row.TranslatePoint(new Point(0, 0), page.Scroller).Y;
                var bottom = row.TranslatePoint(new Point(0, row.ActualHeight), page.Scroller).Y;
                Assert.True(top >= -1 && bottom <= page.Scroller.ViewportHeight + 1, $"Row {i} at {top}..{bottom} outside viewport {page.Scroller.ViewportHeight}");
            }
            page.Scroller.ScrollToEnd(); await Task.Delay(25); window.UpdateLayout();
            var last = (FrameworkElement)page.Body.Children[^1];
            Assert.True(last.TranslatePoint(new Point(0, last.ActualHeight), page.Scroller).Y <= page.Scroller.ViewportHeight + 1);
            Capture(window, width, height, name + "-bottom");
            page.FinishButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); page.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, finish); Assert.Equal(1, back);
            page.Scroller.ScrollToTop(); await Task.Delay(25); window.UpdateLayout();
            Capture(window, width, height, name);
        }
        finally { await view.RetireAsync(); window.Close(); }
    });
    private static void Capture(Window window, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir); var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(dir, "welcome-done-" + name + ".png")); encoder.Save(stream);
    }
}
