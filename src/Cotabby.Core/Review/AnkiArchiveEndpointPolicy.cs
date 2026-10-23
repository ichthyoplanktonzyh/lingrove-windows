using System.Globalization;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Review;

/// <summary>
/// Adaptation of Mac AnkiReviewPolicy.endpoint for retained receipts and explicit local connections.
/// This performs no DNS, HTTP, mutation of stored values or endpoint selection. Parse the
/// original authority before .NET Uri can canonicalize shorthand IPv4 into an allowed loopback.
/// </summary>
public static class AnkiArchiveEndpointPolicy
{
    public static bool IsValid(string? text) => Normalize(text) is not null;
    public static string? Normalize(string? text)
    {
        if (!LearningExplanationText.Fits(text, int.MaxValue)) return null;
        string value = LearningExplanationText.Trim(text!);
        if (!value.StartsWith("http://", StringComparison.Ordinal) || value.IndexOfAny(['?', '#', '@', '\\']) >= 0) return null;
        string authority = value[7..]; int slash = authority.IndexOf('/');
        if (slash >= 0)
        {
            if (authority[slash..] != "/") return null;
            authority = authority[..slash];
        }
        string host, port;
        if (authority.StartsWith('['))
        {
            int end = authority.IndexOf(']'); if (end < 0) return null;
            host = authority[..(end + 1)]; string tail = authority[(end + 1)..];
            if (tail.Length != 0 && !tail.StartsWith(':')) return null;
            port = tail.Length == 0 ? "" : tail[1..];
        }
        else
        {
            int colon = authority.IndexOf(':');
            host = colon < 0 ? authority : authority[..colon]; port = colon < 0 ? "" : authority[(colon + 1)..];
        }
        // URLComponents decodes escaped host spelling before checking the allowlist.
        try { host = Uri.UnescapeDataString(host).ToLowerInvariant(); }
        catch (UriFormatException) { return null; }
        if (host is not ("localhost" or "127.0.0.1" or "[::1]" or "::1")) return null;
        if (port.Length == 0) return "http://" + host; // URLComponents treats a trailing colon as no port.
        if (port.Any(c => c is < '0' or > '9')) return null;
        string significant = port.TrimStart('0');
        return significant.Length <= 5 && int.TryParse(significant, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number is >= 1 and <= 65535 ? "http://" + host + ":" + number.ToString(CultureInfo.InvariantCulture) : null;
    }
}
