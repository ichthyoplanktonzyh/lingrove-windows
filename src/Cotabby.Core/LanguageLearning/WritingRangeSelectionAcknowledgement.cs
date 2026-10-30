using Cotabby.Core.Focus;

namespace Cotabby.Core.LanguageLearning;

public enum WritingRangeSelectionDecision { Pending, Confirmed, Rejected }

/// <summary>3b selection ACK: the exact target confirms; the original selection or an unavailable
/// receipt may still be pending. A changed value, session/safety or unrelated selection rejects.
/// Selection is set once; this policy never authorizes a second selection setter or a content retry.</summary>
public static class WritingRangeSelectionAcknowledgement
{
    public const int MaximumAttempts = 7;
    public static TimeSpan PollInterval => TimeSpan.FromMilliseconds(45);
    public static WritingRangeSelectionDecision Assess(FocusedField saved, LearningTextRange target, bool sameSessionAndSafety,
        string? value, LearningTextRange? selection)
    {
        if (!sameSessionAndSafety || (value is not null && value != saved.Text)) return WritingRangeSelectionDecision.Rejected;
        if (selection is null) return WritingRangeSelectionDecision.Pending;
        if (value is null) return WritingRangeSelectionDecision.Rejected;
        if (selection.Value == target) return WritingRangeSelectionDecision.Confirmed;
        return selection.Value == new LearningTextRange(saved.CaretOffset, saved.SelectionLength)
            ? WritingRangeSelectionDecision.Pending : WritingRangeSelectionDecision.Rejected;
    }
}
