using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Localization;
using Cotabby.Core.Settings;

namespace Cotabby.App.UI.Settings.Panes;

internal static class GeneralPane
{
    public static SettingsPane Build(Rows r) => new("General", "\uE713", "Core toggles and behavior",
        Interface(r),
        new SettingsCard("Status",
            r.Toggle("Enable Cotabby", "Turn Cotabby off everywhere without quitting the app.",
                s => s.Enabled, (s, v) => s.Enabled = v)
                .Tagged("on off disable toggle global pause resume active status stop start"),
            r.Toggle("Open at Login", "Start Cotabby automatically when you sign in to Windows.",
                s => s.LaunchAtLogin, (s, v) => s.LaunchAtLogin = v)
                .Tagged("startup launch boot start autostart sign in")),
        new SettingsCard("Power",
            r.Toggle("Pause on Battery Saver", "Pause suggestions while battery saver is on to reduce battery use.",
                s => s.PauseOnBatterySaver, (s, v) => s.PauseOnBatterySaver = v)
                .Tagged("low power energy automatic auto disable")),
        new SettingsCard("System Tray",
            r.Toggle("Show Word Count in Tray", "Show a running count of words you've accepted in the tray menu.",
                s => s.ShowTrayWordCount, (s, v) => s.ShowTrayWordCount = v)
                .Tagged("words stats counter statistics menu")),
        new SettingsCard("Help",
            r.Action("Onboarding", "Replay the first-run setup walkthrough.", "Open Welcome Guide",
                () => WelcomeWindow.ShowFor(r.Context.Host))
                .Tagged("welcome guide tutorial intro help getting started first run")),
        new SettingsCard("Reset",
            r.Action("Reset All Settings",
                "Restore every Cotabby setting to its original default. "
                + "This does not change your chosen model or your accepted-word count.",
                "Reset All Settings…", () => Reset(r.Context))
                .Tagged("defaults restore factory clear start over revert wipe erase")));

    private sealed record LanguageChoice(string? Code, string Name)
    {
        public override string ToString() => Name;
    }
    private static SettingsCard Interface(Rows r)
    {
        var choices = new List<LanguageChoice> { new(null, InterfaceText.Get("Follow System")) };
        choices.AddRange(InterfaceLanguagePolicy.Supported.Select(value => new LanguageChoice(value.Code, value.Name)));
        var combo = new ComboBox { MinWidth = 180 };
        var restart = new InterfaceLanguageRestartPanel(() => InterfaceText.RequestRestart);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        bool refreshing = false;
        void Refresh()
        {
            refreshing = true;
            try
            {
                string? stored = r.Context.Settings.InterfaceLanguage;
                string? selected = stored is null ? null : InterfaceLanguagePolicy.Normalize(stored) ?? stored;
                var visible = choices.ToList();
                if (stored is not null && InterfaceLanguagePolicy.Normalize(stored) is null)
                    visible.Insert(0, new(stored, $"{InterfaceText.Get("Existing app language")}: {stored}"));
                combo.ItemsSource = visible;
                combo.SelectedItem = visible.First(x => x.Code == selected);
                restart.Update(selected != InterfaceText.LaunchSelection);
            }
            finally { refreshing = false; }
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (refreshing || combo.SelectedItem is not LanguageChoice choice) return;
            if (choice.Code is not null && InterfaceLanguagePolicy.Normalize(choice.Code) is null) return;
            var previous = r.Context.Settings.InterfaceLanguageValue;
            r.Context.Settings.InterfaceLanguage = choice.Code;
            try { r.Context.Host.SettingsStore.Save(r.Context.Settings); error.Text = ""; }
            catch (Exception failure) when (failure is System.IO.IOException or UnauthorizedAccessException)
            { r.Context.Settings.InterfaceLanguageValue = previous; error.Text = "Could not save the interface language."; }
            Refresh();
        };
        r.Context.OnRefresh(Refresh);
        return new SettingsCard(InterfaceText.Get("Interface"),
            r.Labeled(InterfaceText.Get("Interface Language"), InterfaceText.Get("Choose the language used in Lingrove's interface. Changes apply after restarting."), combo),
            Rows.Custom(restart),
            Rows.Custom(error),
            Rows.Note("Some Windows screens and messages are still available only in English."));
    }

    private static void Reset(SettingsContext context)
    {
        var answer = MessageBox.Show(
            "Every Cotabby setting returns to its original default. This can't be undone.",
            "Reset all settings to their defaults?",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK) context.Host.ResetSettings();
    }
}
