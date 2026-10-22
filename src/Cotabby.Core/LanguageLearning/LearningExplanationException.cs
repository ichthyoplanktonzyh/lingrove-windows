namespace Cotabby.Core.LanguageLearning;

public enum LearningExplanationFailure { InvalidContent, IncompleteResponse, ContextBudgetExceeded }

/// <summary>Fixed recoverable Mac policy messages; untrusted source/model text never enters errors.</summary>
public sealed class LearningExplanationException : Exception
{
    public LearningExplanationException(LearningExplanationFailure failure) : base(failure switch
    {
        LearningExplanationFailure.InvalidContent => "This vocabulary item could not be verified.",
        LearningExplanationFailure.IncompleteResponse => "The model returned an incomplete explanation. Try again.",
        LearningExplanationFailure.ContextBudgetExceeded => "The chosen text exceeds this model’s context budget.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    }) => Failure = failure;
    public LearningExplanationFailure Failure { get; }
}
