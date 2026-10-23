using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Notebook-owned headword groups stable senses without flattening their editable ownership.
/// The read-only projections support lookup/search; they never create another owner of a usage.
/// No storage, import format or cross-platform date encoding is defined by these in-memory values.
/// </summary>
public sealed record VocabularyEntry
{
    private IReadOnlyList<VocabularySense> _senses = Array.Empty<VocabularySense>();
    public VocabularyEntry(Guid id, string expression, string dictionaryForm, LearningLanguage language,
        VocabularyEntryKind kind, IReadOnlyList<VocabularySense> senses, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id; Expression = expression; DictionaryForm = dictionaryForm; Language = language; Kind = kind;
        Senses = senses; CreatedAt = createdAt; UpdatedAt = updatedAt;
    }
    public Guid Id { get; }
    public string Expression { get; init; }
    public string DictionaryForm { get; init; }
    public LearningLanguage Language { get; }
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
            var result = Senses.SelectMany(sense => sense.Explanations.Concat(sense.Examples.SelectMany(example => example.Explanations)))
                .Where(record => seen.Add(record.Id)).ToArray();
            return result.Length == 0 ? null : Array.AsReadOnly(result);
        }
    }
    public bool Equals(VocabularyEntry? other) => other is not null && Id == other.Id && CollectionValues.Same(Expression, other.Expression)
        && CollectionValues.Same(DictionaryForm, other.DictionaryForm) && Language == other.Language && Kind == other.Kind
        && CollectionValues.SequenceEqual(Senses, other.Senses) && CreatedAt == other.CreatedAt && UpdatedAt == other.UpdatedAt;
    public override int GetHashCode() => HashCode.Combine(Id, CollectionValues.StringHash(Expression), CollectionValues.StringHash(DictionaryForm),
        Language, Kind, CollectionValues.SequenceHash(Senses), CreatedAt, UpdatedAt);
}

