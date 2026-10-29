using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.App.Settings;

/// <summary>Per-role stored selections and transient power evidence. No credentials or runtime ownership.</summary>
public sealed partial class AppSettings
{
    public bool LearningTranslationPowerSwitching { get; set; }
    public LearningPowerProfile? LearningTranslationBatteryProfile { get; set; }
    public LearningPowerProfile? LearningTranslationPluggedInProfile { get; set; }
    public bool LearningExplanationPowerSwitching { get; set; }
    public LearningPowerProfile? LearningExplanationBatteryProfile { get; set; }
    public LearningPowerProfile? LearningExplanationPluggedInProfile { get; set; }
    [JsonIgnore] public bool LearningPowerPluggedIn { get; set; } = true;
    public Dictionary<string, int> LearningTranslationEndpointContextByDestination { get; set; } = new(CanonicalStringComparer.Instance);
    public Dictionary<string, LearningGenerationProfile> LearningTranslationEndpointGenerationByDestination { get; set; } = new(CanonicalStringComparer.Instance);
    [JsonIgnore] public string LearningTranslationEndpointIdentity => ModelDestinationIdentity.Create("endpoint",
        LearningTranslationActiveSelection.EndpointModel, LearningEndpointBaseUrl ?? "");
    [JsonIgnore] public int LearningTranslationActiveEndpointContext =>
        FindTranslationEndpointValue(LearningTranslationEndpointContextByDestination) is { } value
            ? Math.Clamp(value, 1024, ModelContextBudget.MaximumEndpointTokens)
            : TranslationEndpointIsManual ? Math.Clamp(LearningContextTokens, 2048, ModelContextBudget.MaximumEndpointTokens) : 8192;
    [JsonIgnore] public LearningGenerationProfile LearningTranslationActiveEndpointGeneration =>
        FindTranslationEndpointValue(LearningTranslationEndpointGenerationByDestination) ?? new()
        { Style = TranslationEndpointIsManual ? new LearningTranslationStyle(LearningStyleRegister, LearningCustomInstruction ?? "") : new() };
    [JsonIgnore] private bool TranslationEndpointIsManual => LearningTranslationManualSelection.Backend == LearningModelBackend.Endpoint &&
        CanonicalStringComparer.Instance.Equals(LearningTranslationManualSelection.EndpointModel, LearningTranslationActiveSelection.EndpointModel);
    private T? FindTranslationEndpointValue<T>(Dictionary<string, T>? values) where T : class =>
        values?.FirstOrDefault(pair => CanonicalStringComparer.Instance.Equals(pair.Key, LearningTranslationEndpointIdentity)).Value;
    private int? FindTranslationEndpointValue(Dictionary<string, int>? values)
    {
        if (values is null) return null;
        foreach (var pair in values) if (CanonicalStringComparer.Instance.Equals(pair.Key, LearningTranslationEndpointIdentity)) return pair.Value;
        return null;
    }

    [JsonIgnore] public LearningPowerProfile LearningTranslationManualSelection => EffectiveLearningBackend == LearningTranslationBackend.Endpoint
        ? LearningPowerProfile.Endpoint(LearningEndpointModel ?? "") : LearningPowerProfile.Local(LearningLocalModel ?? "", LearningLocalTemplate);
    [JsonIgnore] public LearningPowerProfile LearningExplanationManualSelection => LearningExplanationBackend == LearningModelBackend.Endpoint
        ? LearningPowerProfile.Endpoint(LearningExplanationEndpointModel ?? "") : LearningPowerProfile.Local(LearningExplanationLocalModel ?? "", LearningExplanationLocalTemplate);
    [JsonIgnore] public LearningPowerProfile LearningTranslationActiveSelection => LearningPowerPolicy.Resolve(LearningTranslationPowerSwitching,
        LearningPowerPluggedIn, LearningTranslationManualSelection, LearningTranslationBatteryProfile, LearningTranslationPluggedInProfile);
    [JsonIgnore] public LearningPowerProfile LearningExplanationActiveSelection => LearningPowerPolicy.Resolve(LearningExplanationPowerSwitching,
        LearningPowerPluggedIn, LearningExplanationManualSelection, LearningExplanationBatteryProfile, LearningExplanationPluggedInProfile);

    public void SetLearningPowerSwitching(LearningModelRole role, bool enabled)
    {
        if (role == LearningModelRole.Translation)
        {
            if (enabled && LearningTranslationBatteryProfile is null)
                LearningTranslationBatteryProfile = LearningTranslationPluggedInProfile = LearningTranslationManualSelection;
            LearningTranslationPowerSwitching = enabled;
        }
        else
        {
            if (enabled && LearningExplanationBatteryProfile is null)
                LearningExplanationBatteryProfile = LearningExplanationPluggedInProfile = LearningExplanationManualSelection;
            LearningExplanationPowerSwitching = enabled;
        }
    }
}
