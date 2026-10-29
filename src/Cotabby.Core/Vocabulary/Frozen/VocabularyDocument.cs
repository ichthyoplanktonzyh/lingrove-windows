using Cotabby.Core.Review;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Frozen version-eight canonical resource graph. It is a detached value boundary for the next
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
    public long SchemaVersion { get; init; } = 8;
    public IReadOnlyList<VocabularyEntry> Catalog { get => _catalog; init => _catalog = CollectionValues.List(value); }
    public IReadOnlyList<VocabularySourceSnapshot> Sources { get => _sources; init => _sources = CollectionValues.List(value); }
    public IReadOnlyList<VocabularySourceOccurrence> Occurrences { get => _occurrences; init => _occurrences = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyInterpretation> Interpretations { get => _interpretations; init => _interpretations = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyCollectionItem> Collections { get => _collections; init => _collections = CollectionValues.List(value); }
    public IReadOnlyList<ReviewLayoutTemplate>? ReviewTemplates { get => _reviewTemplates; init => _reviewTemplates = CollectionValues.OptionalList(value); }
    public VocabularySemanticIndex Semantics { get; init; } = new();
    public VocabularyClassificationIndex Classification { get; init; } = new();
    public const long CurrentVersion = 8;
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
        && Semantics == other.Semantics
        && Classification == other.Classification;
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
        hash.Add(Classification);
        return hash.ToHashCode();
    }
}
