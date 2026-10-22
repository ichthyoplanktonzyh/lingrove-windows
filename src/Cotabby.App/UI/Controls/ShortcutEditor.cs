using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Settings;
using KeyBinding = Cotabby.Core.Input.KeyBinding;
using KeyModifiers = Cotabby.Core.Input.KeyModifiers;

namespace Cotabby.App.UI.Controls;

/// <summary>One of the app wide shortcuts, with how to read and write it on the settings.</summary>
internal sealed class GlobalShortcut
{
    public static GlobalShortcut AcceptWord { get; } =
        new("Accept Word", KeyBinding.Tab, s => s.AcceptWord, (s, key) => s.AcceptWord = key);

    public static GlobalShortcut AcceptAll { get; } =
        new("Accept Entire Suggestion", KeyBinding.Backtick, s => s.AcceptAll, (s, key) => s.AcceptAll = key);

    public static GlobalShortcut Toggle { get; } =
        new("Toggle Cotabby", KeyBinding.Unbound, s => s.GlobalToggle, (s, key) => s.GlobalToggle = key);

    public static GlobalShortcut LearningTranslation { get; } =
        new("Translate Current Sentence", new KeyBinding(0x54, KeyModifiers.Alt, "Alt+T"),
            s => s.LearningTranslationShortcut, (s, key) => s.LearningTranslationShortcut = key);

    private static readonly GlobalShortcut[] All = [AcceptWord, AcceptAll, Toggle, LearningTranslation];

    private readonly Func<AppSettings, KeyBinding> _get;

    private GlobalShortcut(
        string name, KeyBinding @default, Func<AppSettings, KeyBinding> get, Action<AppSettings, KeyBinding> set)
    {
        Name = name;
        Default = @default;
        _get = get;
        Set = set;
    }

    public string Name { get; }
    public KeyBinding Default { get; }
    public Action<AppSettings, KeyBinding> Set { get; }

    /// <summary>True when a double tap of the accept-word key stands in for this shortcut.</summary>
    public bool UsesDoubleTap(AppSettings settings) => this == AcceptAll && settings.DoubleTapAcceptsAll;

    /// <summary>The binding in effect. Double tap leaves the stored accept-all key unused.</summary>
    public KeyBinding Binding(AppSettings settings) => UsesDoubleTap(settings) ? KeyBinding.Unbound : _get(settings);

    public string Label(AppSettings settings)
    {
        if (UsesDoubleTap(settings) && settings.AcceptWord.IsBound)
        {
            return $"{settings.AcceptWord.Label} {settings.AcceptWord.Label}";
        }
        return Binding(settings) is { IsBound: true } binding ? binding.Label : "Not set";
    }

    /// <summary>The name of another shortcut already on this key, or null when the key is free.</summary>
    public string? Conflict(AppSettings settings, KeyBinding candidate)
    {
        if (!candidate.IsBound) return null;

        var global = All.FirstOrDefault(other => other != this
            && other.Binding(settings) is var taken
            && taken.VirtualKey == candidate.VirtualKey
            && taken.Modifiers == candidate.Modifiers);
        if (global is not null) return global.Name;

        // The learning chord is global, so it must also yield to an accept shortcut a user
        // assigned only in one app. This keeps the existing per-app recorder a visible conflict
        // boundary instead of letting the translation hook consume that app's established key.
        if (this == LearningTranslation)
        {
            foreach (var app in settings.ShortcutOverrides)
            {
                if (Matches(app.AcceptWord) || Matches(app.AcceptAll)) return $"{app.ProcessName} shortcut";
            }
        }

        return null;

        bool Matches(KeyBinding? binding) => binding is { IsBound: true } key
            && key.VirtualKey == candidate.VirtualKey
            && key.Modifiers == candidate.Modifiers;
    }
}

/// <summary>A key recorder for a global shortcut with its Reset and Clear buttons.</summary>
internal sealed class ShortcutEditor : StackPanel
{
    private readonly GlobalShortcut _shortcut;
    private readonly Func<AppSettings> _settings;
    private readonly KeyRecorder _recorder;
    private readonly Button _reset;
    private readonly Button _clear;

    /// <param name="shortcut">The shortcut to edit.</param>
    /// <param name="settings">Reads the live settings, which a reset replaces.</param>
    /// <param name="change">Applies and saves one settings mutation.</param>
    public ShortcutEditor(GlobalShortcut shortcut, Func<AppSettings> settings, Action<Action<AppSettings>> change)
    {
        _shortcut = shortcut;
        _settings = settings;
        Orientation = Orientation.Horizontal;

        _recorder = new KeyRecorder { Conflict = candidate => shortcut.Conflict(settings(), candidate) };
        _recorder.Recorded += binding => change(s => shortcut.Set(s, binding));
        _reset = Ui.Button("Reset", () => change(s => shortcut.Set(s, shortcut.Default)));
        _clear = Ui.Button("Clear", () => change(s => shortcut.Set(s, KeyBinding.Unbound)));
        _reset.Margin = _clear.Margin = new Thickness(8, 0, 0, 0);

        Children.Add(_recorder);
        Children.Add(_reset);
        Children.Add(_clear);
    }

    /// <summary>Shows the current binding. Call once after construction and after every settings change.</summary>
    public void Refresh()
    {
        var settings = _settings();
        var binding = _shortcut.Binding(settings);
        bool editable = !_shortcut.UsesDoubleTap(settings);

        _recorder.Show(_shortcut.Label(settings));
        _recorder.IsEnabled = editable;
        _reset.Visibility = Shown(editable && _shortcut.Default.IsBound && binding != _shortcut.Default);
        _clear.Visibility = Shown(editable && binding.IsBound);
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
