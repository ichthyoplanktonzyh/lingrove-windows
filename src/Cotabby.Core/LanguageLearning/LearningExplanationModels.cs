using System.Text.Json.Serialization;
using Cotabby.Core.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Evidence acquisition completeness, separate from whether the target is a word or sentence.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningExplanationContext>))]
public enum LearningExplanationContext
{
    [JsonStringEnumMemberName("sentence")] Sentence,
    [JsonStringEnumMemberName("fragment")] Fragment,
    [JsonStringEnumMemberName("none")] None,
}

/// <summary>
/// Frozen study material, copied from a future card/notebook owner into pure explanation policy.
/// It retains original strings and ordered ranges, never an editor, dictionary or runtime. The
/// local occurrence receipt distinguishes equal saved usages and is deliberately absent from prompts.
/// </summary>
public sealed record LearningExplanationInput
{
    [JsonConstructor]
    public LearningExplanationInput(string target, string dictionaryForm, LearningLanguage language,
        VocabularyEntryKind kind, string context, IReadOnlyList<VocabularyTextRange> ranges,
        LearningExplanationContext contextKind = LearningExplanationContext.Fragment, string? occurrenceIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(dictionaryForm);
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(ranges);
        Target = target; DictionaryForm = dictionaryForm; Language = language; Kind = kind; Context = context;
        Ranges = Array.AsReadOnly(ranges.ToArray()); ContextKind = contextKind; OccurrenceIdentity = occurrenceIdentity;
    }
    [JsonPropertyName("target")] public string Target { get; }
    [JsonPropertyName("dictionaryForm")] public string DictionaryForm { get; }
    [JsonPropertyName("language")] public LearningLanguage Language { get; }
    [JsonPropertyName("kind")] public VocabularyEntryKind Kind { get; }
    [JsonPropertyName("context")] public string Context { get; }
    [JsonPropertyName("ranges")] public IReadOnlyList<VocabularyTextRange> Ranges { get; }
    [JsonPropertyName("contextKind")] public LearningExplanationContext ContextKind { get; }
    [JsonPropertyName("occurrenceIdentity")] public string? OccurrenceIdentity { get; }

    public bool Equals(LearningExplanationInput? other) => other is not null
        && Same(Target, other.Target) && Same(DictionaryForm, other.DictionaryForm) && Language == other.Language
        && Kind == other.Kind && Same(Context, other.Context) && Ranges.SequenceEqual(other.Ranges)
        && ContextKind == other.ContextKind && Same(OccurrenceIdentity, other.OccurrenceIdentity);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Target, CanonicalStringComparer.Instance); hash.Add(DictionaryForm, CanonicalStringComparer.Instance);
        hash.Add(Language); hash.Add(Kind); hash.Add(Context, CanonicalStringComparer.Instance);
        foreach (var range in Ranges) hash.Add(range);
        hash.Add(ContextKind); hash.Add(OccurrenceIdentity is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(OccurrenceIdentity));
        return hash.ToHashCode();
    }
    private static bool Same(string? left, string? right) => CanonicalStringComparer.Instance.Equals(left, right);
}

/// <summary>
/// A complete validated model response. Meaning is independently adoptable; explanation remains
/// generated teaching prose, never an attributed dictionary quotation or a user-authored meaning.
/// </summary>
public sealed record LearningExplanationResult(
    [property: JsonPropertyName("meaning")] string Meaning,
    [property: JsonPropertyName("explanation")] string Explanation)
{
    public bool Equals(LearningExplanationResult? other) => other is not null
        && CanonicalStringComparer.Instance.Equals(Meaning, other.Meaning)
        && CanonicalStringComparer.Instance.Equals(Explanation, other.Explanation);
    public override int GetHashCode() => HashCode.Combine(
        CanonicalStringComparer.Instance.GetHashCode(Meaning), CanonicalStringComparer.Instance.GetHashCode(Explanation));
}
