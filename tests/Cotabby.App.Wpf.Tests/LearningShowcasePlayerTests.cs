using System.Windows;
using System.Windows.Controls;
using Cotabby.App.UI;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningShowcasePlayerTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ExampleMountSwitchAndUnmountDrain() => dispatcher.RunAsync(async () =>
    {
        var player = new LearningShowcasePlayer(autoplayOnHover: false);
        var host = new Window { Width = 600, Height = 510, Left = -10000, Top = -10000, ShowActivated = false,
            ShowInTaskbar = false, Content = new ScrollViewer { Content = player } };
        host.Show();
        try
        {
            await player.Mounted.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(7, player.ScenePicker.Items.Count);
            player.ScenePicker.SelectedIndex = 1; player.StepPicker.SelectedIndex = 2;
            player.ScenePicker.SelectedIndex = 2; player.StepPicker.SelectedIndex = 3;
            player.ScenePicker.SelectedIndex = 3; player.StepPicker.SelectedIndex = 2;
        }
        finally { host.Close(); await player.Retirement.WaitAsync(TimeSpan.FromSeconds(10)); }
    });
}
