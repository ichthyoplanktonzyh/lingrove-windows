using System.Globalization;

namespace Cotabby.Core.Text;

/// <summary>
/// Portable counterpart of NSString's composed-sequence range validation, used by Mac vocabulary
/// and explanation evidence. Unlike Swift Character/.NET text-element selection, NSString permits
/// the CR and LF in CRLF to be referenced separately. Keep that boundary here; readonly UI selection
/// continues to use whole extended graphemes through LiteralTextSelectionMap.
/// Unicode-version differences beyond the pinned fixtures still need platform verification.
/// </summary>
internal static class FoundationTextRanges
{
    public static int[] Boundaries(string text)
    {
        int[] graphemes = StringInfo.ParseCombiningCharacters(text);
        if (!text.Contains("\r\n", StringComparison.Ordinal)) return [.. graphemes, text.Length];
        var boundaries = new List<int>(graphemes);
        for (int index = 1; index < text.Length; index++)
            if (text[index - 1] == '\r' && text[index] == '\n') boundaries.Add(index);
        boundaries.Sort(); boundaries.Add(text.Length);
        return boundaries.ToArray();
    }
    public static bool IsValid(int location, int length, string text, int[] boundaries) =>
        location >= 0 && length > 0 && location <= text.Length && length <= text.Length - location
        && Array.BinarySearch(boundaries, location) >= 0 && Array.BinarySearch(boundaries, location + length) >= 0;
}
