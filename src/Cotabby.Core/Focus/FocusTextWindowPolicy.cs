namespace Cotabby.Core.Focus;

/// <summary>
/// Bounds UIA text for an immutable snapshot while retaining whether any source text was omitted.
/// The focus producer supplies the document prefix and at least one extra suffix unit; insertion
/// policy uses the completeness flag to refuse ambiguous local windows. This helper owns no UIA
/// element and keeps the character-budget and surrogate-boundary rules portable and testable.
/// </summary>
public static class FocusTextWindowPolicy
{
    public const int MaxCharsBeforeCaret = 14000;
    public const int MaxCharsAfterCaret = 512;

    public static (string before, string after, bool truncatedWindow) Bound(string before, string after)
    {
        bool truncated = before.Length > MaxCharsBeforeCaret || after.Length > MaxCharsAfterCaret;
        if (before.Length > MaxCharsBeforeCaret)
        {
            before = before[^MaxCharsBeforeCaret..];
            if (char.IsLowSurrogate(before[0])) before = before[1..];
        }
        if (after.Length > MaxCharsAfterCaret) after = after[..MaxCharsAfterCaret];
        if (after.Length > 0 && char.IsHighSurrogate(after[^1]))
        {
            after = after[..^1];
            truncated = true;
        }
        return (before, after, truncated);
    }
}
