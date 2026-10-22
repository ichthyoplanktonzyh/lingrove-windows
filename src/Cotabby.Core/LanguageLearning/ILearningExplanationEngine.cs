namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// One request's explanation provider, borrowed by the future app-owned explanation service.
/// Only complete validated answers leave this boundary; provisional tokens never become a card
/// or saved record. A facade owns its cancellation/lease, while the app owns a separate reusable
/// runtime for this role. Disposing a facade must not dispose translation/autocomplete runtimes.
/// </summary>
public interface ILearningExplanationEngine : IAsyncDisposable
{
    /// <summary>The configured output value is normalized to the Mac 256–4,096 setting before planning.</summary>
    Task<LearningExplanationResult> GenerateAsync(LearningExplanationInput input, LearningLanguage responseLanguage,
        int maximumOutputTokens, CancellationToken ct);
}
