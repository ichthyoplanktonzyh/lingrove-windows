using Cotabby.Core.Input;

namespace Cotabby.App.Settings;

public sealed partial class AppSettings
{
    /// <summary>Passive writing previews still require the separate translation enablement.</summary>
    public bool LearningSentencePreviewEnabled { get; set; } = true;
    public KeyBinding LearningReplacementShortcut { get; set; } = new(0x54, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+T");
}
