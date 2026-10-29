using LearningExplanationInput = Cotabby.Core.Vocabulary.Resources.LearningExplanationInput;
using LearningExplanationContext = Cotabby.Core.Vocabulary.Resources.LearningExplanationContext;
using FrozenCompleteness = Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>A claim about an occurrence, independent of its evidence and optional lexical destination.
/// Candidate/rejected claims survive without collection membership; accepted claims join examples.</summary>
public sealed record VocabularyInterpretation
{
    private IReadOnlyList<VocabularyMeaning> _translationVariants = Array.Empty<VocabularyMeaning>();
    private IReadOnlyList<LearningExplanationRecord> _explanations = Array.Empty<LearningExplanationRecord>();
    public VocabularyInterpretation(Guid id, Guid occurrenceId, SourceDate createdAt)
    { Id = id; OccurrenceId = occurrenceId; CreatedAt = createdAt; }
    public Guid Id { get; init; }
    public Guid OccurrenceId { get; init; }
    public SourceDate CreatedAt { get; init; }
    public Guid? SenseId { get; init; }
    public VocabularyInterpretationStatus Status { get; init; } = VocabularyInterpretationStatus.Candidate;
    public string Meaning { get; init; } = "";
    public LearningLanguage? MeaningLanguage { get; init; }
    public VocabularyMeaningOrigin MeaningOrigin { get; init; } = VocabularyMeaningOrigin.Captured;
    public string Translation { get; init; } = "";
    public LearningLanguage? TranslationLanguage { get; init; }
    public IReadOnlyList<VocabularyMeaning> TranslationVariants { get => _translationVariants; init => _translationVariants = CollectionValues.List(value); }
    public FrozenCompleteness Completeness { get; init; } = FrozenCompleteness.Unknown;
    public VocabularySemanticProvenance Provenance { get; init; } = new();
    public IReadOnlyList<LearningExplanationRecord> Explanations { get => _explanations; init => _explanations = CollectionValues.List(value); }
    public bool Equals(VocabularyInterpretation? other) => other is not null
        && Id == other.Id
        && OccurrenceId == other.OccurrenceId
        && CreatedAt == other.CreatedAt
        && SenseId == other.SenseId
        && Status == other.Status
        && CollectionValues.Same(Meaning, other.Meaning)
        && MeaningLanguage == other.MeaningLanguage
        && MeaningOrigin == other.MeaningOrigin
        && CollectionValues.Same(Translation, other.Translation)
        && TranslationLanguage == other.TranslationLanguage
        && CollectionValues.SequenceEqual(TranslationVariants, other.TranslationVariants)
        && Completeness == other.Completeness
        && Provenance == other.Provenance
        && CollectionValues.SequenceEqual(Explanations, other.Explanations);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(OccurrenceId);
        hash.Add(CreatedAt);
        hash.Add(SenseId);
        hash.Add(Status);
        hash.Add(CollectionValues.StringHash(Meaning));
        hash.Add(MeaningLanguage);
        hash.Add(MeaningOrigin);
        hash.Add(CollectionValues.StringHash(Translation));
        hash.Add(TranslationLanguage);
        hash.Add(CollectionValues.SequenceHash(TranslationVariants));
        hash.Add(Completeness);
        hash.Add(Provenance);
        hash.Add(CollectionValues.SequenceHash(Explanations));
        return hash.ToHashCode();
    }
}
/// <summary>The local user relationship owns notes, tags, selected claims, rich content and reviews.
/// Removing it uncollects material without deleting knowledge or evidence; SenseId is its key.</summary>
public sealed record VocabularyCollectionItem
{
    private IReadOnlyList<Guid> _interpretationIds = Array.Empty<Guid>();
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private IReadOnlyList<VocabularyContentBlock>? _content = null;
    private IReadOnlyList<VocabularyReview>? _reviewCards = null;
    public VocabularyCollectionItem(Guid senseId, SourceDate savedAt)
    { SenseId = senseId; SavedAt = savedAt; }
    public Guid SenseId { get; init; }
    public SourceDate SavedAt { get; init; }
    public IReadOnlyList<Guid> InterpretationIds { get => _interpretationIds; init => _interpretationIds = CollectionValues.List(value); }
    public bool IsSavedWithoutContext { get; init; }
    public string Note { get; init; } = "";
    public IReadOnlyList<string> Tags { get => _tags; init => _tags = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyContentBlock>? Content { get => _content; init => _content = CollectionValues.OptionalList(value); }
    public VocabularyReview? Review { get; init; }
    public IReadOnlyList<VocabularyReview>? ReviewCards { get => _reviewCards; init => _reviewCards = CollectionValues.OptionalList(value); }
    public bool Equals(VocabularyCollectionItem? other) => other is not null
        && SenseId == other.SenseId
        && SavedAt == other.SavedAt
        && CollectionValues.SequenceEqual(InterpretationIds, other.InterpretationIds)
        && IsSavedWithoutContext == other.IsSavedWithoutContext
        && CollectionValues.Same(Note, other.Note)
        && CollectionValues.SequenceEqual(Tags, other.Tags)
        && CollectionValues.SequenceEqual(Content, other.Content)
        && Review == other.Review
        && CollectionValues.SequenceEqual(ReviewCards, other.ReviewCards);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SenseId);
        hash.Add(SavedAt);
        hash.Add(CollectionValues.SequenceHash(InterpretationIds));
        hash.Add(IsSavedWithoutContext);
        hash.Add(CollectionValues.StringHash(Note));
        hash.Add(CollectionValues.SequenceHash(Tags));
        hash.Add(CollectionValues.SequenceHash(Content));
        hash.Add(Review);
        hash.Add(CollectionValues.SequenceHash(ReviewCards));
        return hash.ToHashCode();
    }
}
