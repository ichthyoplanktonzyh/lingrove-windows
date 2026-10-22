namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Turns one complete captured target and immutable settings into an engine-ready request. The
/// factory resolves model family, reserves output, checks the complete prompt against its allowance,
/// and returns only values that can safely outlive the UI capture.
/// </summary>
public static class LearningTranslationRequestFactory
{
    public static LearningTranslationRequest Create(
        string requestId,
        string source,
        LearningTranslationOptions options)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidRequestId);
        if (source is null)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidSourceRange);
        if (options is null || options.ContextTokens < 1 ||
            options.ContextTokens > ModelContextBudget.MaximumEndpointTokens)
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidContextBudget);
        }
        if (options.ModelName is null || options.Style is null ||
            !Enum.IsDefined(options.SourceLanguage) || !Enum.IsDefined(options.TargetLanguage) ||
            !Enum.IsDefined(options.Style.Register))
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        }
        if (options.SourceLanguage == options.TargetLanguage)
            throw new LearningTranslationException(LearningTranslationFailure.SameLanguage);

        LearningTextScopePolicy.ValidateSource(source);
        var frozenOptions = options with { Style = options.Style.Effective };
        var outputTokens = ModelContextBudget.TranslationOutputTokens(source, frozenOptions.ContextTokens);
        var family = LearningTranslationPrompt.ForEndpointModel(frozenOptions.ModelName);
        var input = new LearningTranslationInput(source, frozenOptions.Style);
        var prompt = LearningTranslationBudget.Prompt(
            input,
            frozenOptions.SourceLanguage,
            frozenOptions.TargetLanguage,
            family,
            total: frozenOptions.ContextTokens,
            output: outputTokens,
            estimated: true);

        return new LearningTranslationRequest(requestId, source, frozenOptions, prompt, outputTokens);
    }
}
