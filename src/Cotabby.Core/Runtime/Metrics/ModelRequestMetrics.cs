using System.Text.Json.Serialization;
using Cotabby.Core.Text;

namespace Cotabby.Core.Runtime.Metrics;

/// <summary>Task identity is independent of its backend. Historical analysis remains decodable
/// without offering the retired task in new filters; durations never identify text or a notebook.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModelRequestSource>))]
public enum ModelRequestSource
{
    [JsonStringEnumMemberName("completion")] Completion, [JsonStringEnumMemberName("translation")] Translation,
    [JsonStringEnumMemberName("analysis")] Analysis, [JsonStringEnumMemberName("explanation")] Explanation,
    [JsonStringEnumMemberName("speech")] Speech, [JsonStringEnumMemberName("ocr")] Ocr,
}
/// <summary>Completed records a validated task result, independently of any later human quality
/// judgment. Failed and cancelled stay distinct in the shared opt-in history.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModelRequestOutcome>))]
public enum ModelRequestOutcome
{
    [JsonStringEnumMemberName("completed")] Completed, [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
}
/// <summary>Original selectable tasks and English resource labels. Historical analysis can be
/// decoded without reintroducing it to a new task selector; future localization owns display.</summary>
public static class ModelRequestSources
{
    public static IReadOnlyList<ModelRequestSource> All { get; } = Array.AsReadOnly(new[]
    { ModelRequestSource.Completion, ModelRequestSource.Translation, ModelRequestSource.Explanation, ModelRequestSource.Speech, ModelRequestSource.Ocr });
    public static string Label(this ModelRequestSource value) => value switch
    {
        ModelRequestSource.Completion => "Autocomplete", ModelRequestSource.Translation => "Translation", ModelRequestSource.Analysis => "Learning Analysis",
        ModelRequestSource.Explanation => "AI explanation", ModelRequestSource.Speech => "TTS", ModelRequestSource.Ocr => "Screen OCR", _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    public static string Label(this ModelRequestOutcome value) => value switch
    { ModelRequestOutcome.Completed => "Completed", ModelRequestOutcome.Failed => "Failed", ModelRequestOutcome.Cancelled => "Cancelled", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
}

/// <summary>One immutable content-free history row. Missing stages are unobserved; zero is a
/// measurement. The app's bounded Store replaces values when a late load/display stage arrives.
/// Stable IDs prevent callbacks from resurrecting cleared or evicted records.</summary>
public sealed record PerformanceMetricEntry(Guid Id, DateTimeOffset Timestamp, string ModelName, long LatencyMs,
    ModelRequestSource Source = ModelRequestSource.Completion, ModelRequestOutcome Outcome = ModelRequestOutcome.Completed,
    string? RequestId = null, long? ModelLoadMs = null, long? FirstOutputMs = null, long? UsableResultMs = null, long? DisplayMs = null)
{
    public bool Equals(PerformanceMetricEntry? other) => other is not null && Id == other.Id && Timestamp == other.Timestamp
        && CanonicalStringComparer.Instance.Equals(ModelName, other.ModelName) && LatencyMs == other.LatencyMs && Source == other.Source && Outcome == other.Outcome
        && CanonicalStringComparer.Instance.Equals(RequestId, other.RequestId) && ModelLoadMs == other.ModelLoadMs && FirstOutputMs == other.FirstOutputMs && UsableResultMs == other.UsableResultMs && DisplayMs == other.DisplayMs;
    public override int GetHashCode()
    {
        var h = new HashCode(); h.Add(Id); h.Add(Timestamp); h.Add(ModelName, CanonicalStringComparer.Instance); h.Add(LatencyMs); h.Add(Source); h.Add(Outcome);
        h.Add(RequestId is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(RequestId)); h.Add(ModelLoadMs); h.Add(FirstOutputMs); h.Add(UsableResultMs); h.Add(DisplayMs); return h.ToHashCode();
    }
}
