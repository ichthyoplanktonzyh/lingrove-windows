using System.Globalization;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Text;

/// <summary>Pure UserDefaults projection for the Windows JSON preference adapter. Raw stored
/// values remain untouched; string/coercing-bool/NSNumber-cast-bool are deliberately distinct,
/// because the original review settings use all three Foundation getters.</summary>
public static class FoundationPreferenceValue
{
    public static string? StringValue(JsonElement? raw)
    {
        if (raw is not { } value) return null;
        try
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => LearningExplanationText.Fits(value.GetString(), int.MaxValue) ? value.GetString() : null,
                JsonValueKind.True => "1", JsonValueKind.False => "0",
                JsonValueKind.Number when value.TryGetDouble(out double number) && double.IsFinite(number)
                    => number.ToString("G16", CultureInfo.InvariantCulture).Replace('E', 'e'),
                _ => null,
            };
        }
        catch (InvalidOperationException) { return null; }
    }
    public static bool? CastBoolean(JsonElement? raw) => raw is not { } value ? null : value.ValueKind switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetDouble(out double number) && number is 0 or 1 => number == 1,
        _ => null,
    };
    public static bool BooleanValue(JsonElement? raw)
    {
        if (raw is not { } value) return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)) return double.IsFinite(number) && number != 0;
        if (value.ValueKind != JsonValueKind.String) return false;
        string? text = StringValue(value);
        return text is not null && (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text == "1");
    }
}
