using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Text;

namespace Cotabby.Core.Vocabulary;

/// <summary>A usage/sense's value-owned authorship flag; generated wording never implies user ownership.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VocabularyMeaningOrigin>))]
public enum VocabularyMeaningOrigin
{
    [JsonStringEnumMemberName("captured")] Captured,
    [JsonStringEnumMemberName("ai")] Ai,
    [JsonStringEnumMemberName("user")] User,
}

/// <summary>Captured evidence completeness, retained with a usage independently of target kind/punctuation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VocabularyExampleCompleteness>))]
public enum VocabularyExampleCompleteness
{
    [JsonStringEnumMemberName("sentence")] Sentence,
    [JsonStringEnumMemberName("fragment")] Fragment,
}

/// <summary>
/// One language-specific meaning/translation variant, separate from a usage's stable UUID. The
/// language is its Mac projection ID; raw text is retained even when canonical equality matches.
/// </summary>
public sealed record VocabularyMeaning(string Text, LearningLanguage? Language)
{
    public string Id => Language?.Code() ?? "unknown";
    public bool Equals(VocabularyMeaning? other) => other is not null && Language == other.Language
        && CanonicalStringComparer.Instance.Equals(Text, other.Text);
    public override int GetHashCode() => HashCode.Combine(CanonicalStringComparer.Instance.GetHashCode(Text), Language);
}

/// <summary>
/// A frozen encountered usage from the stable Mac vocabulary model. A future sense/store owns it;
/// cards supply detached captures, never editor handles. Exact original UTF-16 ranges identify
/// repeated/disjoint occurrences; annotations, translation variants and app provenance remain
/// editable values. Lists are copied on construction and with-initialization, not shared drafts.
/// This in-memory contract defines no storage/date encoding or Mac database compatibility.
/// </summary>
public sealed record VocabularyExample
{
    private IReadOnlyList<VocabularyMeaning> _translationVariants = Array.Empty<VocabularyMeaning>();
    private IReadOnlyList<VocabularyTextRange> _ranges = Array.Empty<VocabularyTextRange>();
    private IReadOnlyList<LearningExplanationRecord> _explanations = Array.Empty<LearningExplanationRecord>();

