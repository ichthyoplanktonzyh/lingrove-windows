using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Settings;

/// <summary>
/// The local explanation role's durable preferences and effective projection. The app settings
/// store owns persistence; its controller borrows a detached snapshot. No translation/completion
/// selection, endpoint consent or credential is inherited into this role.
/// </summary>
public sealed partial class AppSettings
{
    public string LearningExplanationLocalModel { get; set; } = "";
    [JsonConverter(typeof(LearningChatTemplateJsonConverter))]
    public LearningChatTemplate LearningExplanationLocalTemplate { get; set; } = LearningChatTemplate.Automatic;
    public Dictionary<string, int> LearningExplanationLocalContextTokensByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LearningGenerationProfile> LearningExplanationLocalGenerationProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public LearningLanguage? LearningExplanationLanguage { get; set; }

    // Mac follows the app's preferred localization when unset. The current Windows UI is English;
    // do not silently use the translation target or the machine locale as the explanation language.
    [JsonIgnore]
    public LearningLanguage ResolvedLearningExplanationLanguage => LearningExplanationLanguage is { } language && Enum.IsDefined(language)
        ? language : LearningLanguage.English;

    public int EffectiveLearningExplanationContextTokens(string model) =>
        !string.IsNullOrEmpty(model) && LearningExplanationLocalContextTokensByModel is not null
            && LearningExplanationLocalContextTokensByModel.TryGetValue(model, out int tokens)
                ? Math.Clamp(tokens, 1_024, ModelContextBudget.MaximumLocalTokens) : 8_192;

    public LearningGenerationProfile EffectiveLearningExplanationGenerationProfile(string model)
    {
        var saved = !string.IsNullOrEmpty(model) && LearningExplanationLocalGenerationProfiles is not null
            && LearningExplanationLocalGenerationProfiles.TryGetValue(model, out var profile) ? profile : null;
        return new()
        {
            // Explanation has no translation style. Keep its independent sampler/output controls
            // detached so editing a different model cannot mutate an already-admitted request.
            Sampling = (saved?.Sampling ?? new()).Normalized,
            MaximumOutputTokens = saved?.MaximumOutputTokens is { } maximum
                ? LearningExplanationModelPolicy.ResolveOutputTokens(maximum) : null,
        };
    }

    public LearningModelSnapshot LearningExplanationSnapshot()
    {
        var model = LearningExplanationLocalModel ?? "";
        var generation = EffectiveLearningExplanationGenerationProfile(model);
        return new LearningModelSnapshot
        {
            LocalModel = model,
            Template = Enum.IsDefined(LearningExplanationLocalTemplate) ? LearningExplanationLocalTemplate : LearningChatTemplate.Automatic,
            ContextWindowTokens = EffectiveLearningExplanationContextTokens(model),
            SamplingOverrides = generation.Sampling,
            MaximumOutputTokens = generation.MaximumOutputTokens,
        }.ForExplanation();
    }
}
