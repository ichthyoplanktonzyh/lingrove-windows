using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.Core.Input;

namespace Cotabby.App.UI.Settings.Panes;

internal static class ShortcutsPane
{
    public static SettingsPane Build(Rows r) => new("Shortcuts", "\uE765", "Keys that accept suggestions",
        new SettingsCard("Keys",
            r.Shortcut(GlobalShortcut.AcceptWord, "Insert the next word of the suggestion.")
                .Tagged("tab key keybind binding hotkey next word"),
            r.Shortcut(GlobalShortcut.AcceptAll, "Insert the whole remaining suggestion in one keystroke.")
                .Tagged("accept all full complete whole everything keybind binding hotkey"),
            r.Toggle("Double Tap to Accept Entire Suggestion",
                "Press the Accept Word key twice quickly to insert the whole suggestion. "
                + "A single press still inserts the next word.",
                s => s.DoubleTapAcceptsAll, (s, v) => s.DoubleTapAcceptsAll = v)
                .Tagged("double-tap twice tab tab press twice accept all"),
            r.Shortcut(GlobalShortcut.Toggle,
                "Turn Cotabby on or off from anywhere without opening the system tray.")
                .Tagged("global on off hotkey pause enable disable keybind binding"),
            r.Shortcut(GlobalShortcut.LearningTranslation,
                "Manually translate the current sentence into a read-only preview. The key is ignored while translation is disabled or conflicts with another shortcut.")
                .Tagged("learning translate sentence translation hotkey option alt t keybind binding"),
            r.Shortcut(GlobalShortcut.ExpressionAssist,
                "Describe an expression at the caret or use the selected text. Choose a modifier shortcut; Enter and Escape are reserved.")
                .Tagged("expression term assistance meaning shortcut keybind hotkey"),
            r.Shortcut(GlobalShortcut.LearningReading, "Read selected text using the learning model or bundled dictionary.")
                .Tagged("selected text reading translation lookup shortcut"),
            r.Shortcut(GlobalShortcut.ReadingPointer, "Read text under the pointer in an allowed app.")
                .Tagged("pointer reading translation shortcut"),
            r.Shortcut(GlobalShortcut.ReadingScreenshot, "Read an explicitly selected screen region in an allowed app.")
                .Tagged("screenshot region OCR reading shortcut")),
        new SettingsCard("Per-App Shortcuts",
            Rows.Note("Give a specific app its own accept key, disable an accept action there, "
                + "or keep inheriting the global shortcut."),
            Rows.Custom(Overrides(r.Context)),
            r.Stacked("Add an App", "Enter the process name of the app, such as notepad.",
                Ui.AddBox(AppsPane.ProcessNameHint, name => AddOverride(r.Context, name)))
                .Tagged("per app override process accept key")));

    private static void AddOverride(SettingsContext context, string name)
    {
        var processName = AppsPane.ProcessName(name);
        if (Find(context.Settings, processName) >= 0) return;
        context.Change(s => s.ShortcutOverrides.Add(new PerAppShortcutOverride { ProcessName = processName }));
    }

    private static int Find(AppSettings settings, string processName) => settings.ShortcutOverrides.FindIndex(
        o => string.Equals(o.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

    private static StackPanel Overrides(SettingsContext context)
    {
        var panel = new StackPanel();
        context.OnRefresh(() =>
        {
            panel.Children.Clear();
            foreach (var entry in context.Settings.ShortcutOverrides)
            {
                if (panel.Children.Count > 0) panel.Children.Add(Ui.Separator());
                panel.Children.Add(Override(context, entry));
            }
            if (panel.Children.Count == 0)
            {
                var empty = Ui.Text("No per-app shortcuts. Cotabby uses the global accept keys everywhere.", "Caption");
                empty.Margin = new Thickness(0, 12, 0, 12);
                panel.Children.Add(empty);
            }
        });
        return panel;
    }

    private static StackPanel Override(SettingsContext context, PerAppShortcutOverride entry)
    {
        void Replace(PerAppShortcutOverride? updated) => context.Change(s =>
        {
            int index = Find(s, entry.ProcessName);
            if (index < 0) return;
            if (updated is null) s.ShortcutOverrides.RemoveAt(index);
            else s.ShortcutOverrides[index] = updated;
        });

        var name = new TextBlock { Text = entry.ProcessName, FontSize = 13, FontWeight = FontWeights.SemiBold };
        var remove = Ui.Button("Remove", () => Replace(null));
        DockPanel.SetDock(remove, Dock.Right);
        name.VerticalAlignment = VerticalAlignment.Center;

        return new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 10),
            Children =
            {
                new DockPanel { Children = { remove, name } },
                OverrideKey(context, GlobalShortcut.AcceptWord, entry.AcceptWord, key => Replace(entry with { AcceptWord = key })),
                OverrideKey(context, GlobalShortcut.AcceptAll, entry.AcceptAll, key => Replace(entry with { AcceptAll = key })),
                ReadingGrant("Allow selected-text reading", entry.SelectionReadingAllowed, value => Replace(entry with { SelectionReadingAllowed = value })),
                ReadingGrant("Allow reading under pointer", entry.PointerReadingAllowed, value => Replace(entry with { PointerReadingAllowed = value })),
                ReadingGrant("Allow reading a screen region", entry.ScreenshotReadingAllowed, value => Replace(entry with { ScreenshotReadingAllowed = value })),
                IconOverride(entry.SelectionIconEnabled, value => Replace(entry with { SelectionIconEnabled = value })),
            },
        };
    }

