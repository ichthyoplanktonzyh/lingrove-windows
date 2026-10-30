using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Fits the complete target and optional nearest-neighbor draft reference. Only reference size
/// is searched; the winning measured payload stays paired with its exact rendered prompt.
/// </summary>
public static class LearningTranslationBudget
{
    public sealed record Plan<T>(LearningTranslationPrompt.Request Request, T Payload);
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
        return PlanPrompt(input, sourceLanguage, targetLanguage, family, total, output, estimated,
            text => (count(text), false)).Request;
    }

    public static Plan<T> PlanPrompt<T>(LearningTranslationInput input, LearningLanguage? sourceLanguage,
        LearningLanguage targetLanguage, LearningTranslationPrompt.Family family, int total, int output,
        bool estimated, Func<string, (int Count, T Payload)> measure)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(measure);
        if (input.ReferenceBefore is null || input.ReferenceAfter is null)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        var allowance = Math.Min(
            LearningTranslationPrompt.MaximumInputTokens(family),
            ModelContextBudget.InputAllowance(total, output, estimated));
        var bare = LearningTranslationPrompt.Render(input.Source, sourceLanguage, targetLanguage, family, input.Style);
        var bareMeasurement = measure(bare.Prompt);
        if (bareMeasurement.Count > allowance) throw ModelContextBudget.OversizedTarget();
        var best = new Plan<T>(bare, bareMeasurement.Payload);
        if (input.ReferenceBefore.Length == 0 && input.ReferenceAfter.Length == 0) return best;
        LearningTranslationPrompt.ValidateReferenceText(input.ReferenceBefore);
        LearningTranslationPrompt.ValidateReferenceText(input.ReferenceAfter);
        var before = new GraphemeString(input.ReferenceBefore); var after = new GraphemeString(input.ReferenceAfter);
        int low = 0, high = (int)Math.Min(131_072L, (long)before.Count + after.Count);
        while (low <= high)
        {
            int length = low + (high - low) / 2;
            int beforeCount = Math.Min(before.Count, (length * 3) / 4);
            int afterCount = Math.Min(after.Count, length - beforeCount);
            int extraBefore = Math.Min(before.Count - beforeCount, length - beforeCount - afterCount);
            var candidate = input with { ReferenceBefore = before.Suffix(beforeCount + extraBefore), ReferenceAfter = after.Prefix(afterCount) };
            var request = LearningTranslationPrompt.Render(candidate, sourceLanguage, targetLanguage, family);
            var measured = measure(request.Prompt);
            if (measured.Count <= allowance) { best = new(request, measured.Payload); low = length + 1; }
            else high = length - 1;
        }
        return best;
    }
}
