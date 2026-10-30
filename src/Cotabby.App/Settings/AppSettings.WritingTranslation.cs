using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

public sealed partial class AppSettings
{
    /// <summary>Passive writing previews still require the separate translation enablement.</summary>
    public bool LearningSentencePreviewEnabled { get; set; } = true;
    public KeyBinding LearningReplacementShortcut { get; set; } = new(0x54, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+T");
    [JsonPropertyName("learningReferenceMode")] public JsonElement? LearningReferenceModeValue { get; set; }
    [JsonIgnore] public LearningReferenceMode LearningReferenceMode
    {
        get => LearningReferenceModeValue is { ValueKind: JsonValueKind.String } value && value.GetString() == "draft"
            ? LearningReferenceMode.Draft : LearningReferenceMode.CurrentSentence;
        set => LearningReferenceModeValue = JsonSerializer.SerializeToElement(value == LearningReferenceMode.Draft ? "draft" : "currentSentence");
    }
}
