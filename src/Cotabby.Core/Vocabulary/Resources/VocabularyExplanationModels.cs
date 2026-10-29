using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Latest acquisition evidence includes unknown boundaries, distinct from a fragment.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<LearningExplanationContext>))]
public enum LearningExplanationContext
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("sentence")] Sentence,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("fragment")] Fragment,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unknown")] Unknown,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("none")] None,
}

/// <summary>Current saved explanation target, independent of the older provider request type.
/// Unknown study language remains null; the response language lives on the record. Its local
/// occurrence receipt never authorizes a prompt/network transmission. The v7 archive owns values.</summary>
public sealed record LearningExplanationInput
{
    private IReadOnlyList<VocabularyTextRange> _ranges = Array.Empty<VocabularyTextRange>();
    public LearningExplanationInput(string target, string dictionaryForm, LearningLanguage? language,
        VocabularyEntryKind kind, string context, IReadOnlyList<VocabularyTextRange> ranges,
        LearningExplanationContext contextKind = LearningExplanationContext.Fragment, string? occurrenceIdentity = null)
    {
        Target = target; DictionaryForm = dictionaryForm; Language = language; Kind = kind;
        Context = context; Ranges = ranges; ContextKind = contextKind; OccurrenceIdentity = occurrenceIdentity;
    }
    public string Target { get; }
    public string DictionaryForm { get; }
    public LearningLanguage? Language { get; }
    public VocabularyEntryKind Kind { get; }
    public string Context { get; }
    public IReadOnlyList<VocabularyTextRange> Ranges { get => _ranges; init => _ranges = CollectionValues.List(value); }
    public LearningExplanationContext ContextKind { get; }
    public string? OccurrenceIdentity { get; }
    public bool Equals(LearningExplanationInput? other) => other is not null
        && CollectionValues.Same(Target, other.Target) && CollectionValues.Same(DictionaryForm, other.DictionaryForm)
        && Language == other.Language && Kind == other.Kind && CollectionValues.Same(Context, other.Context)
        && CollectionValues.SequenceEqual(Ranges, other.Ranges) && ContextKind == other.ContextKind
        && CollectionValues.Same(OccurrenceIdentity, other.OccurrenceIdentity);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(CollectionValues.StringHash(Target)); hash.Add(CollectionValues.StringHash(DictionaryForm));
        hash.Add(Language); hash.Add(Kind); hash.Add(CollectionValues.StringHash(Context));
        hash.Add(CollectionValues.SequenceHash(Ranges)); hash.Add(ContextKind); hash.Add(CollectionValues.StringHash(OccurrenceIdentity)); return hash.ToHashCode();
    }
}

/// <summary>A durable annotation retains the source's optional language/completeness without
/// changing the existing provider profile or manufacturing an English source-language hint.</summary>
public sealed record LearningExplanationRecord(Guid Id, LearningExplanationInput Input,
    LearningLanguage Language, LearningExplanationResult Result, string Model, DateTimeOffset CreatedAt)
{
    public bool Equals(LearningExplanationRecord? other) => other is not null && Id == other.Id && Input == other.Input
        && Language == other.Language && Result == other.Result && CollectionValues.Same(Model, other.Model) && CreatedAt == other.CreatedAt;
    public override int GetHashCode() => HashCode.Combine(Id, Input, Language, Result, CollectionValues.StringHash(Model), CreatedAt);
}
