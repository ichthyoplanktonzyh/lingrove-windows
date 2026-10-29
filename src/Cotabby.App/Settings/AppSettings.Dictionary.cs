using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

public sealed partial class AppSettings
{
    /// <summary>Current local dictionary catalog; malformed data stays isolated from other settings.</summary>
    [JsonPropertyName("learningDictionaryCatalog.3b")]
    public JsonElement? LearningDictionaryCatalogValue { get; set; }
}
