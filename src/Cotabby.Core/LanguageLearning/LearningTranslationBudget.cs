namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Fits a complete rendered target into a measured allowance. This current-sentence path has no
/// reference excerpts to crop: if the whole target and fixed prompt do not fit, it fails without
/// modifying the target.
/// </summary>
public static class LearningTranslationBudget
{
    public static LearningTranslationPrompt.Request Prompt(
        LearningTranslationInput input,
        LearningLanguage? sourceLanguage,
        LearningLanguage targetLanguage,
        LearningTranslationPrompt.Family family,
        int total,
        int output,
        bool estimated,
        Func<string, int>? count = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        count ??= ModelContextBudget.EstimatedTokens;
        var request = LearningTranslationPrompt.Render(input, sourceLanguage, targetLanguage, family);
        var allowance = Math.Min(
            LearningTranslationPrompt.MaximumInputTokens(family),
            ModelContextBudget.InputAllowance(total, output, estimated));
        if (count(request.Prompt) > allowance) throw ModelContextBudget.OversizedTarget();
        return request;
    }
}
