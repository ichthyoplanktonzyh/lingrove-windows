namespace Cotabby.Core.Suggestions;

/// <summary>
/// One visible suggestion. Immutable, so every transition (typing through it,
/// accepting part of it) produces a fresh instance.
/// </summary>
public sealed record SuggestionSession
{
    public required string RequestId { get; init; }

    /// <summary>The part of the suggestion not yet typed or accepted.</summary>
    public required string VisibleText { get; init; }
    /// <summary>Validated future text, never accepted until it becomes visible.</summary>
    public string HiddenText { get; init; } = "";
    public bool ShowFollowingWords { get; init; } = true;
    internal string ClipboardContext { get; init; } = "";

    public SuggestionSession Advance(int utf16Count)
    {
        int consumed = Math.Clamp(utf16Count, 0, VisibleText.Length + HiddenText.Length);
        var visible = VisibleText[Math.Min(consumed, VisibleText.Length)..];
        if (visible.Length > 0) return this with { VisibleText = visible };
        var remainder = HiddenText[Math.Max(0, consumed - VisibleText.Length)..];
        if (remainder.Length == 0) return this with { VisibleText = "", HiddenText = "" };
        var offered = ShowFollowingWords ? remainder : AcceptanceChunker.NextChunk(remainder);
        return this with { VisibleText = offered, HiddenText = remainder[offered.Length..] };
    }

    /// <summary>Set when the session offers a spelling fix instead of a continuation.</summary>
    public TypoCorrection? Correction { get; init; }
}

/// <summary>A misspelled word before the caret and its fix. Accepting swaps them as one unit.</summary>
public sealed record TypoCorrection(string Word, string CorrectedWord);
