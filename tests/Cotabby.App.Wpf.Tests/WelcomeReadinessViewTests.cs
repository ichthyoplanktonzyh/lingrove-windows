using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI;
using Cotabby.Core.Onboarding;

namespace Cotabby.App.Wpf.Tests;
[Collection("WPF views")]
public sealed class WelcomeReadinessViewTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", "model", true)]
    [InlineData("http://127.0.0.1:11434/v1", " ", false)]
    [InlineData("http://public.example/v1", "model", false)]
    [InlineData("https://name:secret@example.test/v1", "model", false)]
    public void EndpointConfigurationIsCheckedWithoutConnecting(string url, string model, bool expected) =>
        Assert.Equal(expected, WelcomeReadinessView.EndpointValid(url, model));

    [Fact]
    public Task LatestResultWinsAndRetirementJoinsIgnoredCancellation() => dispatcher.RunAsync(async () =>
    {
        var first = new TaskCompletionSource<ReadinessRow[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ReadinessRow[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0; ReadinessTask? selected = null;
        var view = new WelcomeReadinessView(_ => ++calls == 1 ? first.Task : second.Task, task => selected = task);
        var window = new Window { Width = 480, Height = 320, Content = view, ShowInTaskbar = false };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        window.SetResourceReference(Window.BackgroundProperty, "WindowBg");
        try
        {
            window.Show(); view.Refresh(); view.Refresh();
            Assert.Equal("Checking…", Status(view, 0));
            first.SetResult(Rows("old", OnboardingModelStatus.Unavailable));
            second.SetResult([new(ReadinessTask.Completion, "new", OnboardingModelStatus.Downloading),
                new(ReadinessTask.Translation, "missing.gguf", OnboardingModelStatus.Unavailable),
                new(ReadinessTask.Explanation, "cold.gguf", OnboardingModelStatus.Configured),
                new(ReadinessTask.Speech, "System", OnboardingModelStatus.NeedsConfiguration)]);
            await Task.Delay(60);
            Assert.Equal("new", Detail(view, 0));
            Assert.Equal("Downloading your model…", Status(view, 0)); Assert.Equal("Needs attention", Status(view, 1));
            Assert.Equal("Configured", Status(view, 2)); Assert.Equal("No model configured", Status(view, 3));
            ((Button)view.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(ReadinessTask.Explanation, selected);
            if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir); window.UpdateLayout(); var image = new RenderTargetBitmap(480, 320, 96, 96, PixelFormats.Pbgra32); image.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(dir, "welcome-readiness.png")); encoder.Save(stream);
            }
        }
        finally { await view.RetireAsync(); window.Close(); }
    });
    [Fact]
    public Task CloseWaitsForCancelledInspectionThatIgnoresToken() => dispatcher.RunAsync(async () =>
    {
        var held = new TaskCompletionSource<ReadinessRow[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new WelcomeReadinessView(_ => held.Task, _ => { });
        var window = new Window { Content = view, ShowInTaskbar = false }; window.Show(); view.Refresh();
        var stopping = view.RetireAsync(); Assert.False(stopping.IsCompleted); held.SetResult(Rows("late", OnboardingModelStatus.Configured));
        await stopping; Assert.NotEqual("late", Detail(view, 0)); window.Close();
    });
    private static string Detail(WelcomeReadinessView view, int index) => ((TextBlock)((StackPanel)((Grid)((Button)view.Children[index]).Content).Children[0]).Children[1]).Text;
    private static string Status(WelcomeReadinessView view, int index) => ((TextBlock)((Grid)((Button)view.Children[index]).Content).Children[1]).Text;
    private static ReadinessRow[] Rows(string value, OnboardingModelStatus status) => Enum.GetValues<ReadinessTask>().Select(task => new ReadinessRow(task, value, status)).ToArray();
}
