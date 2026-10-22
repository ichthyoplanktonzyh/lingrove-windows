using Cotabby.Core.LanguageLearning;

namespace Cotabby.Inference.LanguageLearning;

/// <summary>Fixed, non-sensitive failures produced by the optional learning endpoint.</summary>
public enum LearningTranslationEndpointFailure
{
    NotConfigured,
    EndpointUnavailable,
    CredentialUnavailable,
    InvalidRequest,
    ModelMismatch,
    RedirectRejected,
    HttpFailure,
    InvalidResponse,
    IncompleteResponse,
    ResponseTooLarge,
    TimedOut,
}

/// <summary>
/// A safe endpoint error for the translation preview. Messages never include the prompt, response
/// body, credential, endpoint-provided error text, or transport exception details.
/// </summary>
public sealed class LearningTranslationEndpointException : LearningTranslationException
{
    public LearningTranslationEndpointException(
        LearningTranslationEndpointFailure failure,
        int? statusCode = null)
        : base(ToCoreFailure(failure))
    {
        EndpointFailure = failure;
        StatusCode = statusCode;
    }

    public LearningTranslationEndpointFailure EndpointFailure { get; }

    /// <summary>The HTTP status, when present; endpoint response text is never retained.</summary>
    public int? StatusCode { get; }

    private static LearningTranslationFailure ToCoreFailure(LearningTranslationEndpointFailure failure) =>
        failure == LearningTranslationEndpointFailure.NotConfigured
            ? LearningTranslationFailure.ProviderNotConfigured
            : LearningTranslationFailure.ProviderFailure;
}
