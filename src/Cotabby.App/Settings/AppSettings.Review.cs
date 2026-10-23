using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

/// <summary>Six original review preference keys in the existing Windows settings file. Raw values
/// isolate damaged preferences and preserve future values across general reset; the review owner
/// supplies their Foundation-compatible projection. No API key belongs in this document.</summary>
public sealed partial class AppSettings
{
    [JsonPropertyName("ankiReviewAddress")] public JsonElement? AnkiReviewAddressValue { get; set; }
    [JsonPropertyName("ankiReviewDeck")] public JsonElement? AnkiReviewDeckValue { get; set; }
    [JsonPropertyName("ankiReviewModel")] public JsonElement? AnkiReviewModelValue { get; set; }
    [JsonPropertyName("ankiReviewMapping")] public JsonElement? AnkiReviewMappingValue { get; set; }
    [JsonPropertyName("ankiReviewRecognition")] public JsonElement? AnkiReviewRecognitionValue { get; set; }
    [JsonPropertyName("ankiReviewGap")] public JsonElement? AnkiReviewGapValue { get; set; }
}
