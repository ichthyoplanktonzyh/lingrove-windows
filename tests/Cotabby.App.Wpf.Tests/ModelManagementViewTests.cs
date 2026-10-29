using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.Learning;
using Cotabby.App.Settings;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.Models;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ModelManagementViewTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task IncompatibleRetainedSelectionIsShownWithoutBeingReplaced() => dispatcher.Run(() =>
    {
        var view = new ModelManagementView("Translation", new TextBlock(), () => { }, () => { }, null);
        var settings = new AppSettings { LearningLocalModel = "gemma-4-E2B.i1-Q6_K.gguf" };
        view.ShowModels([], settings, LocalModelTask.Translation, settings.LearningLocalModel);
        var message = Assert.IsType<TextBlock>(view.InstalledRows.Children[0]);
        Assert.Contains("not compatible with this task", message.Text);
        Assert.Equal("gemma-4-E2B.i1-Q6_K.gguf", settings.LearningLocalModel);
    });

    [Fact]
    public Task ShowsSelectedAndExplicitCustomAssignmentWithoutPerformingEffects() => dispatcher.Run(() =>
    {
        var view = new ModelManagementView("Translation", new TextBlock { Text = "Recommended fixture" }, () => { }, () => { }, null);
        var settings = new AppSettings { LearningLocalModel = "Hy-MT2-1.8B-Q4_K_M.gguf" };
        var models = new[] { new LearningInstalledModel("Hy-MT2-1.8B-Q4_K_M.gguf", "Translator", 10), new LearningInstalledModel("custom.gguf", "Custom", 10), new LearningInstalledModel("Qwen-Base-Q8.gguf", "Base", 10) };
        view.ShowModels(models, settings, LocalModelTask.Translation, settings.LearningLocalModel);
        Assert.Single(view.InstalledRows.Children); Assert.Single(view.CustomRows.Children); Assert.Equal(Visibility.Visible, view.CustomDisclosure.Visibility);
        view.CustomDisclosure.IsExpanded = true;
        view.Measure(new Size(720, 640)); view.Arrange(new Rect(0, 0, 720, 640)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(720, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        Assert.Equal(720, bitmap.PixelWidth); Assert.Equal(640, bitmap.PixelHeight);
        if (Environment.GetEnvironmentVariable("LINGROVE_MODEL_VIEW_SCREENSHOT") is { Length: > 0 } path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path); encoder.Save(stream);
        }
        string? assigned = null; view.AssignRequested += value => assigned = value;
        var row = Assert.IsType<DockPanel>(view.CustomRows.Children[0]);
        var button = Assert.IsType<Button>(row.Children[0]); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("custom.gguf", assigned);
        settings.RecordLocalModelTask("custom.gguf", LocalModelTask.Translation);
        view.ShowModels(models, settings, LocalModelTask.Translation, settings.LearningLocalModel);
        Assert.Equal(2, view.InstalledRows.Children.Count); Assert.Equal(Visibility.Collapsed, view.CustomDisclosure.Visibility);

    });
}
