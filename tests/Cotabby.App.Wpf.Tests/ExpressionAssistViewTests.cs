using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.Core.ExpressionAssist;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ExpressionAssistViewTests(WpfDispatcher dispatcher)
{
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
