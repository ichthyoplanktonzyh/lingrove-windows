namespace Cotabby.Core.Context;

/// <summary>
/// Bounds an explicit plain-text copy before any clipboard mutation. Reject rather than truncate
/// so the user's selected source/result remains exact, including spaces, newlines and Unicode.
/// The native CF_UNICODETEXT terminator cannot represent an embedded NUL or malformed UTF-16.
/// </summary>
public static class ClipboardTextCopyPolicy
{
    public const int MaximumUtf16Length = 128 * 1024;

    public static bool IsValid(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaximumUtf16Length || text.Contains('\0')) return false;
        for (int index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index])) return false;
            }
            else if (char.IsLowSurrogate(text[index])) return false;
        }
        return true;
    }
}
