using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyExampleCompleteness>))]
public enum VocabularyExampleCompleteness
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("sentence")] Sentence,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("fragment")] Fragment,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unknown")] Unknown,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularySourceKind>))]
public enum VocabularySourceKind
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unspecified")] Unspecified,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("captured")] Captured,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("authored")] Authored,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("generated")] Generated,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("imported")] Imported,
}

/// <summary>Latest lexical/joined headword value. Canonical senses contain no personal state;
/// NotebookProjection borrows this same shape for editor reads. Unknown language remains null.</summary>
public sealed record VocabularyEntry
{
    private IReadOnlyList<VocabularySense> _senses = Array.Empty<VocabularySense>();
    public VocabularyEntry(Guid id, string expression, string dictionaryForm, LearningLanguage? language, VocabularyEntryKind kind, IReadOnlyList<VocabularySense> senses, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    { Id = id; Expression = expression; DictionaryForm = dictionaryForm; Language = language; Kind = kind; Senses = senses; CreatedAt = createdAt; UpdatedAt = updatedAt; }
    public Guid Id { get; }
    public string Expression { get; init; }
    public string DictionaryForm { get; init; }
    public LearningLanguage? Language { get; }
    public VocabularyEntryKind Kind { get; }
    public IReadOnlyList<VocabularySense> Senses { get => _senses; init => _senses = CollectionValues.List(value); }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<VocabularyExample> Examples => CollectionValues.List(Senses.SelectMany(sense => sense.Examples));
    public IReadOnlyList<LearningExplanationRecord>? Explanations
    {
        get
        {
            var seen = new HashSet<Guid>();
            var records = Senses.SelectMany(sense => sense.Explanations.Concat(sense.Examples.SelectMany(example => example.Explanations)))
                .Where(record => seen.Add(record.Id)).ToArray();
            return records.Length == 0 ? null : CollectionValues.List(records);
        }
    }
    public bool Equals(VocabularyEntry? other) => other is not null
        && Id == other.Id
        && CollectionValues.Same(Expression, other.Expression)
        && CollectionValues.Same(DictionaryForm, other.DictionaryForm)
        && Language == other.Language
        && Kind == other.Kind
        && CollectionValues.SequenceEqual(Senses, other.Senses)
        && CreatedAt == other.CreatedAt
        && UpdatedAt == other.UpdatedAt;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(CollectionValues.StringHash(Expression));
        hash.Add(CollectionValues.StringHash(DictionaryForm));
        hash.Add(Language);
        hash.Add(Kind);
        hash.Add(CollectionValues.SequenceHash(Senses));
        hash.Add(CreatedAt);
        hash.Add(UpdatedAt);
        return hash.ToHashCode();
    }
}
/// <summary>One stable lexical meaning, or its joined notebook projection. Collections own the
/// projected notes/cards and accepted interpretations supply examples. Owned lists are detached.</summary>
public sealed record VocabularySense
{
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private IReadOnlyList<VocabularyExample> _examples = Array.Empty<VocabularyExample>();
    private IReadOnlyList<VocabularyMeaning> _standaloneMeanings = Array.Empty<VocabularyMeaning>();
    private IReadOnlyList<LearningExplanationRecord> _explanations = Array.Empty<LearningExplanationRecord>();
    private IReadOnlyList<VocabularyMeaning> _alternateMeanings = Array.Empty<VocabularyMeaning>();
    private IReadOnlyList<Guid> _mergedFrom = Array.Empty<Guid>();
    private IReadOnlyList<VocabularyReview>? _reviewCards = null;
    private IReadOnlyList<VocabularyContentBlock>? _content = null;
    public VocabularySense(Guid id, string meaning, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    { Id = id; Meaning = meaning; CreatedAt = createdAt; UpdatedAt = updatedAt; }
    public Guid Id { get; }
    public string Meaning { get; init; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; init; }
    public LearningLanguage? MeaningLanguage { get; init; }
    public VocabularyMeaningOrigin MeaningOrigin { get; init; } = VocabularyMeaningOrigin.Captured;
    public string Note { get; init; } = "";
    public IReadOnlyList<string> Tags { get => _tags; init => _tags = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyExample> Examples { get => _examples; init => _examples = CollectionValues.List(value); }
    public bool IsSavedWithoutContext { get; init; }
    public IReadOnlyList<VocabularyMeaning> StandaloneMeanings { get => _standaloneMeanings; init => _standaloneMeanings = CollectionValues.List(value); }
    public IReadOnlyList<LearningExplanationRecord> Explanations { get => _explanations; init => _explanations = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyMeaning> AlternateMeanings { get => _alternateMeanings; init => _alternateMeanings = CollectionValues.List(value); }
    public IReadOnlyList<Guid> MergedFrom { get => _mergedFrom; init => _mergedFrom = CollectionValues.List(value); }
    public VocabularyReview? Review { get; init; }
    public IReadOnlyList<VocabularyReview>? ReviewCards { get => _reviewCards; init => _reviewCards = CollectionValues.OptionalList(value); }
    public IReadOnlyList<VocabularyContentBlock>? Content { get => _content; init => _content = CollectionValues.OptionalList(value); }
    public bool? CreatedWithoutContext { get; init; }
    public bool SupportsGeneralExplanation => IsSavedWithoutContext || CreatedWithoutContext == true;
    public bool Equals(VocabularySense? other) => other is not null
        && Id == other.Id
        && CollectionValues.Same(Meaning, other.Meaning)
        && CreatedAt == other.CreatedAt
        && UpdatedAt == other.UpdatedAt
        && MeaningLanguage == other.MeaningLanguage
        && MeaningOrigin == other.MeaningOrigin
        && CollectionValues.Same(Note, other.Note)
        && CollectionValues.SequenceEqual(Tags, other.Tags)
        && CollectionValues.SequenceEqual(Examples, other.Examples)
        && IsSavedWithoutContext == other.IsSavedWithoutContext
        && CollectionValues.SequenceEqual(StandaloneMeanings, other.StandaloneMeanings)
        && CollectionValues.SequenceEqual(Explanations, other.Explanations)
        && CollectionValues.SequenceEqual(AlternateMeanings, other.AlternateMeanings)
        && CollectionValues.SequenceEqual(MergedFrom, other.MergedFrom)
        && Review == other.Review
        && CollectionValues.SequenceEqual(ReviewCards, other.ReviewCards)
        && CollectionValues.SequenceEqual(Content, other.Content)
        && CreatedWithoutContext == other.CreatedWithoutContext;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(CollectionValues.StringHash(Meaning));
        hash.Add(CreatedAt);
        hash.Add(UpdatedAt);
        hash.Add(MeaningLanguage);
        hash.Add(MeaningOrigin);
        hash.Add(CollectionValues.StringHash(Note));
        hash.Add(CollectionValues.SequenceHash(Tags));
        hash.Add(CollectionValues.SequenceHash(Examples));
        hash.Add(IsSavedWithoutContext);
        hash.Add(CollectionValues.SequenceHash(StandaloneMeanings));
        hash.Add(CollectionValues.SequenceHash(Explanations));
        hash.Add(CollectionValues.SequenceHash(AlternateMeanings));
        hash.Add(CollectionValues.SequenceHash(MergedFrom));
        hash.Add(Review);
        hash.Add(CollectionValues.SequenceHash(ReviewCards));
        hash.Add(CollectionValues.SequenceHash(Content));
        hash.Add(CreatedWithoutContext);
        return hash.ToHashCode();
    }
}
/// <summary>Joined example: Id names the interpretation; source and occurrence retain independent
/// evidence identity. This value never owns an editor handle or mutable source snapshot.</summary>
public sealed record VocabularyExample
{
    private IReadOnlyList<VocabularyTextRange> _ranges = Array.Empty<VocabularyTextRange>();
    private IReadOnlyList<VocabularyMeaning> _translationVariants = Array.Empty<VocabularyMeaning>();
    private IReadOnlyList<LearningExplanationRecord> _explanations = Array.Empty<LearningExplanationRecord>();
    public VocabularyExample(Guid id, string text, string translation, IReadOnlyList<VocabularyTextRange> ranges, VocabularyExampleCompleteness completeness, string meaning, DateTimeOffset createdAt)
    { Id = id; Text = text; Translation = translation; Ranges = ranges; Completeness = completeness; Meaning = meaning; CreatedAt = createdAt; }
    public Guid Id { get; }
    public string Text { get; init; }
    public string Translation { get; init; }
    public IReadOnlyList<VocabularyTextRange> Ranges { get => _ranges; init => _ranges = CollectionValues.List(value); }
    public VocabularyExampleCompleteness Completeness { get; init; }
    public string Meaning { get; init; }
    public DateTimeOffset CreatedAt { get; }
    public Guid SourceId { get; init; } = Guid.NewGuid();
    public Guid? OccurrenceId { get; init; }
    public VocabularySourceKind SourceKind { get; init; } = VocabularySourceKind.Unspecified;
    public LearningLanguage? TranslationLanguage { get; init; }
    public IReadOnlyList<VocabularyMeaning> TranslationVariants { get => _translationVariants; init => _translationVariants = CollectionValues.List(value); }
    public LearningLanguage? MeaningLanguage { get; init; }
    public VocabularyMeaningOrigin MeaningOrigin { get; init; } = VocabularyMeaningOrigin.Captured;
    public string? SourceApplicationId { get; init; }
    public IReadOnlyList<LearningExplanationRecord> Explanations { get => _explanations; init => _explanations = CollectionValues.List(value); }
    /// <summary>A copied annotation retains source identity, but gets a new claim/occurrence identity.</summary>
    public VocabularyExample CopiedWithNewId() => new(Guid.NewGuid(), Text, Translation, Ranges, Completeness, Meaning, CreatedAt)
    {
        SourceId = SourceId, SourceKind = SourceKind, TranslationLanguage = TranslationLanguage,
        TranslationVariants = TranslationVariants, MeaningLanguage = MeaningLanguage, MeaningOrigin = MeaningOrigin,
        SourceApplicationId = SourceApplicationId, Explanations = Explanations,
    };
    public bool Equals(VocabularyExample? other) => other is not null
        && Id == other.Id
        && CollectionValues.Same(Text, other.Text)
        && CollectionValues.Same(Translation, other.Translation)
        && CollectionValues.SequenceEqual(Ranges, other.Ranges)
        && Completeness == other.Completeness
        && CollectionValues.Same(Meaning, other.Meaning)
        && CreatedAt == other.CreatedAt
        && SourceId == other.SourceId
        && OccurrenceId == other.OccurrenceId
        && SourceKind == other.SourceKind
        && TranslationLanguage == other.TranslationLanguage
        && CollectionValues.SequenceEqual(TranslationVariants, other.TranslationVariants)
        && MeaningLanguage == other.MeaningLanguage
        && MeaningOrigin == other.MeaningOrigin
        && CollectionValues.Same(SourceApplicationId, other.SourceApplicationId)
        && CollectionValues.SequenceEqual(Explanations, other.Explanations);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(CollectionValues.StringHash(Text));
        hash.Add(CollectionValues.StringHash(Translation));
        hash.Add(CollectionValues.SequenceHash(Ranges));
        hash.Add(Completeness);
        hash.Add(CollectionValues.StringHash(Meaning));
        hash.Add(CreatedAt);
        hash.Add(SourceId);
        hash.Add(OccurrenceId);
        hash.Add(SourceKind);
        hash.Add(TranslationLanguage);
        hash.Add(CollectionValues.SequenceHash(TranslationVariants));
        hash.Add(MeaningLanguage);
        hash.Add(MeaningOrigin);
        hash.Add(CollectionValues.StringHash(SourceApplicationId));
        hash.Add(CollectionValues.SequenceHash(Explanations));
        return hash.ToHashCode();
    }
}
/// <summary>Immutable original evidence, retained independently of lexical/collection lifetime.
/// The archive snapshot owns it; corrections fork this value rather than rewriting other claims.</summary>
public sealed record VocabularySourceSnapshot
{
    public VocabularySourceSnapshot(Guid id, string text, VocabularySourceKind kind, string? sourceApplicationId, DateTimeOffset encounteredAt)
    { Id = id; Text = text; Kind = kind; SourceApplicationId = sourceApplicationId; EncounteredAt = encounteredAt; }
    public Guid Id { get; }
    public string Text { get; }
    public VocabularySourceKind Kind { get; }
    public string? SourceApplicationId { get; }
    public DateTimeOffset EncounteredAt { get; }
    public bool Equals(VocabularySourceSnapshot? other) => other is not null
        && Id == other.Id
        && CollectionValues.Same(Text, other.Text)
        && Kind == other.Kind
        && CollectionValues.Same(SourceApplicationId, other.SourceApplicationId)
        && EncounteredAt == other.EncounteredAt;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(CollectionValues.StringHash(Text));
        hash.Add(Kind);
        hash.Add(CollectionValues.StringHash(SourceApplicationId));
        hash.Add(EncounteredAt);
        return hash.ToHashCode();
    }
}
/// <summary>Immutable UTF-16 spans into one source. Interpretations borrow this identity;
/// moving a claim between senses never changes the original characters or ranges.</summary>
public sealed record VocabularySourceOccurrence
{
    private IReadOnlyList<VocabularyTextRange> _ranges = Array.Empty<VocabularyTextRange>();
    public VocabularySourceOccurrence(Guid id, Guid sourceId, IReadOnlyList<VocabularyTextRange> ranges)
    { Id = id; SourceId = sourceId; Ranges = ranges; }
    public Guid Id { get; }
    public Guid SourceId { get; }
    public IReadOnlyList<VocabularyTextRange> Ranges { get => _ranges; init => _ranges = CollectionValues.List(value); }
    public bool Equals(VocabularySourceOccurrence? other) => other is not null
        && Id == other.Id
        && SourceId == other.SourceId
        && CollectionValues.SequenceEqual(Ranges, other.Ranges);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(SourceId);
        hash.Add(CollectionValues.SequenceHash(Ranges));
        return hash.ToHashCode();
    }
}
/// <summary>Version-seven canonical resource graph. It is a detached value boundary for the next
/// archive adapter, not another runtime Store. The older namespace retains version-three evidence
/// until an explicit durable upgrade; this type is not accepted by that older SQL/backup codec.</summary>
public sealed record VocabularyDocument
{
    private IReadOnlyList<VocabularyEntry> _catalog = Array.Empty<VocabularyEntry>();
    private IReadOnlyList<VocabularySourceSnapshot> _sources = Array.Empty<VocabularySourceSnapshot>();
    private IReadOnlyList<VocabularySourceOccurrence> _occurrences = Array.Empty<VocabularySourceOccurrence>();
    private IReadOnlyList<VocabularyInterpretation> _interpretations = Array.Empty<VocabularyInterpretation>();
    private IReadOnlyList<VocabularyCollectionItem> _collections = Array.Empty<VocabularyCollectionItem>();
    private IReadOnlyList<ReviewLayoutTemplate>? _reviewTemplates = null;
    public long SchemaVersion { get; init; } = 7;
    public IReadOnlyList<VocabularyEntry> Catalog { get => _catalog; init => _catalog = CollectionValues.List(value); }
    public IReadOnlyList<VocabularySourceSnapshot> Sources { get => _sources; init => _sources = CollectionValues.List(value); }
    public IReadOnlyList<VocabularySourceOccurrence> Occurrences { get => _occurrences; init => _occurrences = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyInterpretation> Interpretations { get => _interpretations; init => _interpretations = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyCollectionItem> Collections { get => _collections; init => _collections = CollectionValues.List(value); }
    public IReadOnlyList<ReviewLayoutTemplate>? ReviewTemplates { get => _reviewTemplates; init => _reviewTemplates = CollectionValues.OptionalList(value); }
    public VocabularySemanticIndex Semantics { get; init; } = new();
    public const long CurrentVersion = 7;
    public IReadOnlyList<VocabularyEntry> Entries => VocabularyNotebookProjection.Entries(this, collectedOnly: false);
    public IReadOnlyList<VocabularyEntry> CollectedEntries => VocabularyNotebookProjection.Entries(this, collectedOnly: true);
    public VocabularyDocument WithEntries(IReadOnlyList<VocabularyEntry> entries) => VocabularyNotebookProjection.Replace(entries, this);
    public static VocabularyDocument FromEntries(IReadOnlyList<VocabularyEntry> entries) => new VocabularyDocument().WithEntries(entries);
    public bool Equals(VocabularyDocument? other) => other is not null
        && SchemaVersion == other.SchemaVersion
        && CollectionValues.SequenceEqual(Catalog, other.Catalog)
        && CollectionValues.SequenceEqual(Sources, other.Sources)
        && CollectionValues.SequenceEqual(Occurrences, other.Occurrences)
        && CollectionValues.SequenceEqual(Interpretations, other.Interpretations)
        && CollectionValues.SequenceEqual(Collections, other.Collections)
        && CollectionValues.SequenceEqual(ReviewTemplates, other.ReviewTemplates)
        && Semantics == other.Semantics;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(CollectionValues.SequenceHash(Catalog));
        hash.Add(CollectionValues.SequenceHash(Sources));
        hash.Add(CollectionValues.SequenceHash(Occurrences));
        hash.Add(CollectionValues.SequenceHash(Interpretations));
        hash.Add(CollectionValues.SequenceHash(Collections));
        hash.Add(CollectionValues.SequenceHash(ReviewTemplates));
        hash.Add(Semantics);
        return hash.ToHashCode();
    }
}
