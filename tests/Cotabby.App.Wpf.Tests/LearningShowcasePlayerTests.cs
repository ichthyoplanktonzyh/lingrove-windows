using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.App.UI;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningShowcasePlayerTests(WpfDispatcher dispatcher)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExampleMountSwitchAndUnmountDrain(bool dark) => dispatcher.RunAsync(async () =>
    {
        var player = new LearningShowcasePlayer(autoplayOnHover: false, silentSpeech: true);
        var host = new Window { Width = 650, Height = 660, Left = -10000, Top = -10000, ShowActivated = false,
            ShowInTaskbar = false, Content = new ScrollViewer { Content = player } };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        WindowTheme.ApplyPalette(host.Resources, dark);
        host.SetResourceReference(Control.BackgroundProperty, "WindowBg"); host.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        host.Show();
        try
        {
            await player.Mounted.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(7, player.ScenePicker.Items.Count);
            for (int scene = 0; scene < 7; scene++)
            {
                player.ScenePicker.SelectedIndex = scene;
                for (int step = 0; step < LearningShowcaseCatalog.Scenes[scene].Steps.Count; step++)
                {
                    player.StepPicker.SelectedIndex = step;
                    await player.StageWork.WaitAsync(TimeSpan.FromSeconds(10));
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Assert.NotNull(player.SceneContent);
                    Assert.Equal(step, player.StepPicker.SelectedIndex);
                    if (step == (scene == 0 ? 1 : scene == 1 ? 2 : scene == 2 ? 6 : scene == 3 ? 2 : scene == 4 ? 1 : scene == 5 ? 2 : 1))
                        Render(host, $"showcase-{scene}-{(dark ? "dark" : "light")}.png");
                    if (scene == 6 && step >= 2)
                    {
                        var review = Assert.IsType<LearningShowcaseReview>(player.SceneContent);
                        Assert.NotNull(review.Session?.Binding?.NoteId);
                        Assert.Equal("Lingrove sample", review.Session.Binding.Profile);
                    }
                }
            }
        }
        finally { host.Close(); await player.DisposeAsync(); }
    });

    [Fact]
    public Task WelcomeButtonsOpenTheSharedPlayerAndCloseBothAreas() => dispatcher.RunAsync(async () =>
    {
        var choices = ProductFeatureExamples.WelcomeChoices(() => "??", silentSpeech: true);
        var host = new Window { Width = 500, Height = 240, Content = choices, ShowActivated = false, ShowInTaskbar = false };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        host.Show();
        try
        {
            foreach (var button in ((Panel)choices).Children.OfType<Button>())
            {
                LearningViewProbe.Click(button);
                var example = Assert.Single(host.OwnedWindows.Cast<Window>());
                var control = Assert.Single(Descendants(example).OfType<ProductFeatureExamples>());
                if (control.SelectedIndex == 1)
                {
                    var player = Assert.IsType<LearningShowcasePlayer>(((TabItem)control.Items[1]).Content);
                    await player.Mounted; Assert.Equal(0, player.ScenePicker.SelectedIndex);
                }
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                example.Closed += (_, _) => closed.TrySetResult(); example.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(example.IsVisible);
            }
        }
        finally { host.Close(); await LearningShowcasePlayer.RetireAllAsync(); }
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static void Render(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_SHOWCASE_RENDER_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew); encoder.Save(output);
    }
}
