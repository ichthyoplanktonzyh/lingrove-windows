using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

/// <summary>Original notebook preference keys live in the existing Windows settings file.
/// JsonElement keeps a damaged preference isolated: a wrong graph-data/mode type must not make
/// the ordinary settings deserializer discard unrelated engine or shortcut settings.</summary>
public sealed partial class AppSettings
{
    [JsonPropertyName("vocabularyGraphSettings.v1")]
    public JsonElement? VocabularyGraphSettingsValue { get; set; }
    [JsonPropertyName("vocabularyBrowserMode")]
    public JsonElement? VocabularyBrowserModeValue { get; set; }
    [JsonPropertyName("vocabularyListDensity")]
    public JsonElement? VocabularyListDensityValue { get; set; }
    [JsonPropertyName("vocabularyShowsUnexplored")]
    public JsonElement? VocabularyShowsUnexploredValue { get; set; }

    public byte[]? ReadVocabularyGraphSettings() => VocabularyGraphSettingsValue is { ValueKind: JsonValueKind.String } value
        && value.TryGetBytesFromBase64(out var bytes) ? bytes : null;
    public void WriteVocabularyGraphSettings(ReadOnlySpan<byte> data) => VocabularyGraphSettingsValue = JsonSerializer.SerializeToElement(data.ToArray());
    [JsonIgnore]
    public string VocabularyBrowserMode
    {
        get => VocabularyBrowserModeValue is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "list";
        set => VocabularyBrowserModeValue = JsonSerializer.SerializeToElement(value);
    }
    [JsonIgnore]
    public string VocabularyListDensity
    {
        get => VocabularyListDensityValue is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "compact";
        set => VocabularyListDensityValue = JsonSerializer.SerializeToElement(value);
    }
    [JsonIgnore]
    public bool VocabularyShowsUnexplored
    {
        get => VocabularyShowsUnexploredValue is { ValueKind: JsonValueKind.True };
        set => VocabularyShowsUnexploredValue = JsonSerializer.SerializeToElement(value);
    }
}
