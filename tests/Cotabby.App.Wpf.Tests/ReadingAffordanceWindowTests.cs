using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using System.Windows.Automation;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingAffordanceWindowTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ProgressUsesTheBorrowedNonactivatingSurfaceAndOldOwnersCannotHideNewProgress() => dispatcher.RunAsync(() =>
    {
        var window = new ReadingAffordanceWindow(); var first = new object(); var second = new object();
        var selection = new ReadingSelection(Guid.NewGuid(), "history", 42, "field", 3, null, 7, false, true,
            InputCompositionState.Clear, 1, new(465, 290, 70, 20)) { SelectionBounds = new(465, 290, 70, 20) };
        var frame = new ReadingVisualFrame(500, 300, new(0, 0, 1000, 800), new(0, 0, 1000, 600), 1);
        try
        {
            window.ShowProgress(selection, frame, first); Assert.True(window.IsVisible); Assert.False(window.IsActive); Assert.False(window.ShowActivated);
            var button = Assert.IsType<Button>(window.Content); Assert.False(button.IsEnabled);
            Assert.True(Assert.IsType<ProgressBar>(button.Content).IsIndeterminate); Assert.Equal("Preparing…", AutomationProperties.GetName(button));
            Assert.False(window.Contains(500, 300));
            if (Environment.GetEnvironmentVariable("LINGROVE_READING_PROGRESS_RENDER") is { Length: > 0 } path)
            {
                var content = (FrameworkElement)window.Content; content.UpdateLayout();
                var image = new RenderTargetBitmap(36, 36, 96, 96, PixelFormats.Pbgra32); image.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(path); encoder.Save(output);
            }
            window.ShowProgress(selection, frame, second); window.HideProgress(first); Assert.True(window.IsVisible);
            window.HideProgress(second); Assert.False(window.IsVisible);
            window.ShowAt(30, 30, () => { }); Assert.True(button.IsEnabled); Assert.Equal("文", button.Content);
        }
        finally { window.Close(); }
        window.HideProgress(second); return Task.CompletedTask;
    });
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
