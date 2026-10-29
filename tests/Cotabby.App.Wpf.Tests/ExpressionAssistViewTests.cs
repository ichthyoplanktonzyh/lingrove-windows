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
        var window = new ExpressionAssistWindow(new ScreenRect(50, 50, 1, 18));
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
