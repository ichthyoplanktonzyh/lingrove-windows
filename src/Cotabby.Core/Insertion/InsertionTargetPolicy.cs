using Cotabby.Core.Focus;
using Cotabby.Core.Support;

namespace Cotabby.Core.Insertion;

/// <summary>
/// Pure eligibility rules for guarded text edits. The policy compares immutable focus snapshots
/// and validates UTF-16 ranges without querying UI Automation or changing any host state.
/// </summary>
public static class InsertionTargetPolicy
{
    private const int MaximumInsertionUtf16Units = 64 * 1024;

    /// <summary>
    /// Return true when a snapshot contains enough reliable identity and caret evidence to support
    /// a guarded edit. A missing field id, secure field, selection, guessed selection state,
    /// incomplete text window, or malformed caret/text encoding fails closed.
    /// </summary>
    public static bool IsEligibleField(FocusedField? field)
    {
        if (field is null
            || field.ProcessId <= 0
            || field.FocusChangeSequence <= 0
            || string.IsNullOrWhiteSpace(field.FieldId)
            || field.IsSecure
            || !CompositionEvidencePolicy.IsClear(field)
            || field.HasSelection
            || field.SelectionLength != 0
            || !field.HasReliableSelection
            || field.IsTextWindowTruncated
            || field.Text is null
            || !IsWellFormedUtf16(field.Text)
            || !IsUtf16Boundary(field.Text, field.CaretOffset))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Return true only when the fresh snapshot still identifies the same unselected field and
    /// exact text/caret state that the caller originally captured.
    /// </summary>
    public static bool IsCurrentTarget(FocusedField? expected, FocusedField? current)
    {
        return IsSameField(expected, current)
            && string.Equals(expected!.Text, current!.Text, StringComparison.Ordinal)
            && expected.CaretOffset == current.CaretOffset
            && string.Equals(expected.TrailingText, current.TrailingText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Return true when two eligible snapshots name the same field in the same focus epoch. Text
    /// and caret may change, which lets a capture remain anchored while a user types in its field.
    /// </summary>
    public static bool IsSameField(FocusedField? expected, FocusedField? current)
    {
        if (!IsEligibleField(expected) || !IsEligibleField(current)) return false;

        return expected!.ProcessId == current!.ProcessId
            && string.Equals(expected.FieldId, current.FieldId, StringComparison.Ordinal)
            && expected.FocusChangeSequence == current.FocusChangeSequence
            && CompositionEvidencePolicy.IsSameReceipt(expected, current);
    }

    /// <summary>
    /// Return true for well-formed output accepted by the shared insertion safety gate. Empty text
    /// is allowed because an ordinary empty insert is a harmless successful no-op.
    /// </summary>
    public static bool IsValidInsertionText(string? text)
    {
        if (text is null) return false;
        if (text.Length == 0) return true;
        if (text.Length > MaximumInsertionUtf16Units) return false;

        return IsWellFormedUtf16(text) && InsertionSafetyGate.IsSafeToInsert(text);
    }

    /// <summary>
    /// Validate the deletion range and replacement output. Until each host's native range-delete
    /// behavior is proven, destructive spans are limited to printable ASCII so UTF-16 units match
    /// the single characters the host is expected to remove.
    /// </summary>
    public static bool IsValidReplacement(FocusedField? target, int deleteUtf16Count, string? text)
    {
        if (!IsEligibleField(target)
            || deleteUtf16Count < 0
            || deleteUtf16Count > target!.CaretOffset
            || !IsValidInsertionText(text))
        {
            return false;
        }

        // Removing text without inserting a replacement is too destructive for this speculative
        // path. A zero-length edit is still a no-op and is handled safely by the inserter.
        if (deleteUtf16Count > 0 && text!.Length == 0) return false;

        int start = target.CaretOffset - deleteUtf16Count;
        if (!IsUtf16Boundary(target.Text, start)) return false;

        for (int index = start; index < target.CaretOffset; index++)
        {
            char codeUnit = target.Text[index];
            if (codeUnit < 0x20 || codeUnit > 0x7E) return false;
        }

        return true;
    }

    private static bool IsUtf16Boundary(string text, int offset)
    {
        if (offset < 0 || offset > text.Length) return false;

        // An offset between a high and low surrogate would split one Unicode scalar into invalid
        // fragments. UIA offsets are UTF-16, so this check belongs at the portable boundary.
        return offset == 0
            || offset == text.Length
            || !char.IsHighSurrogate(text[offset - 1])
            || !char.IsLowSurrogate(text[offset]);
    }

    private static bool IsWellFormedUtf16(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            char codeUnit = text[index];
            if (char.IsHighSurrogate(codeUnit))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(codeUnit))
            {
                return false;
            }
        }

        return true;
    }
}
