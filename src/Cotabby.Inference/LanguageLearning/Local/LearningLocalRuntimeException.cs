namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>Fixed native-runtime failure categories safe for the learning facade to map.</summary>
public enum LearningLocalRuntimeFailure
{
    /// <summary>The token plan, output reserve, or sampler profile is invalid.</summary>
    InvalidRequest,
    /// <summary>The complete prompt and positive output reserve do not fit the loaded context.</summary>
    PromptBudgetExceeded,
    ModelUnavailable,
    ModelLoadFailed,
    DecodeFailed,
    OutputLimitReached,
    InvalidUtf8,
    OutputTooLarge,
    RuntimeDisposed,
}

/// <summary>
/// A stable local-runtime error that never includes a prompt, source text, model output, or an
/// untrusted native error string. The facade maps it to the existing learning failure surface.
/// </summary>
public sealed class LearningLocalRuntimeException : Exception
{
    public LearningLocalRuntimeException(
        LearningLocalRuntimeFailure failure,
        Exception? innerException = null)
        : base(MessageFor(failure), innerException)
    {
        Failure = failure;
    }

    public LearningLocalRuntimeFailure Failure { get; }

    private static string MessageFor(LearningLocalRuntimeFailure failure) => failure switch
    {
        LearningLocalRuntimeFailure.InvalidRequest => "The local translation request is invalid.",
        LearningLocalRuntimeFailure.PromptBudgetExceeded => "The complete translation prompt and output reserve do not fit this model context.",
        LearningLocalRuntimeFailure.ModelUnavailable => "The selected local model is unavailable.",
        LearningLocalRuntimeFailure.ModelLoadFailed => "The selected local model could not be loaded.",
        LearningLocalRuntimeFailure.DecodeFailed => "The local model could not process this translation.",
        LearningLocalRuntimeFailure.OutputLimitReached => "The model reached its output limit before completing the translation.",
        LearningLocalRuntimeFailure.InvalidUtf8 => "The local model returned incomplete text encoding.",
        LearningLocalRuntimeFailure.OutputTooLarge => "The local model returned more text than the safe output limit.",
        LearningLocalRuntimeFailure.RuntimeDisposed => "The local translation runtime is stopping.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown local runtime failure."),
    };
}
