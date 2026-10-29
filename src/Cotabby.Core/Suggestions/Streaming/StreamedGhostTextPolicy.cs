using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.Suggestions.Streaming;

/// <summary>3b streamed cumulative presentation and hidden-fragment rule. Counts and prefixes
/// are Swift-compatible graphemes; no displayed text or editor state is changed here.</summary>
public static class StreamedGhostTextPolicy
{
    public static string CompletedBufferedPrediction(string text, int visibleCharacterCount)
    {
        var value = new GraphemeString(text);
        int visible = Math.Clamp(visibleCharacterCount, 0, value.Count);
        if (visible == value.Count || value.Last is not { } last ||
            !(last.IsLetter || last.IsNumber || last.Is('_') || CaretWordContext.IsConnector(last))) return text;
        int tokenStart = value.Count - value.SuffixLength(c => !c.IsWhitespace);
        if ((last.Is('\'') && value[tokenStart].Is('\'')) || (last.Is('’') && value[tokenStart].Is('‘'))) return text;
        return value.Prefix(Math.Max(visible, tokenStart));
    }
    public static bool IsRenderableExtension(string candidate, string? rendered)
    {
        if (candidate.Length == 0) return false;
        if (string.IsNullOrEmpty(rendered)) return true;
        var result = new GraphemeString(candidate); var prior = new GraphemeString(rendered);
        return result.Count > prior.Count && CanonicalStringComparer.Instance.Equals(result.Prefix(prior.Count), rendered);
    }
}
