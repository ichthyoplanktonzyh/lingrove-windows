using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Original ModelContextBudget.identity destination keys, independent of admission and
/// transport. A settings profile retains the exact four-string JSON spelling, with Swift-style
/// slash escaping. Comparison belongs to CanonicalStringComparer, never a destructive rewrite
/// of the emitted key. No credential store, model or network is accessed here.</summary>
public static partial class ModelDestinationIdentity
{
    public static string Create(string backend, string model, string baseUrl = "", string mode = "") =>
        "[" + string.Join(",", new[] { backend, FoundationText.TrimWhitespaceAndNewlines(model), Normalize(FoundationText.TrimWhitespaceAndNewlines(baseUrl)), mode }.Select(Quoted)) + "]";
    private static string Normalize(string candidate)
    {
        // System.Uri removes dot segments and rejects some components Foundation retains.
        // Parse components explicitly; this helper formats identity only and cannot admit an
        // endpoint. The existing endpoint constructor still owns request URL validation.
        var match = Components().Match(candidate); if (!match.Success) return candidate;
        string scheme = match.Groups["scheme"].Value.ToLowerInvariant(), authority = match.Groups["authority"].Value;
        string path = match.Groups["path"].Value, query = match.Groups["query"].Value, fragment = match.Groups["fragment"].Value;
        bool hasAuthority = match.Groups["authority"].Success;
        if (scheme.Length == 0 && path.Split('/')[0].Contains(':')) return candidate;
        if (hasAuthority)
        {
            string hostPort = authority[(authority.LastIndexOf('@') + 1)..], host; string port = "";
            if (hostPort.StartsWith('['))
            {
                int end = hostPort.IndexOf(']'); if (end < 0) return candidate;
                host = hostPort[..(end + 1)]; if (end + 1 < hostPort.Length) { if (hostPort[end + 1] != ':') return candidate; port = hostPort[(end + 2)..]; }
            }
            else
            {
                int colon = hostPort.LastIndexOf(':'); host = colon < 0 ? hostPort : hostPort[..colon]; port = colon < 0 ? "" : hostPort[(colon + 1)..];
            }
            if (!host.StartsWith('[') && host.Any(c => char.IsWhiteSpace(c) || c is '\\' or '[' or ']' or ':')) return candidate;
            if (port.Length > 0 && !port.All(char.IsAsciiDigit)) return candidate;
            try
            {
                host = Uri.UnescapeDataString(host).ToLowerInvariant();
                if (!host.StartsWith('[')) host = new IdnMapping().GetAscii(host);
                else host = host.Replace("%", "%25", StringComparison.Ordinal);
            }
            catch (ArgumentException) { return candidate; }
            if (port.Length > 0 && int.TryParse(port, out int number))
                port = (scheme == "https" && number == 443 || scheme == "http" && number == 80) ? "" : number.ToString(CultureInfo.InvariantCulture);
            authority = "//" + host + (port.Length > 0 ? ":" + port : "");
        }
        string decoded = DecodePath(path).TrimEnd('/');
        if (candidate.Length > 0 && decoded.Length == 0) decoded = "/v1";
        return (scheme.Length == 0 ? "" : scheme + ":") + authority + Encode(decoded, false) + EncodeSuffix(query) + EncodeSuffix(fragment);
    }
    private static string DecodePath(string path)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < path.Length;)
        {
            if (path[i] == '%' && i + 2 < path.Length && Uri.IsHexDigit(path[i + 1]) && Uri.IsHexDigit(path[i + 2]))
            { bytes.Add(byte.Parse(path.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 3; }
            else { Rune rune = Rune.GetRuneAt(path, i); bytes.AddRange(Encoding.UTF8.GetBytes(rune.ToString())); i += rune.Utf16SequenceLength; }
        }
        // URLComponents' decoded path is empty when an escape cannot form UTF-8, so the
        // original nonempty-destination rule then selects /v1. Do not preserve invalid bytes.
        try { return new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
        catch (DecoderFallbackException) { return ""; }
    }
    private static string EncodeSuffix(string value)
    {
        if (value.Length == 0) return "";
        var result = new StringBuilder(value[..1]);
        for (int i = 1; i < value.Length;)
        {
            // The source changes path/host, but leaves query/fragment components untouched.
            // Existing percent triplets there retain spelling, including escaped slashes.
            if (value[i] == '%' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            { result.Append(value.AsSpan(i, 3)); i += 3; continue; }
            Rune rune = Rune.GetRuneAt(value, i); result.Append(Encode(rune.ToString(), true)); i += rune.Utf16SequenceLength;
        }
        return result.ToString();
    }
    private static string Encode(string value, bool suffix)
    {
        var output = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            int c = rune.Value;
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "-._~!$&'()*+,;=:@/".Contains((char)c) && c <= 127 || suffix && c <= 127 && "?[]".Contains((char)c)) output.Append(rune);
            else foreach (byte b in Encoding.UTF8.GetBytes(rune.ToString())) output.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return output.ToString();
    }
    private static string Quoted(string text)
    {
        var result = new StringBuilder("\"");
        foreach (char c in text)
            result.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '/' => "\\/", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture), _ => c.ToString(),
            });
        return result.Append('"').ToString();
    }
    [GeneratedRegex(@"^(?:(?<scheme>[A-Za-z][A-Za-z0-9+.-]*):)?(?://(?<authority>[^/?#]*))?(?<path>[^?#]*)(?<query>\?[^#]*)?(?<fragment>#.*)?$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Components();
}
