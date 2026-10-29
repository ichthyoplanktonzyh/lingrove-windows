using Cotabby.Core.Text;

namespace Cotabby.Core.Support;

/// <summary>Pure Mac prompt-preface cleanup borrowed by SuggestionTextNormalizer. It owns only
/// the distinctive phrase list and cuts at the first echo, preserving useful output before it.
/// Keeping it separate prevents prompt wording from becoming another coordinator/backend rule.</summary>
internal static class PromptPrefaceEchoFilter
{
    private static readonly string[] AnywherePhrases = [
        "The user usually writes in", "Match the language of the text before the caret",
        "Notes the writer keeps in mind:", "Nearby on screen:", "On the clipboard:", "Later in the same passage:"];
    private static readonly string[] LineStartPhrases = ["Writing style:", "Written by "];

    internal static string Truncated(string text)
    {
        int cut = text.Length;
        foreach (string phrase in AnywherePhrases)
        {
            int index = text.IndexOf(phrase, StringComparison.Ordinal);
            if (index >= 0) cut = Math.Min(cut, index);
        }
        int lineStart = 0;
        while (lineStart < cut)
        {
            var line = new GraphemeString(text[lineStart..]);
            int trimmedStart = lineStart + line.Offset(line.PrefixLength(g => g.IsWhitespace && !g.IsNewline));
            if (LineStartPhrases.Any(phrase => text.AsSpan(trimmedStart).StartsWith(phrase, StringComparison.Ordinal)))
            { cut = Math.Min(cut, lineStart); break; }
            int newline = text.IndexOf('\n', lineStart);
            if (newline < 0) break;
            lineStart = newline + 1;
        }
        if (cut == text.Length) return text;
        var kept = new GraphemeString(text[..cut]);
        return kept.Prefix(kept.Count - kept.SuffixLength(g => g.IsWhitespace));
    }
}
