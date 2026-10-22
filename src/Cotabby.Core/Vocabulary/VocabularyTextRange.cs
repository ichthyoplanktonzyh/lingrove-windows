using System.Text.Json.Serialization;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Target granularity from the Mac vocabulary contract, independent of evidence completeness.
/// This value introduces no notebook store, sense identity or persistence service.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VocabularyEntryKind>))]
public enum VocabularyEntryKind
{
    [JsonStringEnumMemberName("word")] Word,
    [JsonStringEnumMemberName("expression")] Expression,
    [JsonStringEnumMemberName("pattern")] Pattern,
    [JsonStringEnumMemberName("sentence")] Sentence,
}

/// <summary>
/// One original UTF-16 occurrence span, owned by an immutable learning capture. A list preserves
/// repeated or discontinuous pattern parts without searching the current editor for matching text.
/// </summary>
public readonly record struct VocabularyTextRange(
    [property: JsonPropertyName("location")] int Location,
    [property: JsonPropertyName("length")] int Length);
