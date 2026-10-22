using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Optional sampler changes for one model destination. Null values preserve that model family's
/// recommendations; normalization is repeated when the profile is resolved so persisted or UI
/// values cannot pass non-finite numbers or invalid native sampler arguments.
/// </summary>
public sealed record LearningModelSamplingOverrides(
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("topP")] double? TopP = null,
    [property: JsonPropertyName("topK")] int? TopK = null,
    [property: JsonPropertyName("repetitionPenalty")] double? RepetitionPenalty = null,
    [property: JsonPropertyName("seedMode")] LearningModelSeedMode SeedMode = LearningModelSeedMode.Recommended,
    [property: JsonPropertyName("fixedSeed")] uint FixedSeed = 42)
{
    [JsonIgnore]
    public bool IsRecommended => Temperature is null && TopP is null && TopK is null &&
        RepetitionPenalty is null && SeedMode == LearningModelSeedMode.Recommended;

    [JsonIgnore]
    public LearningModelSamplingOverrides Normalized => new(
        Bound(Temperature, 0, 2),
        Bound(TopP, 0.01, 1),
        TopK is null ? null : Math.Clamp(TopK.Value, 1, 256),
        Bound(RepetitionPenalty, 1, 2),
        Enum.IsDefined(SeedMode) ? SeedMode : LearningModelSeedMode.Recommended,
        Math.Min(FixedSeed, uint.MaxValue - 1));

    public uint ResolveSeed(uint recommended) => Normalized.SeedMode switch
    {
        LearningModelSeedMode.Recommended => recommended,
        LearningModelSeedMode.Random => uint.MaxValue,
        LearningModelSeedMode.Fixed => Normalized.FixedSeed,
        _ => recommended,
    };

    /// <summary>Endpoints receive the temperature/top-p controls supported by the shared transport.</summary>
    [JsonIgnore]
    public LearningModelSamplingOverrides EndpointOnly
    {
        get
        {
            var normalized = Normalized;
            return new LearningModelSamplingOverrides(normalized.Temperature, normalized.TopP);
        }
    }

    private static double? Bound(double? value, double minimum, double maximum) =>
        value is { } number && double.IsFinite(number)
            ? Math.Clamp(number, minimum, maximum)
            : null;
}

[JsonConverter(typeof(LearningModelSeedModeJsonConverter))]
public enum LearningModelSeedMode
{
    Recommended,
    Random,
    Fixed,
}

public sealed class LearningModelSeedModeJsonConverter : JsonConverter<LearningModelSeedMode>
{
    public override LearningModelSeedMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "recommended" => LearningModelSeedMode.Recommended,
            "random" => LearningModelSeedMode.Random,
            "fixed" => LearningModelSeedMode.Fixed,
            _ => throw new JsonException("The learning model seed mode is not supported."),
        };

    public override void Write(Utf8JsonWriter writer, LearningModelSeedMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            LearningModelSeedMode.Recommended => "recommended",
            LearningModelSeedMode.Random => "random",
            LearningModelSeedMode.Fixed => "fixed",
            _ => throw new JsonException("The learning model seed mode is not supported."),
        });
}

/// <summary>The fully resolved sampler for one operation, distinct from resident model identity.</summary>
public sealed record LearningModelSamplingValues(
    double Temperature,
    double TopP,
    int TopK,
    double RepetitionPenalty,
    uint Seed = uint.MaxValue)
{
    public LearningModelSamplingValues Applying(LearningModelSamplingOverrides? overrides)
    {
        if (overrides is null) return this;
        var normalized = overrides.Normalized;
        return new LearningModelSamplingValues(
            normalized.Temperature ?? Temperature,
            normalized.TopP ?? TopP,
            normalized.TopK ?? TopK,
            normalized.RepetitionPenalty ?? RepetitionPenalty,
            normalized.ResolveSeed(Seed));
    }

    public static LearningModelSamplingValues FromInstruction(LearningInstructionSampling sampling) =>
        new(sampling.Temperature, sampling.TopP, sampling.TopK, sampling.RepetitionPenalty);
}

/// <summary>
/// A destination's durable expression and sampler preferences. The settings owner keys this value
/// by destination identity; a request freezes an effective copy before asynchronous generation.
/// </summary>
public sealed record LearningGenerationProfile
{
    [JsonPropertyName("style")]
    public LearningTranslationStyle Style { get; init; } = new();

    [JsonPropertyName("sampling")]
    public LearningModelSamplingOverrides Sampling { get; init; } = new();

    [JsonPropertyName("maximumOutputTokens")]
    public int? MaximumOutputTokens { get; init; }
}

/// <summary>
/// A short-lived local runtime configuration. Model path, requested template, and context capacity
/// remain separate facts: prompt family comes from the resolved template/metadata, never the path.
/// The native adapter owns file access and GPU implementation details.
/// </summary>
public sealed record LearningLocalModelConfiguration
{
    public LearningLocalModelConfiguration(
        string modelPath,
        LearningChatTemplate requestedTemplate = LearningChatTemplate.Automatic,
        int contextWindowTokens = 8_192,
        LearningModelSamplingOverrides? samplingOverrides = null,
        int gpuLayerCount = 0)
    {
        if (modelPath is null || !Enum.IsDefined(requestedTemplate))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        if (contextWindowTokens is < 1_024 or > ModelContextBudget.MaximumLocalTokens)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidContextBudget);
        if (gpuLayerCount < 0)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);

        ModelPath = modelPath;
        RequestedTemplate = requestedTemplate;
        ContextWindowTokens = contextWindowTokens;
        SamplingOverrides = samplingOverrides?.Normalized;
        GpuLayerCount = gpuLayerCount;
    }

    public string ModelPath { get; }
    public LearningChatTemplate RequestedTemplate { get; }
    public int ContextWindowTokens { get; }
    public LearningModelSamplingOverrides? SamplingOverrides { get; }
    /// <summary>Zero selects CPU; positive values are native adapter hints capped by the model's layer count.</summary>
    public int GpuLayerCount { get; }
}

/// <summary>
/// The immutable result of resolving a local selection before planning or generation. It carries
/// model identity/path separately from the template/family so a renamed GGUF retains its real task.
/// </summary>
public sealed record LearningLocalModelProfile(
    string ModelPath,
    LearningChatTemplate Template,
    LearningTranslationPrompt.Family Family,
    int ContextWindowTokens,
    LearningInstructionSampling RecommendedSampling,
    LearningModelSamplingOverrides? SamplingOverrides)
{
    public LearningModelSamplingValues Sampling =>
        LearningModelSamplingValues.FromInstruction(RecommendedSampling).Applying(SamplingOverrides);
}
