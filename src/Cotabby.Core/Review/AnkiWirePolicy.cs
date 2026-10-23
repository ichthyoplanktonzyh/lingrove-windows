using System.Globalization;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Review;

/// <summary>Pure AnkiConnect v6 framing adapted from the original client. It owns UTF-8 wire
/// budgets and envelope classification, never a session, endpoint choice or remote write. Encoding
/// is measured before allocating the body, using Foundation escaping rather than .NET HTML escapes.</summary>
public static class AnkiWirePolicy
{
    public const int MaximumRequestBytes = 18 * 1024 * 1024;
    public const int MaximumResponseBytes = 2_000_000;
    public const string InvalidResponse = "AnkiConnect returned an invalid response.";
    public const string Rejected = "Anki rejected the request. Check the API key, fields and active profile.";
    public const string CouldNotConnect = "Could not connect to Anki. Open Anki with AnkiConnect enabled.";
    // The Mac client reuses SpeechError.responseTooLarge, including its original wording.
    public const string RequestTooLarge = "The speech audio exceeded the size limit. Try a shorter passage.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions ReaderOptions = new() { MaxDepth = 512 };

    public static byte[] Request(string action, IReadOnlyDictionary<string, AnkiValue> parameters, string apiKey)
    {
        var body = new Dictionary<string, AnkiValue>
        {
            ["action"] = new AnkiValue.Text(action), ["version"] = new AnkiValue.Number(6), ["params"] = new AnkiValue.Map(parameters),
        };
        if (apiKey.Length != 0) body["key"] = new AnkiValue.Text(apiKey);
        return EncodeBody(body);
    }
    public static byte[] EncodeBody(IReadOnlyDictionary<string, AnkiValue> body)
    {
        var value = new AnkiValue.Map(body);
        // JSONEncoder finishes encoding before it applies the byte limit. Validate the entire
        // value first, so a late NaN/bad scalar/depth failure is not misclassified as oversize.
        long count = Measure(value, 0);
        if (count > MaximumRequestBytes) throw new AnkiReviewException(RequestTooLarge);
        var builder = new StringBuilder((int)count); Append(builder, value);
        return StrictUtf8.GetBytes(builder.ToString());
    }
    public static AnkiValue Response(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumResponseBytes) throw new AnkiReviewException(InvalidResponse);
        // A syntactically malformed JSON response remains a decode exception, mapped by the
        // transport to CouldNotConnect. A decoded but wrong-shaped envelope is InvalidResponse.
        var value = JsonSerializer.Deserialize<AnkiValue>(FoundationJsonInput.Utf8Input(bytes).Span, ReaderOptions);
        var envelope = value?.Object;
        if (envelope is null || !envelope.TryGetValue("error", out var error) || !envelope.TryGetValue("result", out var result))
            throw new AnkiReviewException(InvalidResponse);
        if (error is not AnkiValue.NullValue) throw new AnkiReviewException(Rejected);
        return result;
    }
    private static long Measure(AnkiValue value, int depth)
    {
        bool container = value is AnkiValue.Map or AnkiValue.Sequence;
        if (depth >= 512 || container && depth >= 511) throw EncodingError();
        return value switch
        {
            AnkiValue.NullValue => 4, AnkiValue.Boolean boolean => boolean.Value ? 4 : 5,
            AnkiValue.Number number => Number(number.Value).Length,
            AnkiValue.Text text => QuotedCount(text.Value),
            AnkiValue.Sequence sequence => 2L + Math.Max(0, sequence.Values.Count - 1) + sequence.Values.Sum(child => Measure(child, depth + 1)),
            AnkiValue.Map map => 2L + Math.Max(0, map.Values.Count - 1) + map.Values.Sum(pair => QuotedCount(pair.Key) + 1 + Measure(pair.Value, depth + 1)),
            _ => throw EncodingError(),
        };
    }
    private static long QuotedCount(string text)
    {
        long count;
        try { count = StrictUtf8.GetByteCount(text) + 2L; }
        catch (EncoderFallbackException) { throw EncodingError(); }
        foreach (char c in text) count += c switch { '"' or '\\' or '/' or '\b' or '\f' or '\n' or '\r' or '\t' => 1, < ' ' => 5, _ => 0 };
        return count;
    }
    private static void Append(StringBuilder output, AnkiValue value)
    {
        switch (value)
        {
            case AnkiValue.NullValue: output.Append("null"); break;
            case AnkiValue.Boolean boolean: output.Append(boolean.Value ? "true" : "false"); break;
            case AnkiValue.Number number: output.Append(Number(number.Value)); break;
            case AnkiValue.Text text: output.Append(LearningExplanationText.Quote(text.Value)); break;
            case AnkiValue.Sequence sequence:
                output.Append('['); bool firstItem = true;
                foreach (var child in sequence.Values) { if (!firstItem) output.Append(','); firstItem = false; Append(output, child); }
                output.Append(']'); break;
            case AnkiValue.Map map:
                output.Append('{'); bool firstKey = true;
                foreach (var pair in map.Values)
                { if (!firstKey) output.Append(','); firstKey = false; output.Append(LearningExplanationText.Quote(pair.Key)).Append(':'); Append(output, pair.Value); }
                output.Append('}'); break;
            default: throw EncodingError();
        }
    }
    private static string Number(double value)
    {
        if (!double.IsFinite(value)) throw EncodingError();
        string text = value.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');
        // Swift's binary64 formatter uses scientific notation beyond its exact-integer range
        // (2^53), whereas .NET retains fixed notation longer. Digits remain shortest round-trip.
        if (!text.Contains('e') && Math.Abs(value) > 9_007_199_254_740_992d)
        {
            bool negative = text.StartsWith('-'); string digits = negative ? text[1..] : text;
            int exponent = digits.Length - 1; string tail = digits[1..].TrimEnd('0');
            text = (negative ? "-" : "") + digits[0] + (tail.Length == 0 ? "" : "." + tail) + "e+" + exponent.ToString(CultureInfo.InvariantCulture);
        }
        return text;
    }
    private static JsonException EncodingError() => new("Anki request cannot be encoded.");
}
