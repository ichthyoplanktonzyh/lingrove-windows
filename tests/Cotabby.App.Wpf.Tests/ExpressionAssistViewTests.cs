using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ExpressionAssistViewTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task RetiredCompositionCannotBlockOrClearTheNextInputSession() => dispatcher.RunAsync(async () =>
    {
        var window = new ExpressionAssistWindow(new ScreenRect(50, 50, 1, 18), (_, _) => { }, _ => { });
        var panel = (IExpressionAssistPanel)window;
        var observation = new ExpressionEditorObservation(1, "field", 1, 1, "", new(0, 0), false, true, InputCompositionState.Clear, 1);
        var receipt = ExpressionEditorReceiptPolicy.Capture(observation, null)!;
        try
        {
            await panel.ShowAsync(receipt, "", static () => true);
            var view = window.View;
            RaiseComposition(view.QueryBox, TextCompositionManager.PreviewTextInputStartEvent);
            Assert.True(view.IsComposing);
            RaiseComposition(view.QueryBox, TextCompositionManager.PreviewTextInputEvent);
            panel.Hide(false); // The old completion remains queued on the dispatcher.
            Assert.False(view.IsComposing);
            var showing = panel.ShowAsync(receipt, "new query", static () => true);
            RaiseComposition(view.QueryBox, TextCompositionManager.PreviewTextInputStartEvent);
            await showing; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(view.IsComposing); // The old completion did not clear the new preedit.
            bool prematureCancel = false; view.Cancel += () => prematureCancel = true;
            var composingKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view.QueryBox), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            view.QueryBox.RaiseEvent(composingKey); Assert.False(prematureCancel);
            panel.Hide(false);
            await panel.ShowAsync(receipt, "new query", static () => true);
            Assert.False(view.IsComposing);
            string? observed = null; bool cancelled = false;
            view.QueryChanged += (text, composing) => { if (!composing) observed = text; };
            view.Cancel += () => cancelled = true;
            view.SetQuery("new input"); Assert.Equal("new input", observed);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view.QueryBox), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            view.QueryBox.RaiseEvent(key);
            Assert.True(key.Handled); Assert.True(cancelled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ThemedDestinationAndCandidatesStayBounded() => dispatcher.RunAsync(() =>
    {
        var window = new ExpressionAssistWindow(new ScreenRect(50, 50, 1, 18), (_, _) => { }, _ => { });
        try
        {
            var view = window.View;
            view.SetDestination("AI explanation endpoint: https://example.invalid/" + new string('p', 300) + " · model\nThe selected text and its context are sent to this destination.");
            view.SetQuery("Describe a careful alternative");
            view.Update(new ExpressionAssistResult([new("rule out caching", "exclude this possible cause", "rule out"),
                new("eliminate the possibility", "a second expression", null), new("consider another explanation", "another option", null)], null), null, false);
            window.Show(); window.UpdateLayout();
            var scrolls = Descendants<ScrollViewer>(view).ToArray();
            Assert.Contains(scrolls, scroll => scroll.MaxHeight == 72 && scroll.ExtentHeight > scroll.ViewportHeight);
            Assert.Contains(scrolls, scroll => scroll.MaxHeight == 210);
            Assert.Equal(3, Descendants<Button>(view).Count());
            if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                foreach (var (name, dark, scale, height) in new[] { ("light", false, 1d, 600), ("dark", true, 1d, 600), ("highcontrast-simulated", true, 1d, 600), ("short", false, 1d, 310), ("large-text", false, 1.5d, 600) })
                {
                    Cotabby.App.UI.WindowTheme.ApplyPalette(window.Resources, dark);
                    window.Resources["ExpressionSelected"] = window.Resources["KeycapBg"];
                    window.Resources["ExpressionSelectedBorder"] = window.FindResource("AccentBrush");
                    if (name == "highcontrast-simulated")
                    {
                        foreach (string key in new[] { "CardBg", "InputBg", "KeycapBg", "ExpressionSelected" }) window.Resources[key] = Brushes.Black;
                        foreach (string key in new[] { "TextPrimary", "TextSecondary", "CardBorder" }) window.Resources[key] = Brushes.White;
                        window.Resources["ExpressionSelectedBorder"] = Brushes.Yellow;
                    }
                    view.LayoutTransform = new ScaleTransform(scale, scale);
                    view.Measure(new Size(570, height)); view.Arrange(new Rect(0, 0, 570, height)); view.UpdateLayout();
                    var image = new RenderTargetBitmap(570, height, 96, 96, PixelFormats.Pbgra32); image.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var output = File.Create(Path.Combine(dir, "expression-theme-" + name + ".png")); encoder.Save(output);
                }
                view.LayoutTransform = Transform.Identity;
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static void RaiseComposition(TextBox target, RoutedEvent routedEvent)
    {
        var composition = new TextComposition(InputManager.Current, target, "");
        target.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = routedEvent });
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    [Fact]
    public Task CandidateViewShowsHierarchyAndDispatchesExplicitSelection() => dispatcher.RunAsync(() =>
    {
        var view = new ExpressionAssistView();
        var window = new Window { Width = 410, Height = 430, ShowActivated = false, ShowInTaskbar = false, Content = view };
        try
        {
            window.Show(); view.SetQuery("排除缓存是原因");
            view.Update(new ExpressionAssistResult([new("rule out caching", "排除缓存", "rule out"),
                new("exclude caching", "另一种说法", null)], null), null, false);
            window.UpdateLayout();
            Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Base form: rule out");
            Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "排除缓存");
            ExpressionAssistCandidate? chosen = null; view.Accept += candidate => chosen = candidate;
            var buttons = Descendants<Button>(view).ToArray();
            Assert.Equal(2, buttons.Length); Assert.All(buttons, button => Assert.False(button.Focusable));
            Click(buttons[1]); Assert.Equal("exclude caching", chosen?.Text);
            if (Environment.GetEnvironmentVariable("LINGROVE_EXPRESSION_VIEW_RENDER") is { Length: > 0 } path)
            {
                var image = new RenderTargetBitmap(410, 430, 96, 96, PixelFormats.Pbgra32); image.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(path); encoder.Save(output);
            }
            view.Update(new ExpressionAssistResult([], "Which meaning?"), "Which meaning?", false);
            Assert.Empty(Descendants<Button>(view));
            Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Which meaning?");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
