using Cotabby.App.Settings;
using Cotabby.Core.Input;

namespace Cotabby.App.Tests.Settings;

public sealed class ExpressionAssistSettingsTests
{
    private static readonly KeyBinding Chord = new(0x45, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+E");

    [Fact]
    public void DefaultsAndResetDoNotEnableAnInteractionOrBorrowAnEndpoint()
    {
        var settings = SettingsStore.Parse("""{"LearningEndpointEnabled":true,"LearningTargetLanguage":"French"}""");
        Assert.False(settings.ExpressionAssistEnabled);
        Assert.Equal("??", settings.ExpressionAssistTrigger);
        Assert.False(settings.ExpressionAssistShortcut.IsBound);
        settings.ExpressionAssistEnabled = true; settings.ExpressionAssistTrigger = "!!"; settings.ExpressionAssistShortcut = Chord;
        var restored = SettingsStore.Parse(SettingsStore.Serialize(settings));
        Assert.True(restored.ExpressionAssistEnabled); Assert.Equal("!!", restored.ExpressionAssistTrigger);
        Assert.Equal(Chord, restored.ExpressionAssistShortcut); Assert.True(restored.LearningEndpointEnabled);
        var reset = restored.ResetGeneralPreferences(0);
        Assert.False(reset.ExpressionAssistEnabled); Assert.Equal("??", reset.ExpressionAssistTrigger);
        Assert.False(reset.ExpressionAssistShortcut.IsBound);
        Assert.Equal("??", SettingsStore.Parse("""{"ExpressionAssistTrigger":null}""").ExpressionAssistTrigger);
    }

    [Fact]
    public void RecordingChecksEveryAppWhileDispatchResolvesOnlyTheActiveApp()
    {
        var settings = new AppSettings
        {
            ShortcutOverrides = [new() { ProcessName = "editor", AcceptWord = Chord }],
        };
        Assert.Equal("Accept Word", settings.ExpressionAssistShortcutConflict(Chord));
        Assert.Equal("Accept Word", settings.ExpressionAssistShortcutConflict(Chord, "EDITOR"));
        Assert.Null(settings.ExpressionAssistShortcutConflict(Chord, "other"));
        settings.AcceptWord = Chord;
        settings.ShortcutOverrides[0] = new() { ProcessName = "editor", AcceptWord = KeyBinding.Unbound };
        Assert.Null(settings.ExpressionAssistShortcutConflict(Chord, "editor"));
        Assert.Equal("Accept Word", settings.ExpressionAssistShortcutConflict(Chord, "other"));
    }

    [Fact]
    public void TranslationReservationFollowsItsEnabledStateAndComparesKeysRatherThanLabels()
    {
        var settings = new AppSettings { LearningTranslationShortcut = Chord };
        Assert.Null(settings.ExpressionAssistShortcutConflict(Chord));
        settings.LearningTranslationEnabled = true;
        Assert.Equal("Translation", settings.ExpressionAssistShortcutConflict(Chord with { Label = "another label" }));
        settings.LearningTranslationEnabled = false; settings.GlobalToggle = Chord;
        Assert.Equal("Toggle Lingrove", settings.ExpressionAssistShortcutConflict(Chord));
    }

    [Theory]
    [InlineData(0x45, KeyModifiers.None)]
    [InlineData(0x0D, KeyModifiers.Control)]
    [InlineData(0x1B, KeyModifiers.Alt)]
    public void ReservedOrUnmodifiedShortcutsAreRejected(int key, KeyModifiers modifiers)
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.ExpressionAssistShortcutConflict(new(key, modifiers, "test")));
        Assert.Null(settings.ExpressionAssistShortcutConflict(KeyBinding.Unbound));
    }
}
