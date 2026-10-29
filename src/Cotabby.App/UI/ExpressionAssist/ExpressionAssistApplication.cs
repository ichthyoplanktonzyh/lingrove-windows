using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Suggestions;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>App-owned composition of the existing feature/session, shared focus/input and real
/// explanation generator. Detached preferences keep the hook path free of mutable settings/IO.
/// Native effects and the shared explanation runtime remain borrowed, separately drained owners.</summary>
internal sealed class ExpressionAssistApplication : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ExpressionAssistFeature _feature;
    private Preferences _preferences = Preferences.From(new AppSettings(), false);
    private long _revision;
    private Task? _disposal;

    internal ExpressionAssistApplication(Dispatcher dispatcher, IFocusTracker focus, IKeyboardHook hook,
        Func<Func<bool>, FocusedField?> guardedRefresh, IExpressionAssistEditor editor,
        Func<ExpressionAssistRequest, CancellationToken, Task<ExpressionAssistResult>> generate,
        Action beforePresent)
    {
        dispatcher.VerifyAccess(); _dispatcher = dispatcher;
        _feature = new(dispatcher, focus, hook, guardedRefresh, field =>
        {
            beforePresent();
            var window = new ExpressionAssistWindow(field.CaretRect);
            var session = new ExpressionAssistSession(dispatcher, editor, window, generate, () =>
            {
                var preferences = Volatile.Read(ref _preferences);
                return (preferences.Target, preferences.Explanation);
            });
            window.Bind(session);
            return (session, window.Close);
        }, (field, settings) => field is null || Volatile.Read(ref _preferences).Shortcuts.ExpressionAssistShortcutConflict(
            new(settings.ShortcutKey, settings.ShortcutModifiers, ""), field.ProcessName) is not null,
            fieldAvailable: FieldAvailable);
        // Subscribe before the other inline features. Shared listeners are started by AppHost.
        _feature.Start(new());
    }

    internal bool IsActive => _feature.IsActive;

    internal void ApplySettings(AppSettings settings, bool batterySaverOn)
    {
        _dispatcher.VerifyAccess();
        if (_disposal is not null) return;
        var next = Preferences.From(settings, batterySaverOn);
        if (_revision != 0 && _preferences.SameInteraction(next)) return;
        Volatile.Write(ref _preferences, next);
        _feature.SettingsChanged(new(next.Enabled, next.Trigger, next.Shortcut.VirtualKey,
            next.Shortcut.Modifiers, next.Availability.Enabled, ++_revision));
    }

    internal bool FieldAvailable(FocusedField field)
    {
        var preferences = Volatile.Read(ref _preferences);
        return SuggestionAvailability.DisabledReason(preferences.Availability, DateTimeOffset.UtcNow, preferences.BatterySaverOn) is null
            // Explicit expression assistance accepts a selection; all environment/credential gates remain.
            && SuggestionAvailability.DisabledReason(preferences.Availability, field with { HasSelection = false }) is null;
    }

    public ValueTask DisposeAsync()
    {
        if (!_dispatcher.CheckAccess())
            return new(_dispatcher.InvokeAsync(() => DisposeAsync().AsTask()).Task.Unwrap());
        return new(_disposal ??= _feature.DisposeAsync().AsTask());
    }

    private sealed record Preferences(bool Enabled, string Trigger, KeyBinding Shortcut,
        LearningLanguage Target, LearningLanguage Explanation, LearningModelSnapshot Model,
        SuggestionSettings Availability, bool BatterySaverOn, AppSettings Shortcuts)
    {
        internal static Preferences From(AppSettings settings, bool batterySaverOn) => new(
            settings.ExpressionAssistEnabled, settings.ExpressionAssistTrigger ?? "??", settings.ExpressionAssistShortcut,
            Enum.IsDefined(settings.LearningTargetLanguage) ? settings.LearningTargetLanguage : LearningLanguage.English,
            settings.ResolvedLearningExplanationLanguage, settings.LearningExplanationSnapshot(),
            settings.ToSuggestionSettings(), batterySaverOn,
            new AppSettings
            {
                AcceptWord = settings.AcceptWord, AcceptAll = settings.AcceptAll, GlobalToggle = settings.GlobalToggle,
                LearningTranslationEnabled = settings.LearningTranslationEnabled,
                LearningTranslationShortcut = settings.LearningTranslationShortcut,
                ShortcutOverrides = (settings.ShortcutOverrides ?? []).ToList(),
            });

        internal bool SameInteraction(Preferences other) => Enabled == other.Enabled && Trigger == other.Trigger
            && Shortcut == other.Shortcut && Target == other.Target && Explanation == other.Explanation && Model == other.Model
            && Availability.Enabled == other.Availability.Enabled && Availability.Pause == other.Availability.Pause
            && Availability.PauseOnBatterySaver == other.Availability.PauseOnBatterySaver && BatterySaverOn == other.BatterySaverOn
            && Availability.SuggestInIntegratedTerminals == other.Availability.SuggestInIntegratedTerminals
            && Availability.BlockedApps.SetEquals(other.Availability.BlockedApps)
            && Shortcuts.AcceptWord == other.Shortcuts.AcceptWord && Shortcuts.AcceptAll == other.Shortcuts.AcceptAll
            && Shortcuts.GlobalToggle == other.Shortcuts.GlobalToggle
            && Shortcuts.LearningTranslationEnabled == other.Shortcuts.LearningTranslationEnabled
            && Shortcuts.LearningTranslationShortcut == other.Shortcuts.LearningTranslationShortcut
            && Shortcuts.ShortcutOverrides.SequenceEqual(other.Shortcuts.ShortcutOverrides);
    }
}