    private static CheckBox ReadingGrant(string label, bool enabled, Action<bool> set)
    {
        var check = new CheckBox { Content = label, IsChecked = enabled, Margin = new Thickness(0, 5, 0, 0) };
        check.Checked += (_, _) => set(true); check.Unchecked += (_, _) => set(false); return check;
    }
    private static StackPanel IconOverride(bool? enabled, Action<bool?> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Selection icon", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var choices = new ComboBox { ItemsSource = new[] { "Use default", "Show", "Hide" }, SelectedIndex = enabled is null ? 0 : enabled.Value ? 1 : 2 };
        choices.SelectionChanged += (_, _) => set(choices.SelectedIndex switch { 1 => true, 2 => false, _ => null }); panel.Children.Add(choices); return panel;
    }

    /// <summary>
    /// One action of an override. A null key inherits the global shortcut and an unbound
    /// key turns the action off in that app.
    /// </summary>
    private static DockPanel OverrideKey(
        SettingsContext context, GlobalShortcut global, KeyBinding? key, Action<KeyBinding?> set)
    {
        var recorder = new KeyRecorder
        {
            Conflict = candidate => context.Settings.ExpressionAssistEnabled
                && candidate.IsBound && candidate.VirtualKey == context.Settings.ExpressionAssistShortcut.VirtualKey
                && candidate.Modifiers == context.Settings.ExpressionAssistShortcut.Modifiers
                    ? GlobalShortcut.ExpressionAssist.Name
                    : context.Settings.LearningReadingEnabled && candidate.IsBound
                        && candidate.VirtualKey == context.Settings.LearningReadingShortcut.VirtualKey
                        && candidate.Modifiers == context.Settings.LearningReadingShortcut.Modifiers
                            ? GlobalShortcut.LearningReading.Name
                            : context.Settings.LearningReadingPointerEnabled && candidate.IsBound
                                && candidate.VirtualKey == context.Settings.LearningReadingPointerShortcut.VirtualKey
                                && candidate.Modifiers == context.Settings.LearningReadingPointerShortcut.Modifiers
                                    ? GlobalShortcut.ReadingPointer.Name
                                    : context.Settings.LearningReadingScreenshotEnabled && candidate.IsBound
                                        && candidate.VirtualKey == context.Settings.LearningReadingScreenshotShortcut.VirtualKey
                                        && candidate.Modifiers == context.Settings.LearningReadingScreenshotShortcut.Modifiers
                                            ? GlobalShortcut.ReadingScreenshot.Name : null,
        };
        recorder.Recorded += binding => set(binding);
        recorder.Show(key switch
        {
            null => $"Uses global ({global.Label(context.Settings)})",
            { IsBound: false } => "Disabled",
            { } bound => bound.Label,
        });

        var inherit = Ui.Button("Inherit", () => set(null));
        inherit.IsEnabled = key is not null;
        var disable = Ui.Button("Disable", () => set(KeyBinding.Unbound));
        disable.IsEnabled = key is not { IsBound: false };

        var buttons = Ui.Across(recorder, inherit, disable);
        DockPanel.SetDock(buttons, Dock.Right);
        var label = Ui.Text(global.Name, "Caption");
        label.VerticalAlignment = VerticalAlignment.Center;
        return new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { buttons, label } };
    }
}
