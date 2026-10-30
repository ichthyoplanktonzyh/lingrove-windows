using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Localization;

namespace Cotabby.App.UI;

/// <summary>Home and the welcome sheet share one player. Mounting learning creates its private
/// sample owners; leaving the area retires them. Neither entry borrows production notebook data.</summary>
internal sealed class ProductFeatureExamples : TabControl
{
    internal ProductFeatureExamples(Func<string> trigger, bool learningFirst = false, bool autoplayOnHover = true, bool silentSpeech = false, Func<Cotabby.App.Settings.AppSettings>? current = null)
    {
        Items.Add(new TabItem { Header = InterfaceText.Get("Autocomplete"), Content = new WelcomeCompletionDemo() });
        Items.Add(new TabItem { Header = InterfaceText.Get("Language Learning"), Content = new LearningShowcasePlayer(trigger, autoplayOnHover, silentSpeech, current) });
        SelectedIndex = learningFirst ? 1 : 0;
    }

    internal static FrameworkElement WelcomeChoices(Func<string> trigger, bool silentSpeech = false, Func<Cotabby.App.Settings.AppSettings>? current = null)
    {
        var choices = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 18, 0, 0) };
        foreach (bool learning in new[] { false, true })
        {
            var title = InterfaceText.Get(learning ? "Language Learning" : "Autocomplete");
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var caption = new TextBlock { Text = InterfaceText.Get("View example"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); text.Children.Add(caption);
            var button = new Button { Content = text, Padding = new Thickness(16), Margin = new Thickness(4) };
            button.SetResourceReference(StyleProperty, "Btn");
            System.Windows.Automation.AutomationProperties.SetName(button, title + " · " + caption.Text);
            button.Click += (_, _) => new ExampleWindow(trigger, learning, silentSpeech, current) { Owner = Window.GetWindow(choices) }.Show();
            choices.Children.Add(button);
        }
        return choices;
    }

    private sealed class ExampleWindow : Window
    {
        private bool _retired;
        private Task? _retirement;
        private readonly ProductFeatureExamples _examples;
        internal ExampleWindow(Func<string> trigger, bool learning, bool silentSpeech, Func<Cotabby.App.Settings.AppSettings>? current)
        {
            Title = InterfaceText.Get(learning ? "Language Learning" : "Autocomplete");
            Width = 680; Height = 650; MinWidth = 520; MinHeight = 450;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
            SetResourceReference(BackgroundProperty, "WindowBg"); SetResourceReference(ForegroundProperty, "TextPrimary"); WindowTheme.Follow(this);
            var body = new DockPanel { Margin = new Thickness(20) };
            var close = new Button { Content = InterfaceText.Get("Close"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 12) };
            close.SetResourceReference(StyleProperty, "Btn"); close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Top); body.Children.Add(close);
            _examples = new(trigger, learning, autoplayOnHover: false, silentSpeech, current); body.Children.Add(new ScrollViewer { Content = _examples, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = body;
            Closing += OnClosing;
        }
        private async void OnClosing(object? sender, CancelEventArgs args)
        {
            if (_retired) return; args.Cancel = true; IsEnabled = false;
            try { await (_retirement ??= RetireAsync()); }
            catch
            {
                _retired = true; IsEnabled = true;
                Content = new TextBlock { Text = "The example could not finish closing. Close this window to dismiss it.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) };
                return;
            }
            _retired = true;
            await System.Windows.Threading.Dispatcher.Yield();
            Close();
        }
        private async Task RetireAsync()
        {
            if (((TabItem)_examples.Items[1]).Content is LearningShowcasePlayer player) await player.DisposeAsync();
        }
    }
}
