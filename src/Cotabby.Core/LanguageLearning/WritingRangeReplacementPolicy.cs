using System.Globalization;
using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Exact Unicode selection/caret capture and ACK policy for the 3b range transaction.
/// UTF-16 offsets are source anchors, never a count of Backspace or UIA Character operations.</summary>
public static class WritingRangeReplacementPolicy
{
    public static bool IsCapture(FocusedField? field) => field is not null
        && InsertionTargetPolicy.IsEligibleField(field with { HasSelection = false, SelectionLength = 0 })
        && field.SelectionLength >= 0 && field.SelectionLength <= field.Text.Length - field.CaretOffset
        && field.HasSelection == (field.SelectionLength > 0)
        && Boundary(field.Text, field.CaretOffset) && Boundary(field.Text, field.CaretOffset + field.SelectionLength);

    public static bool CanReplace(FocusedField? field, LearningTextRange range, string? source, string? replacement)
    {
        if (!IsCapture(field) || !field!.SupportsRangeReplacement || string.IsNullOrEmpty(source)
            || string.IsNullOrEmpty(replacement) || !InsertionTargetPolicy.IsValidInsertionText(replacement)
            || range.Location < 0 || range.Length != source.Length || range.Location > field.Text.Length
            || range.Length > field.Text.Length - range.Location
            || !Boundary(field.Text, range.Location) || !Boundary(field.Text, range.Location + range.Length)
            || !field.Text.AsSpan(range.Location, range.Length).SequenceEqual(source)) return false;
        return field.HasSelection ? range.Location == field.CaretOffset && range.Length == field.SelectionLength
            : range.Location <= field.CaretOffset && field.CaretOffset <= range.Location + range.Length;
    }

    public static bool SameField(FocusedField saved, FocusedField? current) => IsCapture(saved) && IsCapture(current)
        && saved.ProcessId == current!.ProcessId && saved.FieldId == current.FieldId
        && saved.FocusChangeSequence == current.FocusChangeSequence
        && CompositionEvidencePolicy.IsSameReceipt(saved, current);

    public static bool IsCurrent(FocusedField saved, FocusedField? current) => SameField(saved, current)
        && saved.Text == current!.Text && saved.CaretOffset == current.CaretOffset
        && saved.SelectionLength == current.SelectionLength
        && saved.SupportsRangeReplacement == current.SupportsRangeReplacement;

    public static bool IsAcknowledged(FocusedField saved, LearningTextRange range, string source, string replacement, FocusedField? current) =>
        CanReplace(saved, range, source, replacement) && SameField(saved, current)
        && current!.Text == string.Concat(saved.Text.AsSpan(0, range.Location), replacement, saved.Text.AsSpan(range.Location + range.Length));

    private static bool Boundary(string text, int position) => position == text.Length
        || Array.BinarySearch(StringInfo.ParseCombiningCharacters(text), position) >= 0;
}
