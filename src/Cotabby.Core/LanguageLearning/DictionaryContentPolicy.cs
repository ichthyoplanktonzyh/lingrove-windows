using System.Text;
using System.Text.RegularExpressions;
using Cotabby.Core.Support;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Mac 3b dictionary text and resource boundaries. Returned paths are relative keys,
/// not filesystem authority; a platform owner must still verify the resolved file.</summary>
public static class DictionaryContentPolicy
{
    public static string ImportTitle(string title, string fallback) => title.Length == 0 ? fallback : string.Concat(new GraphemeString(title).Take(200).Select(x => x.ToString()));
    public static bool ValidQuery(string text) => !string.IsNullOrEmpty(text)
        && LearningExplanationText.Fits(text, 256) && InsertionSafetyGate.IsSafeToInsert(text);
    public static string? ResourcePath(string path)
    {
        if (path is null) return null;
        string value = path;
        try
        {
            if (!Regex.IsMatch(path, "%(?![0-9a-fA-F]{2})")) value = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException) { }
        var normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || !LearningExplanationText.Fits(normalized, 2048) || normalized.Contains('\0')
            || normalized.Contains(':') || normalized.Split('/').Contains("..", StringComparer.Ordinal)) return null;
        return normalized;
    }
    public static string Escaped(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    public static string PlainText(string html, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = TimeSpan.FromMilliseconds(250);
        string cleaned = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, deadline);
        cancellationToken.ThrowIfCancellationRequested();
        cleaned = Regex.Replace(cleaned, @"</?(?:div|p|br|li|h[1-6]|tr)\b[^>]*>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, deadline);
        cancellationToken.ThrowIfCancellationRequested();
        cleaned = Regex.Replace(cleaned, @"<[^>]*>", "", RegexOptions.CultureInvariant, deadline);
        cancellationToken.ThrowIfCancellationRequested();
        cleaned = cleaned.Replace("&nbsp;", " ").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
        return FoundationText.TrimWhitespaceAndNewlines(cleaned);
    }
}
