using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Localization;
using Cotabby.App.UI.Controls;

namespace Cotabby.App.UI.Settings;

/// <summary>Displays the launch-locale restart requirement and borrows application lifetime.
/// This view never starts a process or owns the application shutdown sequence.</summary>
internal sealed class InterfaceLanguageRestartPanel : StackPanel
{
    private readonly Func<Func<Task>?> _restart;
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _button;
    internal InterfaceLanguageRestartPanel(Func<Func<Task>?> restart)
    {
        _restart = restart;
        _button = Ui.Button(InterfaceText.Get("Restart Now"), () => _ = RestartAsync());
        _button.Name = "interfaceRestartNow";
        _button.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(_notice); Children.Add(_button);
        Visibility = Visibility.Collapsed;
    }
    internal void Update(bool required)
    {
        Visibility = required ? Visibility.Visible : Visibility.Collapsed;
        _button.IsEnabled = required && _restart() is not null;
        _notice.Text = !required ? "" : _button.IsEnabled
            ? InterfaceText.Get("Restart Lingrove to apply the selected interface language.")
            : "Restart is unavailable. Quit and reopen the app to apply this language.";
    }
    private async Task RestartAsync()
    {
        if (_restart() is not { } restart) return;
        _button.IsEnabled = false;
        try { await restart(); }
        catch { _notice.Text = "The app could not restart. Quit and reopen it to apply this language."; _button.IsEnabled = true; }
    }
}