/// <summary>
/// One stable meaning identity owns notes, usages, authored content and independent review cards.
/// Rewording never replaces Id; merged-from IDs remain aliases. Legacy Review and optional flexible
/// ReviewCards coexist exactly as on Mac, without implicit migration or nil-to-empty normalization.
/// A future archive transaction replaces this immutable snapshot after validation/conflict checks.
/// </summary>
public sealed record VocabularySense
{
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private IReadOnlyList<VocabularyExample> _examples = Array.Empty<VocabularyExample>();
    private IReadOnlyList<VocabularyMeaning> _standaloneMeanings = Array.Empty<VocabularyMeaning>(), _alternateMeanings = Array.Empty<VocabularyMeaning>();
    private IReadOnlyList<LearningExplanationRecord> _explanations = Array.Empty<LearningExplanationRecord>();
    private IReadOnlyList<Guid> _mergedFrom = Array.Empty<Guid>();
    private IReadOnlyList<VocabularyReview>? _reviewCards;
    private IReadOnlyList<VocabularyContentBlock>? _content;
    public VocabularySense(Guid id, string meaning, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    { Id = id; Meaning = meaning; CreatedAt = createdAt; UpdatedAt = updatedAt; }
    public Guid Id { get; }
    public string Meaning { get; init; }
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
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyList<ReviewLayout> ReviewLayouts => CollectionValues.List((Review is null ? Array.Empty<VocabularyReview>() : new[] { Review })
        .Concat(ReviewCards ?? []).SelectMany(review => new[] { review.Plan }.Concat(review.PreservedPlans))
        .Select(plan => plan.Layout).OfType<ReviewLayout>());
    public VocabularyReview? ReviewValue(Guid? cardId) => cardId is null ? Review : ReviewCards?.FirstOrDefault(review => review.Plan.Id == cardId);

    /// <summary>Mac setReview adapted to snapshot replacement; only the named card slot changes.</summary>
    public VocabularySense WithReview(VocabularyReview value, Guid? cardId)
    {
        if (cardId is null) return this with { Review = value };
        var cards = (ReviewCards ?? []).ToList();
        int index = cards.FindIndex(review => review.Plan.Id == cardId);
        if (index < 0) cards.Add(value); else cards[index] = value;
        return this with { ReviewCards = cards };
    }

    public bool Equals(VocabularySense? other) => other is not null && Id == other.Id && CollectionValues.Same(Meaning, other.Meaning)
        && MeaningLanguage == other.MeaningLanguage && MeaningOrigin == other.MeaningOrigin && CollectionValues.Same(Note, other.Note)
        && CollectionValues.SequenceEqual(Tags, other.Tags) && CollectionValues.SequenceEqual(Examples, other.Examples)
        && IsSavedWithoutContext == other.IsSavedWithoutContext && CollectionValues.SequenceEqual(StandaloneMeanings, other.StandaloneMeanings)
        && CollectionValues.SequenceEqual(Explanations, other.Explanations) && CollectionValues.SequenceEqual(AlternateMeanings, other.AlternateMeanings)
        && CollectionValues.SequenceEqual(MergedFrom, other.MergedFrom) && Review == other.Review
        && CollectionValues.SequenceEqual(ReviewCards, other.ReviewCards) && CollectionValues.SequenceEqual(Content, other.Content)
        && CreatedAt == other.CreatedAt && UpdatedAt == other.UpdatedAt;
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Id); hash.Add(CollectionValues.StringHash(Meaning)); hash.Add(MeaningLanguage); hash.Add(MeaningOrigin);
        hash.Add(CollectionValues.StringHash(Note)); hash.Add(CollectionValues.SequenceHash(Tags)); hash.Add(CollectionValues.SequenceHash(Examples));
        hash.Add(IsSavedWithoutContext); hash.Add(CollectionValues.SequenceHash(StandaloneMeanings)); hash.Add(CollectionValues.SequenceHash(Explanations));
        hash.Add(CollectionValues.SequenceHash(AlternateMeanings)); hash.Add(CollectionValues.SequenceHash(MergedFrom)); hash.Add(Review);
        hash.Add(CollectionValues.SequenceHash(ReviewCards)); hash.Add(CollectionValues.SequenceHash(Content)); hash.Add(CreatedAt); hash.Add(UpdatedAt);
        return hash.ToHashCode();
    }
}

/// <summary>
/// App/store-owned immutable notebook snapshot shape. Version 3 matches the reference value's
/// schema marker, not a promise of storage compatibility. Optional templates preserve absence.
/// </summary>
public sealed record VocabularyDocument
{
    private IReadOnlyList<VocabularyEntry> _entries = Array.Empty<VocabularyEntry>();
    private IReadOnlyList<ReviewLayoutTemplate>? _reviewTemplates;
    public int SchemaVersion { get; init; } = 3;
    public IReadOnlyList<VocabularyEntry> Entries { get => _entries; init => _entries = CollectionValues.List(value); }
    public IReadOnlyList<ReviewLayoutTemplate>? ReviewTemplates { get => _reviewTemplates; init => _reviewTemplates = CollectionValues.OptionalList(value); }
    public bool Equals(VocabularyDocument? other) => other is not null && SchemaVersion == other.SchemaVersion
        && CollectionValues.SequenceEqual(Entries, other.Entries) && CollectionValues.SequenceEqual(ReviewTemplates, other.ReviewTemplates);
    public override int GetHashCode() => HashCode.Combine(SchemaVersion, CollectionValues.SequenceHash(Entries), CollectionValues.SequenceHash(ReviewTemplates));
}

/// <summary>Store revision receipt accompanies the graph; it is not a record/usage ID or persisted schema.</summary>
public sealed class VocabularySnapshot(ulong revision, VocabularyDocument document)
{
    public ulong Revision { get; } = revision;
    public VocabularyDocument Document { get; } = document;
}
