using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningPowerControlsTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task SyntheticControlIsInertUntilExplicitToggleAndKeepsManualSelection() => dispatcher.RunAsync(() =>
    {
        var settings = new AppSettings { LearningLocalModel = "manual.gguf" }; int writes = 0;
        var view = new LearningPowerControls(() => settings, change => { writes++; change(settings); },
            LearningModelRole.Translation, ["manual.gguf", "battery.gguf"]);
        var host = new Border { Background = Brushes.White, Padding = new Thickness(16), Child = view };
        host.Measure(new Size(500, 600)); host.Arrange(new Rect(0, 0, 500, 600)); host.UpdateLayout();
        Assert.Equal(0, writes); Assert.False(settings.LearningTranslationPowerSwitching);
        var toggle = Assert.IsType<CheckBox>(view.Children[0]); toggle.IsChecked = true;
        Assert.Equal(1, writes); Assert.Equal("manual.gguf", settings.LearningTranslationBatteryProfile!.LocalModel);
        host.Measure(new Size(500, 600)); host.Arrange(new Rect(0, 0, 500, 600)); host.UpdateLayout();
        var image = new RenderTargetBitmap(500, 600, 96, 96, PixelFormats.Pbgra32); image.Render(host);
        if (Environment.GetEnvironmentVariable("LINGROVE_POWER_RENDER") is { Length: > 0 } path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = System.IO.File.Create(path); encoder.Save(output);
        }
        toggle.IsChecked = false; Assert.Equal("manual.gguf", settings.LearningTranslationActiveSelection.LocalModel);
        return Task.CompletedTask;
    });
}
