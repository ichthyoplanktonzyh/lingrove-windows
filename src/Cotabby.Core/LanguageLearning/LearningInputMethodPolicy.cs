namespace Cotabby.Core.LanguageLearning;

/// <summary>How much the capture host could establish about an IME's marked text.</summary>
public enum LearningMarkedRangeEvidence
{
    ExplicitlyEmpty,
    UnavailableWithComposition,
    UnavailableWithoutComposition,
}

public enum LearningInputMethodDisposition
{
    ReplacementAllowed,
    CopyOnly,
    RejectPreview,
}

/// <summary>
/// Immutable result of checking input-method evidence. The macOS contract treats a positive marked
/// range as uncommitted text, malformed negative length as copy-only, and a missing range as
/// unknown evidence that still requires an explicit replacement confirmation.
/// </summary>
public sealed record LearningInputMethodAssessment(
    LearningInputMethodDisposition Disposition,
    LearningMarkedRangeEvidence? Evidence = null,
    string? Reason = null)
{
    public bool PermitsReplacement => Disposition == LearningInputMethodDisposition.ReplacementAllowed;

    public string? ReplacementUnavailableReason => Disposition == LearningInputMethodDisposition.CopyOnly ? Reason : null;

    public string? PreviewRejectionReason => Disposition == LearningInputMethodDisposition.RejectPreview ? Reason : null;
}

/// <summary>
/// Pure policy over marked-range evidence. Input-source identity only describes composition
/// capability; only a positive range proves live preedit text for this particular field.
/// </summary>
public static class LearningInputMethodPolicy
{
    public static string MarkedTextConfirmationMessage =>
        new LearningTranslationException(LearningTranslationFailure.MarkedText).Message;

    public static string UnavailableMarkedRangeReason =>
        new LearningTranslationException(LearningTranslationFailure.MalformedMarkedRange).Message;

    public static LearningInputMethodAssessment Assess(LearningTextRange? markedRange, bool isComposingInputMethod)
    {
        if (markedRange is { Length: < 0 })
            return new LearningInputMethodAssessment(
                LearningInputMethodDisposition.CopyOnly,
                Reason: UnavailableMarkedRangeReason);

        if (markedRange is { Length: > 0 })
            return new LearningInputMethodAssessment(
                LearningInputMethodDisposition.RejectPreview,
                Reason: MarkedTextConfirmationMessage);

        if (markedRange is { Length: 0 })
            return new LearningInputMethodAssessment(
                LearningInputMethodDisposition.ReplacementAllowed,
                LearningMarkedRangeEvidence.ExplicitlyEmpty);

        return new LearningInputMethodAssessment(
            LearningInputMethodDisposition.ReplacementAllowed,
            isComposingInputMethod
                ? LearningMarkedRangeEvidence.UnavailableWithComposition
                : LearningMarkedRangeEvidence.UnavailableWithoutComposition);
    }
}
