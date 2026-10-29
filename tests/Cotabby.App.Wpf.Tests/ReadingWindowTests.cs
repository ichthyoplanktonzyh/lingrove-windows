using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.Reading;
using Cotabby.App.UI.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingWindowTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task SelectionCardDisplaysDestinationSourceAndResult() => dispatcher.RunAsync(async () =>
    {
        var selection = new ReadingSelection(Guid.NewGuid(), "This is a sentence.", 1, "field", 1, 0, 19, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var state = new ReadingState(selection, null, LearningLanguage.Chinese,
            new(true, ReadingRouteReason.TerminalText, null, false), ReadingStatus.Ready, "Local model", "这是一句话。");
        var window = new ReadingWindow();
        try
        {
            await window.ShowAsync(state); window.UpdateLayout();
            var labels = Descendants<TextBlock>(window).Select(block => block.Text).ToArray();
            Assert.Contains("Selected text translation", labels); Assert.Contains("Local model", labels);
            Assert.Contains(selection.Text, labels); Assert.Contains("这是一句话。", labels);
            if (Environment.GetEnvironmentVariable("LINGROVE_READING_RENDER") is { Length: > 0 } path)
            {
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(path); encoder.Save(output);
            }
        }
        finally { window.Close(); }
    });
    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