    public VocabularyExample(Guid id, string text, string translation, IReadOnlyList<VocabularyTextRange> ranges,
        VocabularyExampleCompleteness completeness, string meaning, DateTimeOffset createdAt,
        LearningLanguage? translationLanguage = null, IReadOnlyList<VocabularyMeaning>? translationVariants = null,
        LearningLanguage? meaningLanguage = null, VocabularyMeaningOrigin meaningOrigin = VocabularyMeaningOrigin.Captured,
        string? sourceApplicationId = null, IReadOnlyList<LearningExplanationRecord>? explanations = null)
    {
        Id = id; Text = text; Translation = translation; Ranges = ranges; Completeness = completeness;
        Meaning = meaning; CreatedAt = createdAt; TranslationLanguage = translationLanguage;
        TranslationVariants = translationVariants ?? []; MeaningLanguage = meaningLanguage;
        MeaningOrigin = meaningOrigin; SourceApplicationId = sourceApplicationId; Explanations = explanations ?? [];
    }
    public Guid Id { get; }
    public string Text { get; init; }
    public string Translation { get; init; }
    public LearningLanguage? TranslationLanguage { get; init; }
    public IReadOnlyList<VocabularyMeaning> TranslationVariants
    { get => _translationVariants; init => _translationVariants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<VocabularyTextRange> Ranges
    { get => _ranges; init => _ranges = Array.AsReadOnly(value.ToArray()); }
    public VocabularyExampleCompleteness Completeness { get; init; }
    public string Meaning { get; init; }
    public LearningLanguage? MeaningLanguage { get; init; }
    public VocabularyMeaningOrigin MeaningOrigin { get; init; }
    /// <summary>Mac appBundleIdentifier counterpart; a caller supplies identity, never an AX/UIA handle.</summary>
    public string? SourceApplicationId { get; init; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<LearningExplanationRecord> Explanations
    { get => _explanations; init => _explanations = Array.AsReadOnly(value.ToArray()); }

    public VocabularyExample CopiedWithNewId() => new(Guid.NewGuid(), Text, Translation, Ranges, Completeness,
        Meaning, CreatedAt, TranslationLanguage, TranslationVariants, MeaningLanguage, MeaningOrigin, SourceApplicationId, Explanations);

    public bool Equals(VocabularyExample? other) => other is not null && Id == other.Id && Same(Text, other.Text)
        && Same(Translation, other.Translation) && TranslationLanguage == other.TranslationLanguage
        && TranslationVariants.SequenceEqual(other.TranslationVariants) && Ranges.SequenceEqual(other.Ranges)
        && Completeness == other.Completeness && Same(Meaning, other.Meaning) && MeaningLanguage == other.MeaningLanguage
        && MeaningOrigin == other.MeaningOrigin && Same(SourceApplicationId, other.SourceApplicationId)
        && CreatedAt == other.CreatedAt && Explanations.SequenceEqual(other.Explanations);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(Text, CanonicalStringComparer.Instance); hash.Add(Translation, CanonicalStringComparer.Instance);
        hash.Add(TranslationLanguage); foreach (var value in TranslationVariants) hash.Add(value);
        foreach (var value in Ranges) hash.Add(value);
        hash.Add(Completeness); hash.Add(Meaning, CanonicalStringComparer.Instance); hash.Add(MeaningLanguage); hash.Add(MeaningOrigin);
        hash.Add(SourceApplicationId is null ? 0 : CanonicalStringComparer.Instance.GetHashCode(SourceApplicationId)); hash.Add(CreatedAt);
        foreach (var value in Explanations) hash.Add(value);
        return hash.ToHashCode();
    }
    private static bool Same(string? left, string? right) => CanonicalStringComparer.Instance.Equals(left, right);
}

/// <summary>
/// Short-lived save intent from an already displayed immutable card. Explicit SenseId is supplied
/// only by notebook navigation; generated meaning never invents a destination. No live recapture,
/// file, dictionary/provider or credential is retained. A later archive commits it transactionally.
/// </summary>
public sealed record VocabularyCapture(string Expression, string DictionaryForm, LearningLanguage? Language,
    VocabularyEntryKind Kind, string Meaning = "", LearningLanguage? MeaningLanguage = null,
    VocabularyExample? Example = null, LearningExplanationRecord? Explanation = null, Guid? SenseId = null)
{
    /// <summary>Staged lookup pairs must not be replaced by a cached target-only explanation.</summary>
    public bool AllowsExplanationEnrichment { get; init; } = true;
    public bool Equals(VocabularyCapture? other) => other is not null && Language == other.Language && Kind == other.Kind
        && CanonicalStringComparer.Instance.Equals(Expression, other.Expression)
        && CanonicalStringComparer.Instance.Equals(DictionaryForm, other.DictionaryForm)
        && CanonicalStringComparer.Instance.Equals(Meaning, other.Meaning) && MeaningLanguage == other.MeaningLanguage
        && Example == other.Example && Explanation == other.Explanation && SenseId == other.SenseId
        && AllowsExplanationEnrichment == other.AllowsExplanationEnrichment;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Expression, CanonicalStringComparer.Instance); hash.Add(DictionaryForm, CanonicalStringComparer.Instance);
        hash.Add(Language); hash.Add(Kind); hash.Add(Meaning, CanonicalStringComparer.Instance); hash.Add(MeaningLanguage);
        hash.Add(Example); hash.Add(Explanation); hash.Add(SenseId);
        hash.Add(AllowsExplanationEnrichment);
        return hash.ToHashCode();
    }
}

/// <summary>Stable navigation/destination values name both identity levels; labels/order cannot substitute.</summary>
public readonly record struct VocabularySelection(Guid EntryId, Guid SenseId);
/// <summary>Detached save destination names the current sense and optional usage, never an array index.</summary>
public readonly record struct VocabularyExplanationDestination(Guid EntryId, Guid SenseId, Guid? ExampleId);
