using Cotabby.Core.Focus;
using Cotabby.Core.Text;

namespace Cotabby.Core.ExpressionAssist;

/// <summary>A UTF-16 interval within a complete editor value.</summary>
public readonly record struct ExpressionEditorRange(int Start, int Length);

/// <summary>
/// Complete, immutable evidence captured by the platform editor owner. The retained native target
/// stays with that owner; these values never imply that a later focus or edit is safe by themselves.
/// </summary>
public sealed record ExpressionEditorObservation(
    int ProcessId, string FieldId, long FocusEpoch, long ObservationVersion,
    string Value, ExpressionEditorRange Selection, bool IsSecure, bool IsComplete,
    InputCompositionState Composition, long CompositionSequence);

/// <summary>The original selection and exact explicit replacement span have independent roles.</summary>
public sealed record ExpressionEditorReceipt(
    Guid SessionId, ExpressionEditorObservation Original, ExpressionEditorRange ReplacementRange,
    string Source, bool IsTrigger);

/// <summary>Portable admission and receipt comparisons; no platform handle or side effect lives here.</summary>
public static class ExpressionEditorReceiptPolicy
{
    public const int MaximumValueUtf16Units = 64 * 1024;

    public static bool IsAdmissible(ExpressionEditorObservation? observation)
    {
        if (observation is null || observation.ProcessId <= 0 || string.IsNullOrWhiteSpace(observation.FieldId)
            || observation.FocusEpoch <= 0 || observation.ObservationVersion <= 0 || observation.IsSecure
            || !observation.IsComplete || observation.Composition != InputCompositionState.Clear
            || observation.CompositionSequence <= 0 || observation.Value is null
            || observation.Value.Length > MaximumValueUtf16Units) return false;
        return InBounds(observation.Value, observation.Selection);
    }

    /// <summary>Trigger capture requires its literal UTF-16 text immediately before a collapsed caret.</summary>
    public static ExpressionEditorReceipt? Capture(ExpressionEditorObservation? observation, string? trigger)
    {
        if (!IsAdmissible(observation)) return null;
        var value = observation!.Value;
        var range = observation.Selection;
        bool isTrigger = trigger is not null;
        if (trigger is not null)
        {
            if (trigger.Length == 0 || !EditorTextRangePolicy.IsValid(trigger, 0, trigger.Length) || range.Length != 0 || trigger.Length > range.Start) return null;
            range = new(range.Start - trigger.Length, trigger.Length);
            if (!InBounds(value, range) || !value.AsSpan(range.Start, range.Length).SequenceEqual(trigger.AsSpan())) return null;
        }
        return new(Guid.NewGuid(), observation, range, value.Substring(range.Start, range.Length), isTrigger);
    }

    public static bool PermitsHostRead(int? frontmost, int host, int inputOwner, bool inputOwnsKeyboard) =>
        frontmost == host || (frontmost == inputOwner && inputOwnsKeyboard);

    public static bool ShouldRestoreHost(int? frontmost, int host, int inputOwner, bool inputOwnsKeyboard) =>
        host != inputOwner && frontmost == inputOwner && inputOwnsKeyboard;

    /// <summary>Comparison is scoped to the explicitly authorized return path; the caller must also
    /// establish input ownership and two stable observations in the new focus epoch.</summary>
    public static bool MatchesOriginal(ExpressionEditorReceipt saved, ExpressionEditorObservation? current)
    {
        if (!IsAdmissible(current) || !IsAdmissible(saved.Original) || saved.SessionId == Guid.Empty) return false;
        var old = saved.Original;
        return current!.ProcessId == old.ProcessId && string.Equals(current.FieldId, old.FieldId, StringComparison.Ordinal)
            && string.Equals(current.Value, old.Value, StringComparison.Ordinal) && current.Selection == old.Selection
            && (saved.IsTrigger
                ? old.Selection.Length == 0 && saved.ReplacementRange.Length > 0
                    && saved.ReplacementRange.Start >= 0 && saved.ReplacementRange.Start <= old.Selection.Start
                    && saved.ReplacementRange.Length == old.Selection.Start - saved.ReplacementRange.Start
                : saved.ReplacementRange == old.Selection)
            && InBounds(current.Value, saved.ReplacementRange)
            && current.Value.AsSpan(saved.ReplacementRange.Start, saved.ReplacementRange.Length).SequenceEqual(saved.Source.AsSpan());
    }

    private static bool InBounds(string text, ExpressionEditorRange range) =>
        EditorTextRangePolicy.IsValid(text, range.Start, range.Length);
}
