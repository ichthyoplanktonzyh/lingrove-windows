using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cotabby.Core.LanguageLearning;

/// <summary>The phrase-level register a translation may use without changing its target text.</summary>
[JsonConverter(typeof(LearningTranslationRegisterJsonConverter))]
public enum LearningTranslationRegister
{
    Natural,
    Formal,
    Conversational,
    Custom,
}

/// <summary>Serializes style registers through the same durable identifiers as the macOS settings.</summary>
public sealed class LearningTranslationRegisterJsonConverter : JsonConverter<LearningTranslationRegister>
{
    public override LearningTranslationRegister Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "natural" => LearningTranslationRegister.Natural,
            "formal" => LearningTranslationRegister.Formal,
            "conversational" => LearningTranslationRegister.Conversational,
            "custom" => LearningTranslationRegister.Custom,
            _ => throw new JsonException("The translation style register is not supported."),
        };

    public override void Write(Utf8JsonWriter writer, LearningTranslationRegister value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            LearningTranslationRegister.Natural => "natural",
            LearningTranslationRegister.Formal => "formal",
            LearningTranslationRegister.Conversational => "conversational",
            LearningTranslationRegister.Custom => "custom",
            _ => throw new JsonException("The translation style register is not supported."),
        });
}

/// <summary>
/// A destination's expression preference. Settings own persistence; each request takes an
/// effective immutable copy so later edits cannot change an in-flight prompt.
/// </summary>
public sealed record LearningTranslationStyle(
    [property: JsonPropertyName("register")]
    LearningTranslationRegister Register = LearningTranslationRegister.Natural,
    [property: JsonPropertyName("customInstruction")]
    string CustomInstruction = "")
{
    /// <summary>
    /// Bounds the custom preference exactly as the macOS value does: first 4,000 UTF-8 bytes (a
    /// cut scalar decodes to U+FFFD), then at most 1,000 grapheme clusters.
    /// </summary>
    [JsonIgnore]
    public LearningTranslationStyle Normalized => new(Register,
        TakeTextElements(DecodeUtf8Prefix(CustomInstruction ?? string.Empty, MaximumInstructionBytes), MaximumInstructionGraphemes));

    /// <summary>Non-custom registers discard dormant custom text from the frozen request.</summary>
    [JsonIgnore]
    public LearningTranslationStyle Effective => Register == LearningTranslationRegister.Custom
        ? Normalized
        : new LearningTranslationStyle(Register);

    /// <summary>
    /// The bounded user-authored preference channel, or null when no instruction should be added.
    /// It remains separate from literal source text and always precedes fixed translation rules.
    /// </summary>
    [JsonIgnore]
    public string? Instruction => Register switch
    {
        LearningTranslationRegister.Natural => null,
        LearningTranslationRegister.Formal => "Use a formal written register suitable for documents.",
        LearningTranslationRegister.Conversational => "Use natural everyday conversational phrasing.",
        LearningTranslationRegister.Custom => Normalized.CustomInstruction.Trim() is { Length: > 0 } instruction
            ? instruction
            : null,
        _ => throw new ArgumentOutOfRangeException(nameof(Register), Register, "Unknown translation register."),
    };

    private const int MaximumInstructionBytes = 4_000;
    private const int MaximumInstructionGraphemes = 1_000;

    private static string DecodeUtf8Prefix(string value, int maximumBytes)
    {
        // Encode one Unicode scalar at a time so an enormous saved instruction never needs a second
        // unbounded byte array. Copying a partial final scalar intentionally mirrors Swift's
        // String(decoding:prefix, as: UTF8.self), which replaces that incomplete scalar.
        var bytes = new byte[maximumBytes];
        var written = 0;
        Span<byte> encodedRune = stackalloc byte[4];
        foreach (var rune in value.EnumerateRunes())
        {
            var runeLength = rune.EncodeToUtf8(encodedRune);
            var copyLength = Math.Min(runeLength, maximumBytes - written);
            encodedRune[..copyLength].CopyTo(bytes.AsSpan(written));
            written += copyLength;
            if (copyLength != runeLength || written == maximumBytes) break;
        }

        return Encoding.UTF8.GetString(bytes, 0, written);
    }

    private static string TakeTextElements(string value, int maximumElements)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        var end = 0;
        var count = 0;
        while (count < maximumElements && enumerator.MoveNext())
        {
            end = enumerator.ElementIndex + ((string)enumerator.GetTextElement()).Length;
            count++;
        }

        return value[..end];
    }
}
