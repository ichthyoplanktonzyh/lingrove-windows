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
    public Task SupersededScanFailuresAndClosedViewsCannotOverwriteCurrentResults() => dispatcher.RunAsync(async () =>
    {
        var scan = new ModelScanLifetime(); var old = new TaskCompletionSource<string>(); var newer = new TaskCompletionSource<string>();
        string status = "loading";
        var oldWork = scan.RunAsync(() => old.Task, value => status = value, () => status = "old failure", CancellationToken.None);
        var newWork = scan.RunAsync(() => newer.Task, value => status = value, () => status = "new failure", CancellationToken.None);
        newer.SetResult("new result"); await newWork; old.SetException(new IOException("fixture")); await oldWork;
        Assert.Equal("new result", status);
        var pending = new TaskCompletionSource<string>();
        var last = scan.RunAsync(() => pending.Task, value => status = value, () => status = "late failure", CancellationToken.None);
        scan.Close(); pending.SetException(new IOException("fixture")); await last; Assert.Equal("new result", status);
    });

    [Fact]
    public Task TransferProgressAndFailureFollowTheReopenedView() => dispatcher.Run(() =>
    {
        var store = new ModelTransferStatusStore(); var previous = new TextBlock();
        store.Bind("Model.gguf", previous, "Not installed"); store.Set("Model.gguf", "Downloading 20%");
        Assert.Equal("Downloading 20%", previous.Text);
        store.BeginView(); var current = new TextBlock(); store.Bind("model.gguf", current, "Not installed");
        Assert.Equal("Downloading 20%", current.Text);
        store.Set("MODEL.gguf", "Download failed");
        Assert.Equal("Download failed", current.Text); Assert.Equal("Downloading 20%", previous.Text);
        store.Start("model.gguf");
        Assert.Equal(ModelTransferStatusStore.Starting, current.Text);
        store.BeginView(); var reopened = new TextBlock(); store.Bind("Model.gguf", reopened, "Not installed");
        Assert.Equal(ModelTransferStatusStore.Starting, reopened.Text);
    });

    [Fact]
    public Task PowerPickerMatchesTheSameIdsAsRuntimeResolution() => dispatcher.Run(() =>
    {
        var builtin = ModelCatalog.All[0];
        var custom = new CotabbyModel { Id = "custom:model.gguf", DisplayName = "model", FileName = "model.gguf", DownloadUrl = "", ApproxSizeBytes = 1, ContextLength = 4096 };
        var models = new[] { builtin, custom };
        var wrongCaseBuiltin = EnginePane.PowerChoices(models, "TABBY-2-MINI");
        Assert.False(Assert.Single(wrongCaseBuiltin, item => item.Id == "TABBY-2-MINI").Selectable);
        Assert.True(Assert.Single(wrongCaseBuiltin, item => item.Id == builtin.Id).Selectable);
        var customCase = EnginePane.PowerChoices(models, "custom:MODEL.gguf");
        Assert.True(Assert.Single(customCase, item => item.Id == "custom:MODEL.gguf").Selectable);
        var wrongPrefix = EnginePane.PowerChoices(models, "CUSTOM:model.gguf");
        Assert.False(Assert.Single(wrongPrefix, item => item.Id == "CUSTOM:model.gguf").Selectable);
    });

    [Fact]
    public Task RetainedSettingsAreBoundedOnlyForDisplayAndKnownAliasesRemainVisible() => dispatcher.Run(() =>
    {
        var raw = "unknown\n\u202e" + new string('x', 5000);
        var settings = new AppSettings { LearningLocalModel = raw };
        var view = new ModelManagementView("Translation", new TextBlock(), () => { }, () => { }, null);
        view.ShowModels([], settings, LocalModelTask.Translation, raw);
        var retained = Assert.IsType<TextBlock>(view.InstalledRows.Children[0]).Text;
        Assert.True(retained.Length < 400); Assert.DoesNotContain('\n', retained); Assert.DoesNotContain('\u202e', retained);
        Assert.Same(raw, settings.LearningLocalModel);
        var source = "https://example.test/" + new string('y', 5000) + "\u202e";
        var safe = ModelDisplayText.Safe(source); Assert.True(safe.Length <= 257); Assert.DoesNotContain('\u202e', safe);
        settings.RecordLocalModelTask("custom.gguf", LocalModelTask.Translation);
        settings.LocalModelSourcesValue["custom.gguf"] = source;
        view.ShowModels([new LearningInstalledModel("custom.gguf", "custom", 1)], settings, LocalModelTask.Translation, "custom.gguf");
        var row = Assert.IsType<DockPanel>(view.InstalledRows.Children[0]);
        var details = Assert.IsType<StackPanel>(row.Children[1]);
        var disclosure = Assert.IsType<Expander>(details.Children[1]);
        var detailText = Assert.IsType<TextBlock>(disclosure.Content).Text;
        Assert.True(detailText.Length < 350); Assert.DoesNotContain('\u202e', detailText);
        Assert.Equal(source, settings.LocalModelSourcesValue["custom.gguf"]);
        var filename = "safe\u202etxt.gguf";
        var custom = new CotabbyModel { Id = "custom:" + filename, FileName = filename, DisplayName = filename, DownloadUrl = "", ApproxSizeBytes = 1, ContextLength = 4096 };
        var choice = Assert.Single(EnginePane.PowerChoices([custom], custom.Id), item => item.Id == custom.Id);
        Assert.Equal(custom.Id, choice.Id); Assert.DoesNotContain('\u202e', choice.Label);
        Assert.DoesNotContain('\u202e', ModelDisplayText.Name(filename));
        Assert.True(ModelDisplayText.Safe(string.Concat(Enumerable.Repeat("😀", 100)), maximumUtf16: 120).Length <= 120);
        Assert.Equal("Lingrove Base · not compatible with this task", ModelDisplayText.Unavailable("gemma-4-E2B.i1-Q6_K.gguf", LocalModelTask.Translation));
    });

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
