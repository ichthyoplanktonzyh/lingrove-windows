using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningExpressionExampleTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task SampleStagesDoNotChangeAnExternalDocument() => dispatcher.RunAsync(() =>
    {
        var scene = new LearningExpressionExample(); int interactions = 0; scene.Interacted += () => interactions++;
        scene.SetStep(0); Assert.Contains("??", scene.ExampleText);
        scene.SetStep(1); Assert.Contains("让我随时了解最新进展", scene.ExampleText);
        scene.SetStep(2); Assert.Equal(0, interactions);
        scene.SetStep(3); Assert.Contains("keep me posted", scene.ExampleText);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(600, 510, 1.0)]
    [InlineData(600, 350, 1.0)]
    [InlineData(600, 510, 1.5)]
    public Task SampleRendersAtBoundedSizes(int width, int height, double scale) => dispatcher.RunAsync(() =>
    {
        var scene = new LearningExpressionExample { LayoutTransform = new ScaleTransform(scale, scale) }; scene.SetStep(2);
        var host = new Border { Background = Brushes.White, Padding = new Thickness(20),
            Child = new ScrollViewer { Content = scene, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
        if (Environment.GetEnvironmentVariable("LINGROVE_EXPRESSION_SAMPLE_RENDER") is { Length: > 0 } directory)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(directory, $"expression-{height}-{scale:0.0}.png")); encoder.Save(file);
        }
        Assert.True(scene.DesiredSize.Width <= width); return Task.CompletedTask;
    });
}
