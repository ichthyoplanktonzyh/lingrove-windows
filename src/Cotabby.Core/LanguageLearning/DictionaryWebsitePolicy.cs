using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Mac 3b website URL construction. This only builds an explicit browser offer;
/// neither URL creation nor source selection opens a browser or performs a network request.</summary>
public static class DictionaryWebsitePolicy
{
    public static Uri? Url(string? template, string? query)
    {
        if (template is null || query is null || Encoding.UTF8.GetByteCount(template) > 2048 || !DictionaryContentPolicy.ValidQuery(query)) return null;
        const string placeholder = "{query}";
        int first = template.IndexOf(placeholder, StringComparison.Ordinal);
        if (first < 0 || template.IndexOf(placeholder, first + placeholder.Length, StringComparison.Ordinal) >= 0) return null;
        int scheme = template.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return null;
        int authorityStart = scheme + 3;
        int authorityEnd = template.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0) authorityEnd = template.Length;
        if (first >= authorityStart && first < authorityEnd || template.AsSpan(authorityStart, authorityEnd - authorityStart).Contains('@')) return null;
        string value;
        try { value = template[..first] + Uri.EscapeDataString(query) + template[(first + placeholder.Length)..]; }
        catch (UriFormatException) { return null; }
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Length > 0 && uri.UserInfo.Length == 0 && !uri.Host.Contains('{') ? uri : null;
    }
}
