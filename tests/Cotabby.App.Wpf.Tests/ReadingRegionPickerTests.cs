using System.Windows;
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
    public Task CancellationCompletesWithoutARegion() => dispatcher.RunAsync(async () =>
    {
        using var picker = new ReadingRegionPicker(new ScreenRect(30, 30, 100, 100)); using var cancellation = new CancellationTokenSource();
        var result = picker.SelectAsync(cancellation.Token); cancellation.Cancel(); Assert.Null(await result); Assert.False(picker.IsVisible);
    });
}
