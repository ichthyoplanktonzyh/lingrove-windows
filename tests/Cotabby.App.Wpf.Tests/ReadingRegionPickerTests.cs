using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Cotabby.App.UI.Reading;
using Cotabby.Core.Focus;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingRegionPickerTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task OneExplicitGestureIsClampedToVerifiedBoundsAndCloses() => dispatcher.RunAsync(async () =>
    {
        using var picker = new ReadingRegionPicker(new ScreenRect(30, 30, 100, 100));
        Assert.False(picker.ShowActivated);
        var result = picker.SelectAsync(default); picker.Begin(new Point(40, 50)); picker.End(new Point(300, 300));
        Assert.Equal(new ScreenRect(40, 50, 90, 80), await result); Assert.False(picker.IsVisible);
        picker.End(new Point(41, 51)); Assert.Equal(new ScreenRect(40, 50, 90, 80), await result);
    });
    [Fact]
    public Task SelectionLeavesAClearHoleAndOnlyRendersOwnedVisuals() => dispatcher.RunAsync(async () =>
    {
        using var picker = new ReadingRegionPicker(new ScreenRect(30, 30, 740, 400));
        var result = picker.SelectAsync(default);
        try
        {
            picker.UpdateLayout(); picker.Begin(new Point(160, 160)); picker.Update(new Point(580, 330)); picker.UpdateLayout();
            var canvas = Assert.IsType<Canvas>(picker.Content);
            var shade = Assert.IsType<System.Windows.Shapes.Path>(canvas.Children[0]);
            var hole = Assert.IsType<CombinedGeometry>(shade.Data);
            Assert.Equal(GeometryCombineMode.Exclude, hole.GeometryCombineMode);
            Assert.False(hole.FillContains(canvas.PointFromScreen(new Point(250, 250))));
            Assert.True(hole.FillContains(new Point(4, 4)));
            if (Environment.GetEnvironmentVariable("LINGROVE_REGION_RENDER") is { Length: > 0 } path)
            {
                // Render this app's visual tree over synthetic content. Never capture the desktop.
                var image = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var sample = new DrawingVisual(); using (var drawing = sample.RenderOpen())
                {
                    drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 245, 249)), null, new Rect(canvas.RenderSize));
                    drawing.DrawRectangle(Brushes.White, new Pen(Brushes.LightSlateGray, 1), new Rect(85, 95, 530, 240));
                    var text = new FormattedText("Reading sample\n\nLanguage learning while reading.\nSelect text from a page or document.",
                        System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 19, Brushes.DarkSlateGray, 1);
                    drawing.DrawText(text, new Point(115, 125));
                }
                image.Render(sample); image.Render(canvas);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(path); encoder.Save(stream);
            }
        }
        finally { picker.Dispose(); }
        Assert.Null(await result);
    });
    [Fact]
    public Task CancellationCompletesWithoutARegion() => dispatcher.RunAsync(async () =>
    {
        using var picker = new ReadingRegionPicker(new ScreenRect(30, 30, 100, 100)); using var cancellation = new CancellationTokenSource();
        var result = picker.SelectAsync(cancellation.Token); cancellation.Cancel(); Assert.Null(await result); Assert.False(picker.IsVisible);
    });
}
