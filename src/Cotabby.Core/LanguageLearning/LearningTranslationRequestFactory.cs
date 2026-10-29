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
        => CreateCore(requestId, source, options, family: null, preserveOptions: false,
            actualContextWindowTokens: null, estimated: true, count: null);

    /// <summary>
    /// Builds a request for an already resolved local prompt family. This prevents a local model's
    /// behavior from being inferred from its filename; endpoint callers keep using the alias-based overload.
    /// </summary>
    public static LearningTranslationRequest Create(
        string requestId,
        string source,
        LearningTranslationOptions options,
        LearningTranslationPrompt.Family family)
        => CreateCore(requestId, source, options, family, preserveOptions: true,
            actualContextWindowTokens: null, estimated: true, count: null);

    /// <summary>
    /// Builds a local request with the native model's exact token count. The callback must count
    /// template framing and literal prompt text using their distinct tokenization paths; endpoints
    /// continue to use the conservative estimator overload above.
    /// </summary>
    public static LearningTranslationRequest Create(
        string requestId,
        string source,
        LearningTranslationOptions options,
        LearningTranslationPrompt.Family family,
        Func<string, int> exactTokenCount)
    {
        ArgumentNullException.ThrowIfNull(exactTokenCount);
        return CreateCore(requestId, source, options, family, preserveOptions: true,
            actualContextWindowTokens: null, estimated: false, count: exactTokenCount);
    }

    /// <summary>
    /// Uses the native model's observed capacity when planning exact local tokens. The reserve is
    /// still derived from configured settings, matching the Mac runtime's configured-output policy.
    /// </summary>
    public static LearningTranslationRequest Create(
        string requestId,
        string source,
        LearningTranslationOptions options,
        LearningTranslationPrompt.Family family,
        int actualContextWindowTokens,
        Func<string, int> exactTokenCount)
    {
        ArgumentNullException.ThrowIfNull(exactTokenCount);
        return CreateCore(requestId, source, options, family, preserveOptions: true,
            actualContextWindowTokens: actualContextWindowTokens, estimated: false, count: exactTokenCount);
    }

    private static LearningTranslationRequest CreateCore(
        string requestId,
        string source,
        LearningTranslationOptions options,
        LearningTranslationPrompt.Family? family,
        bool preserveOptions,
        int? actualContextWindowTokens,
        bool estimated,
        Func<string, int>? count)
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
            (options.SourceLanguage is { } sourceLanguage && !Enum.IsDefined(sourceLanguage)) || !Enum.IsDefined(options.TargetLanguage) ||
            !Enum.IsDefined(options.Style.Register))
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        }
        if (actualContextWindowTokens is { } actualContext &&
            (actualContext < 1 || actualContext > ModelContextBudget.MaximumLocalTokens))
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidContextBudget);
        }
        if (options.SourceLanguage == options.TargetLanguage)
            throw new LearningTranslationException(LearningTranslationFailure.SameLanguage);

        LearningTextScopePolicy.ValidateSource(source);
        var frozenOptions = options with { Style = options.Style.Effective };
        var outputTokens = ModelContextBudget.TranslationOutputTokens(source, frozenOptions.ContextTokens);
        var planningContextTokens = actualContextWindowTokens is { } actual
            ? Math.Min(frozenOptions.ContextTokens, actual)
            : frozenOptions.ContextTokens;
        var promptFamily = family ?? LearningTranslationPrompt.ForEndpointModel(frozenOptions.ModelName);
        var measurement = count;
        if (!estimated && measurement is { } exactMeasurement)
        {
            measurement = promptText =>
            {
                var inputTokens = exactMeasurement(promptText);
                if (inputTokens <= 0)
                    throw ModelContextBudget.OversizedTarget();
                if (promptFamily == LearningTranslationPrompt.Family.TranslateGemma && inputTokens > 2_048)
                    throw new LearningTranslationException(LearningTranslationFailure.TranslateGemmaInputTooLarge);
                return inputTokens;
            };
        }
        var input = new LearningTranslationInput(source, frozenOptions.Style);
        var prompt = LearningTranslationBudget.Prompt(
            input,
            frozenOptions.SourceLanguage,
            frozenOptions.TargetLanguage,
            promptFamily,
            total: planningContextTokens,
            output: outputTokens,
            estimated: estimated,
            count: measurement);

        return new LearningTranslationRequest(requestId, source,
            preserveOptions ? options : frozenOptions, prompt, outputTokens);
    }
}
