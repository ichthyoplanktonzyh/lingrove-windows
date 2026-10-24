using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>The original Codable profile-map boundary. One invalid profile discards the map;
/// missing required sampler fields do not become newly invented defaults. Unknown fields stay
/// forward-compatible. The settings owner commits a detached map only after encoding succeeds.</summary>
public static class SpeechProfileCodec
{
    private static readonly JsonSerializerOptions Options = new()
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 512 };
    public static byte[] Encode(IReadOnlyDictionary<string, SpeechGenerationProfile> profiles) => JsonSerializer.SerializeToUtf8Bytes(profiles, Options);
    public static IReadOnlyDictionary<string, SpeechGenerationProfile>? Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            // Codable inspects known strings lazily: malformed UTF-8 in an ignored value does
            // not invalidate an otherwise usable profile. Known keys/strings are decoded below;
            // UTF-16/32 transcoding still validates its scalar input before JSON parsing.
            using var document = JsonDocument.Parse(FoundationJsonInput.Utf8Input(bytes.ToArray(), validateUtf8: false), new JsonDocumentOptions { MaxDepth = 512 });
            var result = new Dictionary<string, SpeechGenerationProfile>(CanonicalStringComparer.Instance);
            foreach (var property in First(document.RootElement))
            {
                var fields = First(property.Value); var sampling = First(fields["sampling"]);
                // Synthesized Swift Codable requires stored nonoptional fields even if the
                // declaration has a default. Optional members alone accept missing/null keys.
                var value = new LearningModelSamplingOverrides(Number(sampling, "temperature"), Number(sampling, "topP"),
                    Integer(sampling, "topK"), Number(sampling, "repetitionPenalty"), sampling["seedMode"].GetString() switch
                    {
                        "recommended" => LearningModelSeedMode.Recommended, "random" => LearningModelSeedMode.Random,
                        "fixed" => LearningModelSeedMode.Fixed, _ => throw new JsonException(),
                    }, checked((uint)FoundationJsonInteger.Read(sampling["fixedSeed"])));
                result.TryAdd(property.Key, new() { Sampling = value, ReferenceAudioPath = Text(fields, "referenceAudioPath"), EndpointVoice = Text(fields, "endpointVoice") });
            }
            return new ReadOnlyDictionary<string, SpeechGenerationProfile>(result);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException) { return null; }
    }
    private static Dictionary<string, JsonElement> First(JsonElement value)
    {
        var result = new Dictionary<string, JsonElement>(CanonicalStringComparer.Instance);
        foreach (var property in value.EnumerateObject()) result.TryAdd(property.Name, property.Value);
        return result;
    }
    private static double? Number(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        double result = v.GetDouble(); return double.IsFinite(result) ? result : throw new JsonException();
    }
    private static long? Integer(Dictionary<string, JsonElement> values, string key) => values.TryGetValue(key, out var v) && v.ValueKind != JsonValueKind.Null ? FoundationJsonInteger.Read(v) : null;
    private static string? Text(Dictionary<string, JsonElement> values, string key) => values.TryGetValue(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;
}
