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

    /// <summary>Local exact planning retains the winning token payload while draft-reference
    /// binary search probes other candidates. No losing token array can be used for generation.</summary>
    public static (LearningTranslationRequest Request, T Payload) CreatePlanned<T>(string requestId, string source,
        LearningTranslationOptions options, LearningTranslationPrompt.Family family, int actualContextWindowTokens,
        Func<string, (int Count, T Payload)> measure) => CreateCorePlanned(requestId, source, options, family,
            preserveOptions: true, actualContextWindowTokens, estimated: false, measure);

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
        count ??= ModelContextBudget.EstimatedTokens;
        return CreateCorePlanned(requestId, source, options, family, preserveOptions, actualContextWindowTokens,
            estimated, text => (count(text), false)).Request;
    }

    private static (LearningTranslationRequest Request, T Payload) CreateCorePlanned<T>(
        string requestId, string source, LearningTranslationOptions options, LearningTranslationPrompt.Family? family,
        bool preserveOptions, int? actualContextWindowTokens, bool estimated, Func<string, (int Count, T Payload)> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        if (string.IsNullOrWhiteSpace(requestId))
            throw new LearningTranslationException(LearningTranslationFailure.InvalidRequestId);
        if (source is null)
            throw new LearningTranslationException(LearningTranslationFailure.InvalidSourceRange);
        if (options is null || options.ContextTokens < 1 ||
            options.ContextTokens > ModelContextBudget.MaximumEndpointTokens)
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidContextBudget);
        }
        if (options.ModelName is null || options.Style is null || options.ReferenceBefore is null || options.ReferenceAfter is null ||
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
        bool bareMeasurement = true;
        (int Count, T Payload) Measure(string promptText)
        {
            var measured = measure(promptText);
            if (!estimated)
            {
                if (measured.Count <= 0)
                    throw ModelContextBudget.OversizedTarget();
                // Preserve the historical bare-target specialist failure. Oversized references
                // are measured normally so the 3b planner can shrink them under the same cap.
                if (bareMeasurement && promptFamily == LearningTranslationPrompt.Family.TranslateGemma && measured.Count > 2_048)
                    throw new LearningTranslationException(LearningTranslationFailure.TranslateGemmaInputTooLarge);
            }
            bareMeasurement = false;
            return measured;
        }
        var input = new LearningTranslationInput(source, frozenOptions.Style)
        { ReferenceBefore = frozenOptions.ReferenceBefore, ReferenceAfter = frozenOptions.ReferenceAfter };
        var plan = LearningTranslationBudget.PlanPrompt(
            input,
            frozenOptions.SourceLanguage,
            frozenOptions.TargetLanguage,
            promptFamily,
            total: planningContextTokens,
            output: outputTokens,
            estimated: estimated,
            measure: Measure);
        var prompt = plan.Request;

        if (family is null)
        {
            var sampling = (frozenOptions.EndpointSampling ?? new()).EndpointOnly;
            prompt = prompt with { Sampling = prompt.Sampling with
            { Temperature = sampling.Temperature ?? prompt.Sampling.Temperature, TopP = sampling.TopP ?? prompt.Sampling.TopP } };
        }

        return (new LearningTranslationRequest(requestId, source,
            preserveOptions ? options : frozenOptions, prompt, outputTokens), plan.Payload);
    }
}
