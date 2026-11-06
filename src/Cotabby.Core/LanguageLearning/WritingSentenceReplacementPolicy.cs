using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Pure admission and acknowledgement for the Windows caret-ending subset of the 3b
/// sentence replacement transaction. Dispatch itself is never an acknowledgement.</summary>
public static class WritingSentenceReplacementPolicy
{
    public static bool CanReplace(FocusedField? field, LearningTextRange range, string? source, string? replacement)
    {
        if (field is null || source is null || replacement is null || source.Length == 0 || replacement.Length == 0
            || range.Location < 0 || range.Length != source.Length || range.Location > field.Text.Length
            || range.Length > field.Text.Length - range.Location || range.Location + range.Length != field.CaretOffset
            || !string.Equals(field.Text.Substring(range.Location, range.Length), source, StringComparison.Ordinal)) return false;
        return InsertionTargetPolicy.IsValidReplacement(field, range.Length, replacement);
    }

    public static bool IsAcknowledged(FocusedField saved, LearningTextRange range, string source, string replacement, FocusedField? current)
    {
        if (!CanReplace(saved, range, source, replacement) || !InsertionTargetPolicy.IsSameField(saved, current)) return false;
        string expected = string.Concat(saved.Text.AsSpan(0, range.Location), replacement, saved.Text.AsSpan(range.Location + range.Length));
        return string.Equals(expected, current!.Text, StringComparison.Ordinal);
    }
}
