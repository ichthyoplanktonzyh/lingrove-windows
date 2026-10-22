namespace Cotabby.Core.LanguageLearning;

public enum LearningExplanationFailure { InvalidContent, IncompleteResponse, ContextBudgetExceeded, InstructionModelRequired, ProviderFailure }

/// <summary>Fixed recoverable Mac policy messages; untrusted source/model text never enters errors.</summary>
public sealed class LearningExplanationException : Exception
{
    public LearningExplanationException(LearningExplanationFailure failure) : base(failure switch
    {
        LearningExplanationFailure.InvalidContent => "This vocabulary item could not be verified.",
        LearningExplanationFailure.IncompleteResponse => "The model returned an incomplete explanation. Try again.",
        LearningExplanationFailure.ContextBudgetExceeded => "The chosen text exceeds this model’s context budget.",
        LearningExplanationFailure.InstructionModelRequired => "Choose a general instruction model for AI explanation.",
        // A Windows foreign adapter can throw arbitrary IO/native errors. This fixed service
        // boundary intentionally carries neither their contents nor a misleading task/provider.
        LearningExplanationFailure.ProviderFailure => "The explanation could not be completed. Check the explanation settings and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    }) => Failure = failure;
    public LearningExplanationFailure Failure { get; }
}
