using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Models;

/// <summary>Frozen first-use choices over the existing task catalogs. No transfer or setting
/// mutation occurs here; the app owner applies the plan only on explicit user action.</summary>
public static class RecommendedSetupPolicy
{
    public sealed record Plan(CotabbyModel Completion, LearningDownloadableModel Translation, LearningDownloadableModel Explanation);
    public static Plan Create(ulong physicalMemoryBytes)
    {
        var id = physicalMemoryBytes < 16UL * 1_073_741_824 ? "tabby-2-nano" : "tabby-2-base";
        return new(ModelCatalog.All.Single(model => model.Id == id), LearningRuntimeCatalog.TranslationModels[0],
            LearningRuntimeCatalog.ExplanationModels.Single(model => model.Filename == "Qwen3.5-4B-Q4_K_M.gguf"));
    }
    public static bool NeedsLearningSelection(bool local, string? selected, bool powerSwitching) =>
        local && string.IsNullOrWhiteSpace(selected) && !powerSwitching;
    public static bool CanFinishCompletion(string? expected, string? current, string target, bool local, bool powerSwitching) =>
        local && !powerSwitching && (string.Equals(current, expected, StringComparison.Ordinal) || string.Equals(current, target, StringComparison.Ordinal));
}
