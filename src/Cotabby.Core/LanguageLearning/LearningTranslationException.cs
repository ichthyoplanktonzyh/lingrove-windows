namespace Cotabby.Core.LanguageLearning;

/// <summary>Fixed failure categories for bounded, recoverable learning-policy rejections.</summary>
public enum LearningTranslationFailure
{
    InvalidRequestId,
    InvalidContextBudget,
    InvalidOptions,
    SameLanguage,
    InvalidSourceRange,
    InvalidCaretRange,
    NoConvertibleText,
    NoSentenceAtCaret,
    SentenceBeyondVerifiedRange,
    SentenceTokenizerUnavailable,
    OversizedTarget,
    UnsupportedStyle,
    MalformedMarkedRange,
    MarkedText,
    InvalidOutput,
    ProviderNotConfigured,
    ProviderFailure,
}

/// <summary>
/// A user-facing policy failure with a fixed message. Dynamic source text, model output, and other
/// untrusted values never become exception messages or normal log content.
/// </summary>
public class LearningTranslationException : Exception
{
    public LearningTranslationException(LearningTranslationFailure failure)
        : base(MessageFor(failure)) => Failure = failure;

    public LearningTranslationFailure Failure { get; }

    private static string MessageFor(LearningTranslationFailure failure) => failure switch
    {
        LearningTranslationFailure.InvalidRequestId => "The translation request could not be identified.",
        LearningTranslationFailure.InvalidContextBudget => "The translation model budget is invalid.",
        LearningTranslationFailure.InvalidOptions => "The translation settings could not be applied.",
        LearningTranslationFailure.SameLanguage => "The source language is not enabled or matches the target. Check the translation settings.",
        LearningTranslationFailure.InvalidSourceRange => "The text range is incomplete. Select the committed text again.",
        LearningTranslationFailure.InvalidCaretRange => "Could not confirm the caret range.",
        LearningTranslationFailure.NoConvertibleText => "The selected range contains no text that can be converted.",
        LearningTranslationFailure.NoSentenceAtCaret => "There is no complete sentence at the caret. Select text manually.",
        LearningTranslationFailure.SentenceBeyondVerifiedRange => "The current sentence extends beyond the verifiable range. Select text manually; truncated text was not sent.",
        LearningTranslationFailure.SentenceTokenizerUnavailable => "Sentence boundaries are unavailable for this field. Select text manually.",
        LearningTranslationFailure.OversizedTarget => "The full target and output reserve exceed this model's budget. Select less text or raise the budget. Nothing was shortened or replaced.",
        LearningTranslationFailure.UnsupportedStyle => "This model does not support translation style instructions.",
        LearningTranslationFailure.MalformedMarkedRange => "You can read or copy the result. This editor cannot confirm that writing is safe, so the preview is read-only.",
        LearningTranslationFailure.MarkedText => "Your input method has uncommitted text. Confirm the candidate and finish composing before previewing; the original text was kept.",
        LearningTranslationFailure.InvalidOutput => "The model returned empty, oversized, or unsafe-to-insert text. The original text was kept.",
        LearningTranslationFailure.ProviderNotConfigured => "Choose and configure a translation endpoint before requesting a translation.",
        LearningTranslationFailure.ProviderFailure => "The translation endpoint could not complete the request. Check the translation settings and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown translation failure."),
    };
}
