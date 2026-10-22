using Cotabby.Core.Support;
using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Translation task limits shared by prompt planning and request creation. The policy has no model
/// or settings state; a request freezes the chosen context size before generation begins.
/// </summary>
public static class ModelContextBudget
{
    public const int MaximumLocalTokens = 32_768;
    public const int MaximumEndpointTokens = 131_072;
    public const int MaximumTextBytes = 262_144;
    public const int MaximumTranslationOutputTokens = 32_768;

    /// <summary>
    /// Conservative endpoint estimate. It takes the larger of the shared word-aware estimate and
    /// half the UTF-8 bytes, with the latter protecting CJK and other dense scripts.
    /// </summary>
    public static int EstimatedTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Math.Max(TokenCountEstimator.Estimate(text), (Encoding.UTF8.GetByteCount(text) + 1) / 2);
    }

    /// <summary>Context left for prompt input after a complete output reservation and framing margin.</summary>
    public static int InputAllowance(int total, int output, bool estimated) =>
        Math.Max(0, total - output - (estimated ? Math.Max(128, total / 10) : 32));

    /// <summary>
    /// Reserves output for an entire translation. A target whose complete output reserve cannot fit
    /// is rejected before generation rather than clipped to a token or byte budget.
    /// </summary>
    public static int TranslationOutputTokens(string source, int total)
    {
        ArgumentNullException.ThrowIfNull(source);
        LearningTextScopePolicy.ValidateCompleteText(source);

        var desired = Math.Max(Math.Min(2_048, Math.Max(256, total / 3)), EstimatedTokens(source) * 2 + 64);
        if (desired >= total || desired > MaximumTranslationOutputTokens) throw OversizedTarget();
        return desired;
    }

    public static LearningTranslationException OversizedTarget() =>
        new(LearningTranslationFailure.OversizedTarget);
}
