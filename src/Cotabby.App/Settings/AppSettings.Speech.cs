using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

/// <summary>Original speech preference keys in the shared Windows settings document. Raw JSON
/// preserves damaged or future values until an explicit edit; only the speech owner projects
/// them. Profile bytes remain a base64 Data value. Credentials never belong in this document.</summary>
public sealed partial class AppSettings
{
    [JsonPropertyName("learningSpeech.provider")] public JsonElement? SpeechProviderValue { get; set; }
    [JsonPropertyName("learningSpeech.localModel")] public JsonElement? SpeechLocalModelValue { get; set; }
    [JsonPropertyName("learningSpeech.localProjection")] public JsonElement? SpeechLocalProjectionValue { get; set; }
    [JsonPropertyName("learningSpeech.referenceAudio")] public JsonElement? SpeechReferenceAudioValue { get; set; }
    [JsonPropertyName("learningSpeech.endpointURL")] public JsonElement? SpeechEndpointUrlValue { get; set; }
    [JsonPropertyName("learningSpeech.endpointModel")] public JsonElement? SpeechEndpointModelValue { get; set; }
    [JsonPropertyName("learningSpeech.endpointVoice")] public JsonElement? SpeechEndpointVoiceValue { get; set; }
    [JsonPropertyName("learningSpeech.rateMultiplier")] public JsonElement? SpeechRateValue { get; set; }
    [JsonPropertyName("learningSpeech.voiceIDs")] public JsonElement? SpeechVoiceIdsValue { get; set; }
    [JsonPropertyName("learningSpeech.parameters.v1")] public JsonElement? SpeechParametersValue { get; set; }
}
