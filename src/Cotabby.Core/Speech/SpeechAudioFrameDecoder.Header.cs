using System.Globalization;
using System.Text;
using System.Text.Json;
using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>Foundation-shaped header decoding belongs beside framing, not in the transport or
/// player. It preserves first-key/snake-case/optional-scalar rules while never retaining raw JSON
/// in an exception. Unknown values are skipped as in Decodable, including undecoded strings.</summary>
public sealed partial class SpeechAudioFrameDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static SpeechAudioFrameHeader ReadHeader(ReadOnlyMemory<byte> input)
    {
        try
        {
            // Foundation only decodes known string values. Eagerly validating every UTF-8 byte
            // would reject an ignored future string that the original Decodable never reads.
            var bytes = FoundationJsonInput.Utf8Input(input, validateUtf8: false);
            var reader = new Utf8JsonReader(bytes.Span, new JsonReaderOptions { MaxDepth = 512 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw Decode();
            string? @event = null, id = null, format = null; bool? ok = null;
            long? version = null, sequence = null, rate = null, samples = null, payload = null, channels = null;
            var seen = new HashSet<string>(CanonicalStringComparer.Instance);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw Decode();
                string name = SnakeKey(ReadString(ref reader));
                if (!reader.Read()) throw Decode();
                // Foundation keeps the first converted key. A duplicate's scalar type/value
                // must not replace or invalidate the first decoded property.
                if (!seen.Add(name)) { reader.Skip(); continue; }
                switch (name)
                {
                    case "event": @event = ReadString(ref reader); break;
                    case "id": id = OptionalString(ref reader); break;
                    case "format": format = OptionalString(ref reader); break;
                    case "protocolVersion": version = Integer(ref reader); break;
                    case "sequence": sequence = Integer(ref reader); break;
                    case "sampleRate": rate = Integer(ref reader); break;
                    case "samples": samples = Integer(ref reader); break;
                    case "payloadBytes": payload = Integer(ref reader); break;
                    case "channels": channels = Integer(ref reader); break;
                    case "ok": ok = reader.TokenType == JsonTokenType.Null ? null : reader.TokenType is JsonTokenType.True or JsonTokenType.False ? reader.GetBoolean() : throw Decode(); break;
                    default: reader.Skip(); break;
                }
            }
            if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || @event is null) throw Decode();
            return new(@event, version, id, sequence, rate, samples, payload, ok, format, channels);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException or FormatException)
        { throw Decode(); }
    }
    private static string ReadString(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) throw Decode();
        _ = StrictUtf8.GetCharCount(reader.ValueSpan); return reader.GetString()!;
    }
    private static string? OptionalString(ref Utf8JsonReader reader) => reader.TokenType == JsonTokenType.Null ? null : ReadString(ref reader);
    private static long? Integer(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.Number) throw Decode();
        if (reader.TryGetInt64(out long exact)) return exact;
        if (!reader.TryGetDouble(out double value) || !double.IsFinite(value) || Math.Truncate(value) != value) throw Decode();
        if (Math.Abs(value) < 9_007_199_254_740_992d) return (long)value;
        if (value < -9_223_372_036_854_775_808d || value >= 9_223_372_036_854_775_808d) throw Decode();
        // At 2^53 Foundation's Decimal path preserves integer digits that Double would round.
        // The actual header oracle also records truncation of large decimal fractions. Inspect
        // coefficient/scale directly: System.Decimal could round a long suffix into another ID.
        string raw = Encoding.UTF8.GetString(reader.ValueSpan); int exponentIndex = raw.IndexOfAny(['e', 'E']);
        string mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex]; int exponent = 0;
        if (exponentIndex >= 0 && !int.TryParse(raw.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) throw Decode();
        int dot = mantissa.IndexOf('.'); long scale = (long)exponent - (dot < 0 ? 0 : mantissa.Length - dot - 1);
        bool negative = mantissa[0] == '-'; string digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('-').TrimStart('0');
        if (digits.Length == 0) return 0;
        bool fraction = false;
        if (scale < 0)
        {
            long discarded = -scale; if (discarded >= digits.Length) throw Decode();
            int retained = digits.Length - (int)discarded;
            fraction = digits[retained..].Any(c => c != '0'); digits = digits[..retained]; scale = 0;
        }
        if (digits.Length + scale > 19) throw Decode();
        ulong magnitude = ulong.Parse(digits, CultureInfo.InvariantCulture);
        for (int i = 0; i < scale; i++) magnitude *= 10;
        if (magnitude > (negative ? 9_223_372_036_854_775_808UL : long.MaxValue)) throw Decode();
        long result = negative ? magnitude == 9_223_372_036_854_775_808UL ? long.MinValue : -(long)magnitude : (long)magnitude;
        // Non-integer spellings at the negative boundary differ from a bare Int64.min token:
        // -2^63.0 is rejected, while -(2^63-1).0 survives exactly. A rounded fraction at that
        // boundary is rejected too. Do not replace these captured rules with an inclusive cast.
        if (value == -9_223_372_036_854_775_808d && (fraction || result == long.MinValue)) throw Decode();
        return result;
    }
    private static string SnakeKey(string value)
    {
        if (!value.Contains('_')) return value;
        int first = 0, last = value.Length; while (first < last && value[first] == '_') first++; while (last > first && value[last - 1] == '_') last--;
        string[] parts = value[first..last].Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return value;
        return value[..first] + parts[0].ToLowerInvariant() + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant())) + value[last..];
    }
    private static JsonException Decode() => new("Invalid speech frame header.");
}
