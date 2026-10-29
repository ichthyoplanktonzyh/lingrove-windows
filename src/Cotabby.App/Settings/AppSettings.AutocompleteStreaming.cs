using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.App.Settings;

public sealed partial class AppSettings
{
    [JsonPropertyName("lingroveStreamSuggestionsWhileGenerating")] public JsonElement? StreamSuggestionsValue { get; set; }
    [JsonPropertyName("lingrovePredictAheadWhileTyping")] public JsonElement? PredictAheadValue { get; set; }
    [JsonPropertyName("lingroveShowFollowingWords")] public JsonElement? ShowFollowingWordsValue { get; set; }
    [JsonIgnore] public bool StreamSuggestionsWhileGenerating
    {
        get => StreamSuggestionsValue is { ValueKind: JsonValueKind.True };
        set => StreamSuggestionsValue = JsonSerializer.SerializeToElement(value);
    }
    [JsonIgnore] public bool PredictAheadWhileTyping
    {
        get => PredictAheadValue is not { ValueKind: JsonValueKind.False };
        set => PredictAheadValue = JsonSerializer.SerializeToElement(value);
    }
    [JsonIgnore] public bool ShowFollowingWords
    {
        get => ShowFollowingWordsValue is not { ValueKind: JsonValueKind.False };
        set => ShowFollowingWordsValue = JsonSerializer.SerializeToElement(value);
    }
}
