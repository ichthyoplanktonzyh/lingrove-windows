using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.App.Settings;

/// <summary>Explanation owns a separate, explicit destination and per-destination parameters.
/// Unknown backend values remain durable but cannot enable transport. Credential revision is
/// request identity only; the key stays in its independent credential adapter.</summary>
public sealed partial class AppSettings
{
    [JsonPropertyName("learning.explanation.backend")] public JsonElement? LearningExplanationBackendValue { get; set; }
    [JsonIgnore] public LearningModelBackend LearningExplanationBackend
    {
        get => LearningExplanationBackendValue is { ValueKind: JsonValueKind.String } raw && raw.GetString() == "endpoint"
            ? LearningModelBackend.Endpoint : LearningModelBackend.Local;
        set => LearningExplanationBackendValue = JsonSerializer.SerializeToElement(value == LearningModelBackend.Endpoint ? "endpoint" : "local");
    }
    public string LearningExplanationEndpointBaseUrl { get; set; } = OpenAICompatibleEndpointConfiguration.DefaultBaseUrl;
    public string LearningExplanationEndpointModel { get; set; } = "";
    public Dictionary<string, int> LearningExplanationEndpointContextTokensByDestination { get; set; } = new(CanonicalStringComparer.Instance);
    public Dictionary<string, LearningGenerationProfile> LearningExplanationEndpointGenerationProfiles { get; set; } = new(CanonicalStringComparer.Instance);
    [JsonIgnore] internal int LearningExplanationCredentialRevision { get; set; }
    [JsonIgnore] public string LearningExplanationEndpointIdentity => ModelDestinationIdentity.Create("endpoint",
        LearningExplanationActiveSelection.EndpointModel, LearningExplanationEndpointBaseUrl ?? "");

    internal void NormalizeExplanationEndpoints()
    {
        LearningExplanationEndpointBaseUrl ??= OpenAICompatibleEndpointConfiguration.DefaultBaseUrl;
        LearningExplanationEndpointModel ??= "";
        LearningExplanationEndpointContextTokensByDestination = ExplanationDestinationMap(LearningExplanationEndpointContextTokensByDestination);
        LearningExplanationEndpointGenerationProfiles = ExplanationDestinationMap(LearningExplanationEndpointGenerationProfiles);
        LearningTranslationEndpointContextByDestination = ExplanationDestinationMap(LearningTranslationEndpointContextByDestination);
        LearningTranslationEndpointGenerationByDestination = ExplanationDestinationMap(LearningTranslationEndpointGenerationByDestination);
    }
    private static Dictionary<string, T> ExplanationDestinationMap<T>(Dictionary<string, T>? values)
    {
        var result = new Dictionary<string, T>(CanonicalStringComparer.Instance);
        foreach (var pair in values ?? []) result.TryAdd(pair.Key, pair.Value);
        return result;
    }
    public int EffectiveLearningExplanationEndpointContextTokens =>
        LearningExplanationEndpointContextTokensByDestination is { } values && values.TryGetValue(LearningExplanationEndpointIdentity, out int tokens)
            ? Math.Clamp(tokens, 1024, ModelContextBudget.MaximumEndpointTokens) : 8192;
    public LearningGenerationProfile EffectiveLearningExplanationEndpointGenerationProfile
    {
        get
        {
            var saved = LearningExplanationEndpointGenerationProfiles?.GetValueOrDefault(LearningExplanationEndpointIdentity);
            return new() { Sampling = (saved?.Sampling ?? new()).EndpointOnly,
                MaximumOutputTokens = saved?.MaximumOutputTokens is { } output ? LearningExplanationModelPolicy.ResolveOutputTokens(output) : null };
        }
    }
    private LearningModelSnapshot LearningExplanationEndpointSnapshot(string model)
    {
        var generation = EffectiveLearningExplanationEndpointGenerationProfile;
        return new LearningModelSnapshot { Backend = LearningModelBackend.Endpoint,
            BaseUrl = FoundationText.TrimWhitespaceAndNewlines(LearningExplanationEndpointBaseUrl ?? ""),
            EndpointModel = FoundationText.TrimWhitespaceAndNewlines(model),
            CredentialRevision = LearningExplanationCredentialRevision, ContextWindowTokens = EffectiveLearningExplanationEndpointContextTokens,
            SamplingOverrides = generation.Sampling, MaximumOutputTokens = generation.MaximumOutputTokens }.ForExplanation();
    }
}
