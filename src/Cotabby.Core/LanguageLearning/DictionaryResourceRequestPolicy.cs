namespace Cotabby.Core.LanguageLearning;

/// <summary>Custom-scheme requests are scoped to the displayed book and one relative resource.
/// This pure rule does not grant filesystem access or open URLs.</summary>
public static class DictionaryResourceRequestPolicy
{
    public const string Scheme = "lingrove-dictionary";
    public static string ArticleUri(Guid library) => $"{Scheme}://book-{library:N}/__article__";
    public static string? ResourcePath(string raw, Guid library)
    {
        if (raw is null || raw.Length > 4096 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || uri.Scheme != Scheme || !string.Equals(uri.Host, "book-" + library.ToString("N"), StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        int scheme = raw.IndexOf("://", StringComparison.Ordinal); if (scheme < 0) return null;
        int slash = raw.IndexOf('/', scheme + 3); if (slash < 0) return null;
        return DictionaryContentPolicy.ResourcePath(raw[(slash + 1)..]);
    }
}
