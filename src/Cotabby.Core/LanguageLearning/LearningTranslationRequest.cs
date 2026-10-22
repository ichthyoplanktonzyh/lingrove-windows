namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// A complete immutable translation handoff. It contains the literal target, a settings snapshot,
/// the already-rendered model-specific prompt, and a full output reservation; engines do not read
/// UI state or rebuild prompt policy.
/// </summary>
public sealed record LearningTranslationRequest(
    string RequestId,
    string Source,
    LearningTranslationOptions Options,
    LearningTranslationPrompt.Request Prompt,
    int MaxOutputTokens);

/// <summary>
/// Narrow engine boundary for one language-learning request. Implementations own their runtime or
/// endpoint transport and honor cancellation without borrowing autocomplete prompt/session state.
/// </summary>
public interface ILearningTranslationEngine
{
    /// <summary>Generates a complete translation for the frozen request, or throws on failure.</summary>
    Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct);

    /// <summary>
    /// Generates a complete translation and may report ordered text deltas for a provisional
    /// preview. Implementations must stop invoking the callback before the returned task completes.
    /// Existing engines remain compatible; their default implementation uses the complete-result path.
    /// </summary>
    Task<string> TranslateAsync(
        LearningTranslationRequest request,
        Action<string>? onDelta,
        CancellationToken ct) => TranslateAsync(request, ct);
}

/// <summary>
/// Optional asynchronous planning boundary for engines whose request profile depends on a
/// provider-owned model/template choice. Planning validates and freezes a complete request but does
/// not generate output; implementations leave the captured translation settings in
/// <see cref="LearningTranslationRequest.Options"/> and keep resolved runtime details in its prompt.
/// </summary>
/// <remarks>
/// The coordinator invokes this on its worker task with a request-owned token. Plans are checked
/// against the original request identity, source and immutable options before cache lookup or
/// generation, so asynchronous model resolution cannot relabel a result in the UI.
/// </remarks>
public interface ILearningTranslationRequestPlanner
{
    /// <summary>Builds a validated, model-specific request for one exact captured target.</summary>
    Task<LearningTranslationRequest> PlanAsync(
        string requestId,
        string source,
        LearningTranslationOptions options,
        CancellationToken ct);
}
