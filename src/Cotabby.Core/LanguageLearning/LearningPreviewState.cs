namespace Cotabby.Core.LanguageLearning;

/// <summary>The immutable lifecycle status for one readonly sentence-translation preview.</summary>
public enum LearningPreviewStatus
{
    Idle,
    Loading,
    Ready,
    Failed,
    Cancelled,
}

/// <summary>
/// A presentation snapshot owned by <see cref="LearningPreviewCoordinator"/>. It keeps the exact
/// source and translation direction together so a delayed result cannot be relabeled after settings
/// change. This is transient preview state; it does not retain an editor object or authorize writes.
/// </summary>
public sealed record LearningPreviewState
{
    /// <summary>Creates the empty state before a request has been submitted.</summary>
    public static LearningPreviewState Idle { get; } = new();

    /// <summary>The identity used to reject a late result from an older engine task.</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>The exact captured sentence passed to request policy and the engine.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>A complete validated translation, available only when <see cref="Status"/> is Ready.</summary>
    public string? Result { get; init; }

    /// <summary>
    /// A bounded, monotonic text prefix while the engine is running. It is provisional and is always
    /// cleared on failure, cancellation, supersession, or completion; it never authorizes a write.
    /// </summary>
    public string? PartialResult { get; init; }

    /// <summary>The current request lifecycle; only Ready contains a usable result.</summary>
    public LearningPreviewStatus Status { get; init; } = LearningPreviewStatus.Idle;

    /// <summary>A fixed, safe message for Failed; backend exception text is never copied here.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The immutable translation settings captured when this request began.</summary>
    public LearningTranslationOptions? Options { get; init; }
}
