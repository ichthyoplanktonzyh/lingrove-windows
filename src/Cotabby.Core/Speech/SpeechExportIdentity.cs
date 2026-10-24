using System.Globalization;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Speech;

/// <summary>The controller fingerprints the exact original sorted-key JSON bytes, not a new
/// storage schema or a transport request. Optional profile fields are omitted; strings retain
/// Unicode and Foundation's slash escaping. Text/paths/voice values are hashed locally and never
/// logged or sent by this helper. Credentials have no input position in either envelope.</summary>
public static class SpeechExportIdentity
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static byte[] Strings(IEnumerable<string> values) => Utf8.GetBytes("[" + string.Join(",", values.Select(Quote)) + "]");
    public static byte[] Profile(SpeechGenerationProfile profile)
    {
        var fields = new List<(string Key, string Value)>();
        if (profile.EndpointVoice is { } voice) fields.Add(("endpointVoice", Quote(voice)));
        if (profile.ReferenceAudioPath is { } reference) fields.Add(("referenceAudioPath", Quote(reference)));
        var sampling = profile.Sampling;
        var sampler = new List<(string Key, string Value)> { ("fixedSeed", sampling.FixedSeed.ToString(CultureInfo.InvariantCulture)) };
        if (sampling.RepetitionPenalty is { } repetition) sampler.Add(("repetitionPenalty", Number(repetition)));
        sampler.Add(("seedMode", Quote(sampling.SeedMode switch { LearningModelSeedMode.Recommended => "recommended", LearningModelSeedMode.Random => "random", LearningModelSeedMode.Fixed => "fixed", _ => throw new JsonException() })));
        if (sampling.Temperature is { } temperature) sampler.Add(("temperature", Number(temperature)));
        if (sampling.TopK is { } topK) sampler.Add(("topK", topK.ToString(CultureInfo.InvariantCulture)));
        if (sampling.TopP is { } topP) sampler.Add(("topP", Number(topP)));
        fields.Add(("sampling", Object(sampler))); return Utf8.GetBytes(Object(fields));
    }
    private static string Object(IEnumerable<(string Key, string Value)> fields) => "{" + string.Join(",", fields.Select(field => Quote(field.Key) + ":" + field.Value)) + "}";
    private static string Quote(string value)
    {
        var output = new StringBuilder("\"");
        foreach (char character in value) output.Append(character switch
        {
            '"' => "\\\"", '\\' => "\\\\", '/' => "\\/", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
            < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture), _ => character.ToString(),
        });
        return output.Append('"').ToString();
    }
    private static string Number(double value)
    {
        if (!double.IsFinite(value)) throw new JsonException("Non-finite speech sampling value.");
        string text = value.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        if (text.Contains('e')) return text;
        string sign = text.StartsWith('-') ? "-" : "", magnitude = text.TrimStart('-'); int dot = magnitude.IndexOf('.');
        int whole = dot < 0 ? magnitude.Length : dot;
        // Foundation's shortest Double writer switches large integral magnitudes to scientific
        // form at exponent16. .NET's round-trip writer can retain the whole decimal there.
        if (whole <= 16) return text;
        string digits = magnitude.Replace(".", "", StringComparison.Ordinal).TrimEnd('0');
        return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e+" + (whole - 1).ToString("D2", CultureInfo.InvariantCulture);
    }
}
