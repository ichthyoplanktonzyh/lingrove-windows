using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

/// <summary>Opt-in history is app state outside the general preference reset, like the original
/// UserDefaults key. The raw Data/base64 projection retains malformed history until clear/record.
/// It contains only bounded request metadata and timings, never text/audio/credentials.</summary>
public sealed partial class AppSettings
{
    [JsonPropertyName("lingrovePerformanceTrackingEnabled")] public JsonElement? PerformanceTrackingEnabledValue { get; set; }
    [JsonIgnore] public bool PerformanceTrackingEnabled
    {
        get => PerformanceTrackingEnabledValue is { ValueKind: JsonValueKind.True };
        set => PerformanceTrackingEnabledValue = JsonSerializer.SerializeToElement(value);
    }
    [JsonPropertyName("cotabbyPerformanceMetricEntries")] public JsonElement? PerformanceMetricEntriesValue { get; set; }
    [JsonPropertyName("interfaceLanguage")] public JsonElement? InterfaceLanguageValue { get; set; }
    [JsonIgnore] public string? InterfaceLanguage
    {
        get => InterfaceLanguageValue is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
        set => InterfaceLanguageValue = value is null ? null : JsonSerializer.SerializeToElement(value);
    }
}
