using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// The external-link subset of the pinned Mac policy. Dictionary content supplies a destination,
/// never navigation authority; App may open only this bounded HTTP(S) URI after an explicit click.
/// Local files, executable schemes and embedded credentials cannot leave the card.
/// </summary>
public static class DictionaryLinkPolicy
{
    public static Uri? ExternalUrl(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0)
            return null;
        // Uri.UserInfo collapses an explicitly empty user to "". Swift URL.user distinguishes
        // that from no user, so reject even a bare @ in the original authority.
        int start = value.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return null;
        var authority = value.AsSpan(start + 3);
        int end = authority.IndexOfAny('/', '?', '#');
        if (end >= 0) authority = authority[..end];
        return authority.Contains('@') ? null : uri;
    }
}
