using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.Core.Input;
using System.Windows;
using System.Windows.Controls;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Compiled settings adapters only: no recorder focus, hook or desktop input.</summary>
[Collection("WPF views")]
public sealed class ExpressionAssistShortcutTests(WpfDispatcher dispatcher)
{
    [Fact]
    public void ReadingBindingReservesOnlyWhileEnabledAndChecksPerAppOverrides()
    {
        var settings = new AppSettings(); var reading = settings.LearningReadingShortcut;
        Assert.False(settings.LearningReadingEnabled);
        Assert.Null(settings.ExpressionAssistShortcutConflict(reading));
        settings.LearningReadingEnabled = true;
        Assert.Equal("Selected Text Reading", settings.ExpressionAssistShortcutConflict(reading));
        settings.ShortcutOverrides.Add(new PerAppShortcutOverride { ProcessName = "editor", AcceptWord = reading });
        Assert.Equal("editor shortcut", GlobalShortcut.LearningReading.Conflict(settings, reading));
    }
    private static readonly KeyBinding Chord = new(0x45, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+E");

    [Fact]
    public void OtherGlobalRecordersReserveTheExpressionChordOnlyWhileEnabled()
    {
        var settings = new AppSettings { ExpressionAssistShortcut = Chord };
        Assert.Null(GlobalShortcut.Toggle.Conflict(settings, Chord));
        settings.ExpressionAssistEnabled = true;
        Assert.Equal(GlobalShortcut.ExpressionAssist.Name, GlobalShortcut.Toggle.Conflict(settings, Chord));
        Assert.Equal(GlobalShortcut.ExpressionAssist.Name, GlobalShortcut.LearningTranslation.Conflict(settings, Chord));
    }

    [Fact]
    public void ExpressionRecorderUsesAllAppConflictsAndDefaultIsClear()
    {
        var settings = new AppSettings
        {
            ShortcutOverrides = [new() { ProcessName = "editor", AcceptAll = Chord }],
        };
        Assert.Equal("Accept Entire Suggestion", GlobalShortcut.ExpressionAssist.Conflict(settings, Chord));
        Assert.False(GlobalShortcut.ExpressionAssist.Default.IsBound);
        GlobalShortcut.ExpressionAssist.Set(settings, Chord);
        Assert.Equal(Chord, settings.ExpressionAssistShortcut);
        GlobalShortcut.ExpressionAssist.Set(settings, GlobalShortcut.ExpressionAssist.Default);
        Assert.Equal("Not set", GlobalShortcut.ExpressionAssist.Label(settings));
    }

    [Fact]
    public Task SavedShortcutExplainsConflictsIntroducedAfterRecording() => dispatcher.RunAsync(() =>
    {
        var settings = new AppSettings { ExpressionAssistShortcut = Chord, LearningTranslationShortcut = Chord };
        var editor = new ShortcutEditor(GlobalShortcut.ExpressionAssist, () => settings, mutate => mutate(settings));
        var message = Assert.Single(editor.Children.OfType<TextBlock>());
        editor.Refresh(); Assert.Equal(Visibility.Collapsed, message.Visibility);
        settings.LearningTranslationEnabled = true;
        editor.Refresh();
        Assert.Equal(Visibility.Visible, message.Visibility);
        Assert.Contains("Translation", message.Text);
        settings.LearningTranslationEnabled = false;
        editor.Refresh(); Assert.Equal(Visibility.Collapsed, message.Visibility);
        return Task.CompletedTask;
    });
}
