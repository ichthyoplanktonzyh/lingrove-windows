using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

/// <summary>Opt-in history is app state outside the general preference reset, like the original
/// UserDefaults key. The raw Data/base64 projection retains malformed history until clear/record.
/// It contains only bounded request metadata and timings, never text/audio/credentials.</summary>
public sealed partial class AppSettings
{
    [JsonPropertyName("cotabbyPerformanceMetricEntries")] public JsonElement? PerformanceMetricEntriesValue { get; set; }
}
