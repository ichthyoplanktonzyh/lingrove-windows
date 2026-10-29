using Cotabby.App.Hosting;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.App.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class RecommendedSetupTests(WpfDispatcher dispatcher)
{
    private sealed class Host : IRecommendedSetupHost
    {
        public AppSettings Settings { get; } = new();
        public event Action? SettingsApplied;
        public List<LocalModelTask> Requests { get; } = [];
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? CompletionRelease;
        public bool Installed;
        public void PersistSettings() => SettingsApplied?.Invoke();
        public void PersistSelectionWithoutLoading() => SettingsApplied?.Invoke();
        public bool IsCached(CotabbyModel model) => Installed;
        public Task DownloadCompletion(CotabbyModel model, IProgress<(long Bytes, long? Total)> progress, CancellationToken token) { Requests.Add(LocalModelTask.Completion); return (CompletionRelease ?? Release).Task.WaitAsync(token); }
        public Task DownloadLearning(LearningDownloadableModel model, LocalModelTask role, IProgress<(long Bytes, long? Total)> progress, CancellationToken token) { Requests.Add(role); return Release.Task.WaitAsync(token); }
    }
    [Fact]
    public Task CardRendersWithoutStartingWorkAndExposesExplicitActions() => dispatcher.RunAsync(async () =>
    {
        var host = new Host(); await using var setup = new RecommendedSetupController(host, 8UL * 1_073_741_824);
        var view = new RecommendedSetupView(setup, () => host.Settings);
        view.Measure(new Size(500, 500)); view.Arrange(new Rect(0, 0, 500, 500)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(500, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        if (Environment.GetEnvironmentVariable("LINGROVE_RECOMMENDED_SETUP_RENDER") is { Length: > 0 } path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path); encoder.Save(output);
        }
        Assert.Empty(host.Requests);
        FindButton(view, "Use recommended setup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(3, host.Requests.Count); setup.Cancel(); await setup.CurrentWork;
    });
    private static Button FindButton(DependencyObject node, string title)
    {
        if (node is Button button && Equals(button.Content, title)) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindButton(VisualTreeHelper.GetChild(node, i), title); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Button not found");
    }
    [Fact]
    public Task ApplyIsExplicitAndManualSelectionRetiresPendingCompletion() => dispatcher.RunAsync(async () =>
    {
        var host = new Host(); await using var setup = new RecommendedSetupController(host, 16UL * 1_073_741_824);
        Assert.Empty(host.Requests); setup.Apply();
        Assert.Equal(3, host.Requests.Count); Assert.Equal("tabby-2-base", host.Settings.RecommendedCompletionTarget);
        Assert.True(host.Settings.LearningTranslationEnabled);
        host.Settings.ActiveModelId = "custom:manual.gguf"; host.PersistSettings();
        host.Installed = true; host.Release.SetResult(); await setup.CurrentWork;
        Assert.Equal("custom:manual.gguf", host.Settings.ActiveModelId); Assert.Null(host.Settings.RecommendedCompletionTarget);
    });
    [Fact]
    public Task CompletedCompletionSelectsWithoutWaitingForOtherTasks() => dispatcher.RunAsync(async () =>
    {
        var host = new Host { CompletionRelease = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var setup = new RecommendedSetupController(host, 16UL * 1_073_741_824);
        setup.Apply(); host.Installed = true; host.CompletionRelease.SetResult();
        for (int i = 0; i < 40 && host.Settings.RecommendedCompletionTarget is not null; i++) await Task.Delay(10);
        Assert.Equal("tabby-2-base", host.Settings.ActiveModelId); Assert.Null(host.Settings.RecommendedCompletionTarget); Assert.True(setup.Busy);
        host.Release.SetResult(); await setup.CurrentWork;
    });
    [Fact]
    public Task ExistingEndpointsRemainIndependentAndRetryDoesNotReenableTranslation() => dispatcher.RunAsync(async () =>
    {
        var host = new Host(); host.Settings.Engine = EngineKind.Endpoint; host.Settings.EndpointModel = "existing-completion";
        host.Settings.LearningBackend = LearningTranslationBackend.Endpoint; host.Settings.LearningEndpointModel = "existing-translation";
        await using var setup = new RecommendedSetupController(host, 8UL * 1_073_741_824);
        setup.Apply(); Assert.Equal([LocalModelTask.Explanation], host.Requests);
        host.Release.SetResult(); await setup.CurrentWork;
        Assert.Equal("existing-completion", host.Settings.EndpointModel); Assert.Equal("existing-translation", host.Settings.LearningEndpointModel);
        host.Settings.LearningTranslationEnabled = false; setup.Retry(); await setup.CurrentWork;
        Assert.False(host.Settings.LearningTranslationEnabled);
    });
    [Fact]
    public Task ReceiptSurvivesReconstructionAndManualNavigationDoesNotCancelTransfer() => dispatcher.RunAsync(async () =>
    {
        var host = new Host(); await using var setup = new RecommendedSetupController(host, 16UL * 1_073_741_824);
        setup.Apply(); var target = host.Settings.RecommendedCompletionTarget;
        await using var reopened = new RecommendedSetupController(host, 16UL * 1_073_741_824);
        Assert.Equal(target, host.Settings.RecommendedCompletionTarget); Assert.Equal(3, host.Requests.Count);
        reopened.UseManualConfiguration(); Assert.Null(host.Settings.RecommendedCompletionTarget); Assert.True(setup.Busy);
        host.Release.SetResult(); await setup.CurrentWork; Assert.Null(host.Settings.ActiveModelId);
    });
    [Fact]
    public Task CancelDrainsEveryTaskAndKeepsPendingReceiptForRetry() => dispatcher.RunAsync(async () =>
    {
        var host = new Host(); await using var setup = new RecommendedSetupController(host, 8UL * 1_073_741_824);
        setup.Apply(); setup.Cancel(); await setup.CurrentWork;
        Assert.False(setup.Busy); Assert.Equal("tabby-2-nano", host.Settings.RecommendedCompletionTarget);
        Assert.Equal("Cancelled", setup.Status(LocalModelTask.Translation, setup.Plan.Translation.Filename));
        setup.Retry(); Assert.Equal(6, host.Requests.Count); host.Release.SetResult(); await setup.CurrentWork;
    });
}
