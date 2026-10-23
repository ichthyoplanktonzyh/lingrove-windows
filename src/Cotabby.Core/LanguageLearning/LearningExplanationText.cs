using System.Globalization;
using System.Text;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Text primitives at the Foundation/.NET seam for explanation policy. They never normalize study
/// text: byte limits, original UTF-16 ranges and JSON-quoted text must describe the same capture.
/// FoundationTextRanges adapts composed-sequence boundaries (including NSString's separate CR/LF);
/// Unicode-version differences remain a platform gate. Readonly UI selection uses another map.
/// </summary>
internal static class LearningExplanationText
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static bool Fits(string? text, int maximum)
    {
        if (text is null) return false;
        try { return StrictUtf8.GetByteCount(text) <= maximum; }
        catch (EncoderFallbackException) { return false; }
    }

    // Foundation's set includes the historical U+200B; .NET Trim/Unicode White_Space do not.
    // U+FEFF is deliberately retained. Only the comparison copy is trimmed, never a captured range.
    public static string Trim(string text) => text.Trim(TrimCharacters);
    private static readonly char[] TrimCharacters =
        "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u200B\u2028\u2029\u202F\u205F\u3000".ToCharArray();

    public static int[] Boundaries(string text) => FoundationTextRanges.Boundaries(text);
    public static bool ValidRange(int location, int length, string text, int[] boundaries) =>
        FoundationTextRanges.IsValid(location, length, text, boundaries);

    public static string ExpandedWindow(string text, int start, int end, int[] boundaries)
    {
        int lower = Array.BinarySearch(boundaries, start), upper = Array.BinarySearch(boundaries, end);
        if (lower < 0) start = boundaries[~lower - 1];
        if (upper < 0) end = boundaries[~upper];
        return text[start..end];
    }

    /// <summary>
    /// JSONEncoder's default representation escapes slash but retains non-ASCII scalars, including
    /// emoji and U+2028. .NET serializer escaping differs and would change the reference budget.
    /// Callers validate well-formed UTF-16 first, so iterating chars preserves each surrogate pair.
    /// </summary>
    public static string Quote(string text)
    {
        var result = new StringBuilder(text.Length + 2).Append('"');
        foreach (char value in text)
        {
            result.Append(value switch
            {
                '"' => "\\\"", '\\' => "\\\\", '/' => "\\/", '\b' => "\\b", '\f' => "\\f",
                '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' => "\\u" + ((int)value).ToString("x4", CultureInfo.InvariantCulture),
                _ => value.ToString(),
            });
        }
        return result.Append('"').ToString();
    }
}
