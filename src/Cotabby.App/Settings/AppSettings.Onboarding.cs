using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

public sealed partial class AppSettings
{
    // Windows presents recommended and manual template choices as separate pages, so this uses
    // its own step-numbering version rather than interpreting the Mac raw step index.
    [JsonPropertyName("lingroveOnboardingProgressWindows1")] public JsonElement? OnboardingProgressValue { get; set; }
    [JsonIgnore] public (int Step, bool Manual) OnboardingProgress
    {
        get
        {
            if (OnboardingProgressValue is not { ValueKind: JsonValueKind.Object } value) return (0, false);
            if (!value.TryGetProperty("step", out var raw) || raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt32(out var step) || step is < 0 or > 6) return (0, false);
            bool manual = value.TryGetProperty("manual", out var flag) && flag.ValueKind == JsonValueKind.True;
            return (step, manual);
        }
        set => OnboardingProgressValue = JsonSerializer.SerializeToElement(new { step = value.Step, manual = value.Manual });
    }
    internal bool RecordOnboardingStep(int step, bool manual)
    {
        if (step is < 0 or > 6) return false;
        var previous = OnboardingProgress;
        if (step <= previous.Step && manual == previous.Manual) return false;
        OnboardingProgress = (Math.Max(previous.Step, step), manual);
        return true;
    }
}
