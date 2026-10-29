using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.Core.Input;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Compiled settings adapters only: no recorder focus, hook or desktop input.</summary>
public sealed class ExpressionAssistShortcutTests
{
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
}
