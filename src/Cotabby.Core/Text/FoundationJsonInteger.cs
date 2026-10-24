using System.Globalization;
using System.Text.Json;

namespace Cotabby.Core.Text;

/// <summary>Foundation's observed 64-bit Codable integer rules shared by speech profiles and
/// request history. Exact integer tokens stay exact; decimal/exponent fallback keeps the source's
/// Double bounds and rounding behaviour. This parses one known number, not an entire document.</summary>
internal static class FoundationJsonInteger
{
    internal static long Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number) throw new JsonException();
        if (value.TryGetInt64(out long integer)) return integer;
        double number = value.GetDouble();
        // Foundation accepts integral decimal/exponent spellings. Its small-number fallback
        // goes through Double; at 2^53 and above we must not round a neighbouring integer into
        // another identity/version. Parse that bounded decimal value before narrowing.
        if (double.IsFinite(number) && Math.Abs(number) < 9007199254740992d && Math.Truncate(number) == number) return (long)number;
        // The reference decoder's non-integer-spelling path uses strict Double bounds.
        // MaxValue.0 rounds to +2^63; MinValue.0 is -2^63. Both are rejected, although
        // either bare integer token takes the successful exact fast path above.
        if (!double.IsFinite(number) || Math.Truncate(number) != number || number <= -9223372036854775808d || number >= 9223372036854775808d) throw new JsonException();
        // System.Decimal has fewer significant digits than Foundation Decimal and could round
        // a long fractional suffix into the next integer. Inspect the coefficient/scale instead;
        // the reference's large-number path truncates its decimal toward zero after the
        // Double range check. The source corpus records this Foundation-specific behavior.
        string raw = value.GetRawText(); int exponentIndex = raw.IndexOfAny(['e', 'E']);
        string mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex]; int exponent = 0;
        if (exponentIndex >= 0 && !int.TryParse(raw.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) throw new JsonException();
        int dot = mantissa.IndexOf('.'); long scale = (long)exponent - (dot < 0 ? 0 : mantissa.Length - dot - 1);
        bool negative = mantissa[0] == '-'; string digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('-').TrimStart('0');
        if (digits.Length == 0) return 0;
        if (scale < 0)
        {
            long discarded = -scale; if (discarded >= digits.Length) throw new JsonException();
            int retained = digits.Length - (int)discarded;
            digits = digits[..retained]; scale = 0;
        }
        if (digits.Length + scale > 19) throw new JsonException();
        ulong magnitude = ulong.Parse(digits, CultureInfo.InvariantCulture);
        for (int i = 0; i < scale; i++) magnitude *= 10;
        if (magnitude > (negative ? 9223372036854775808UL : long.MaxValue)) throw new JsonException();
        return negative ? magnitude == 9223372036854775808UL ? long.MinValue : -(long)magnitude : (long)magnitude;
    }
}
