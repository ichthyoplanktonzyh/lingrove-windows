using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.Core.Focus;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ExpressionAssistNoticeWindowTests(WpfDispatcher dispatcher)
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [Fact]
    public Task FailureIsNonactivatingAndCopiesOnlyOnExplicitAction() => dispatcher.RunAsync(async () =>
    {
        var copied = new List<string>(); var window = new ExpressionAssistNoticeWindow("The insertion could not be confirmed.", "A candidate expression", copied.Add);
        window.ShowAt(new ScreenRect(300, 300, 1, 20)); Assert.False(window.ShowActivated); Assert.False(window.IsActive); Assert.NotEqual(0, GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 0x08000000); Assert.Empty(copied);
        var text = Find<NonactivatingSelectableText>(window, "expressionNoticeCandidate"); Assert.Equal("A candidate expression", text.Text); Assert.False(text.Focusable);
        text.Select(0, 1); Assert.NotNull(text.RequestCopy()); Assert.Equal(new[] { "A" }, copied);
        Capture(window, "expression-notice-failure"); var copy = Find<Button>(window, "expressionNoticeCopy"); copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(new[] { "A", "A candidate expression" }, copied); Assert.False(copy.IsEnabled); Assert.Equal("Copied ✓", copy.Content);
        await Task.Delay(1100); Assert.False(window.IsVisible);
    });
    [Fact]
    public Task ClosedCopiedNoticeCannotCloseItsReplacement() => dispatcher.RunAsync(async () =>
    {
        var prior = new ExpressionAssistNoticeWindow("First", "first", _ => { });
        prior.ShowAt(ScreenRect.Empty);
        Find<Button>(prior, "expressionNoticeCopy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        prior.Close();
        var replacement = new ExpressionAssistNoticeWindow("Second", null, _ => { });
        try { replacement.ShowAt(ScreenRect.Empty); await Task.Delay(1100); Assert.True(replacement.IsVisible); }
        finally { replacement.Close(); }
    });
    [Fact]
    public Task UnavailableAndCopyFailureRemainVisibleAndNeverAutomaticallyCopy() => dispatcher.Run(() =>
    {
        int calls = 0; var unavailable = new ExpressionAssistNoticeWindow("Expression assistance is unavailable.", null, _ => calls++); unavailable.ShowAt(ScreenRect.Empty); Assert.Null(TryFind<Button>(unavailable, "expressionNoticeCopy")); Assert.Null(TryFind<TextBlock>(unavailable, "expressionNoticeCandidate")); Assert.Equal(0, calls); Capture(unavailable, "expression-notice-unavailable"); unavailable.Close();
        var failure = new ExpressionAssistNoticeWindow("Could not insert.", new string('x', 2000), _ => { calls++; throw new ExternalException(); }); failure.ShowAt(new ScreenRect(300, 300, 1, 20));
        var copy = Find<Button>(failure, "expressionNoticeCopy"); Assert.True(copy.IsVisible); copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, calls); Assert.True(copy.IsEnabled); Assert.Contains(Descendants(failure).OfType<TextBlock>(), t => t.Text == "Unable to copy. Try again."); Capture(failure, "expression-notice-long-error"); failure.Close();
    });
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => TryFind<T>(root, name) ?? throw new InvalidOperationException(name);
    private static T? TryFind<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants(root).OfType<T>().SingleOrDefault(x => x.Name == name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var value in Descendants(VisualTreeHelper.GetChild(root, i))) yield return value; }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is not { Length: > 0 } dir) return; Directory.CreateDirectory(dir);
        foreach (var (suffix, dark, high) in new[] { ("light", false, false), ("dark", true, false), ("highcontrast-simulated", true, true) })
        {
            WindowTheme.ApplyPalette(window.Resources, dark);
            window.Resources["NoticeAccent"] = high ? Brushes.Yellow : new SolidColorBrush(Color.FromRgb(0, 122, 255)); window.Resources["NoticeAccentText"] = high ? Brushes.Black : Brushes.White;
            if (high) { foreach (var key in new[] { "CardBg", "KeycapBg", "InputBg" }) window.Resources[key] = Brushes.Black; foreach (var key in new[] { "CardBorder", "TextPrimary", "TextSecondary" }) window.Resources[key] = Brushes.White; }
            var root = (FrameworkElement)window.Content; root.Measure(new Size(window.ActualWidth, window.ActualHeight)); root.Arrange(new Rect(0, 0, window.ActualWidth, window.ActualHeight)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(dir, name + "-" + suffix + ".png")); encoder.Save(stream);
        }
    }
}
