using System.Globalization;
using System.Text.Json;
using Cotabby.Core.Text;

namespace Cotabby.Core.Runtime.Metrics;

/// <summary>Original history Codable envelope with 2001-epoch dates and legacy source/outcome
/// defaults. One damaged known row rejects the whole blob. Unknown members remain ignored;
/// the app Store alone chooses retention and when to persist this detached value list.</summary>
public static class PerformanceMetricCodec
{
    private static readonly DateTimeOffset Epoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static byte[] Encode(IReadOnlyList<PerformanceMetricEntry> entries)
    {
        using var output = new MemoryStream(); using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartArray(); foreach (var value in entries)
            {
                if (!Enum.IsDefined(value.Source) || !Enum.IsDefined(value.Outcome)) throw new JsonException();
                writer.WriteStartObject(); writer.WriteString("id", value.Id.ToString("D").ToUpperInvariant());
                writer.WriteNumber("timestamp", (value.Timestamp.UtcTicks - Epoch.UtcTicks) / (decimal)TimeSpan.TicksPerSecond);
                writer.WriteString("modelName", value.ModelName); writer.WriteNumber("latencyMs", value.LatencyMs);
                writer.WriteString("source", JsonSerializer.SerializeToElement(value.Source).GetString());
                writer.WriteString("outcome", JsonSerializer.SerializeToElement(value.Outcome).GetString());
                if (value.RequestId is not null) writer.WriteString("requestID", value.RequestId);
                Number("modelLoadMs", value.ModelLoadMs); Number("firstOutputMs", value.FirstOutputMs); Number("usableResultMs", value.UsableResultMs); Number("displayMs", value.DisplayMs);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            void Number(string key, long? value) { if (value is { } number) writer.WriteNumber(key, number); }
        }
        return output.ToArray();
    }
    public static IReadOnlyList<PerformanceMetricEntry>? Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(FoundationJsonInput.Utf8Input(bytes, validateUtf8: false), new JsonDocumentOptions { MaxDepth = 512 });
            var entries = new List<PerformanceMetricEntry>(); foreach (var item in document.RootElement.EnumerateArray())
            {
                var fields = new Dictionary<string, JsonElement>(CanonicalStringComparer.Instance);
                foreach (var property in item.EnumerateObject()) fields.TryAdd(property.Name, property.Value);
                string id = fields["id"].GetString()!; if (!Guid.TryParseExact(id, "D", out Guid uuid)) throw new JsonException();
                var timestamp = Epoch.AddTicks(checked((long)decimal.Round(fields["timestamp"].GetDecimal() * TimeSpan.TicksPerSecond, 0, MidpointRounding.ToEven)));
                var source = Text("source") ?? "completion"; var outcome = Text("outcome") ?? "completed";
                entries.Add(new(uuid, timestamp, fields["modelName"].GetString() ?? throw new JsonException(), FoundationJsonInteger.Read(fields["latencyMs"]),
                    source switch
                    {
                        "completion" => ModelRequestSource.Completion, "translation" => ModelRequestSource.Translation,
                        "analysis" => ModelRequestSource.Analysis, "explanation" => ModelRequestSource.Explanation,
                        "speech" => ModelRequestSource.Speech, "ocr" => ModelRequestSource.Ocr, _ => throw new JsonException(),
                    }, outcome switch { "completed" => ModelRequestOutcome.Completed, "failed" => ModelRequestOutcome.Failed, "cancelled" => ModelRequestOutcome.Cancelled, _ => throw new JsonException() },
                    Text("requestID"), Number("modelLoadMs"), Number("firstOutputMs"), Number("usableResultMs"), Number("displayMs")));
                string? Text(string key) => fields.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
                long? Number(string key) => fields.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null ? FoundationJsonInteger.Read(value) : null;
            }
            return Array.AsReadOnly(entries.ToArray());
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException or KeyNotFoundException) { return null; }
    }
}
