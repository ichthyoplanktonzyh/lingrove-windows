using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.Reading;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingAffordanceWindowTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task IconDoesNotActivateAndCanInvokeItsAction() => dispatcher.RunAsync(() =>
    {
        var window = new ReadingAffordanceWindow(); int calls = 0;
        try
        {
            Assert.False(window.ShowActivated); window.ShowAt(30, 30, () => calls++);
            Assert.True(window.IsVisible); Assert.False(window.IsActive);
            if (Environment.GetEnvironmentVariable("LINGROVE_READING_ICON_RENDER") is { Length: > 0 } path)
            {
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(path); encoder.Save(output);
            }
            ((Button)window.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls); window.HideAffordance(); Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
