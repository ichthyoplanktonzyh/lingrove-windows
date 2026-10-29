using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Independent directory of meanings. Collections and expression evidence do not belong here.</summary>
public sealed record VocabularyClassificationIndex
{
    private IReadOnlyList<VocabularyClassificationFramework> _frameworks = Array.Empty<VocabularyClassificationFramework>();
    private IReadOnlyList<VocabularyCategory> _categories = Array.Empty<VocabularyCategory>();
    private IReadOnlyList<VocabularyCategoryParent> _parents = Array.Empty<VocabularyCategoryParent>();
    private IReadOnlyList<VocabularyMeaningCategoryMembership> _memberships = Array.Empty<VocabularyMeaningCategoryMembership>();
    private IReadOnlyList<VocabularyCategoryOverride> _overrides = Array.Empty<VocabularyCategoryOverride>();
    public IReadOnlyList<VocabularyClassificationFramework> Frameworks { get => _frameworks; init => _frameworks = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyCategory> Categories { get => _categories; init => _categories = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyCategoryParent> Parents { get => _parents; init => _parents = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyMeaningCategoryMembership> Memberships { get => _memberships; init => _memberships = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyCategoryOverride> Overrides { get => _overrides; init => _overrides = CollectionValues.List(value); }
    public bool Equals(VocabularyClassificationIndex? other) => other is not null && CollectionValues.SequenceEqual(Frameworks, other.Frameworks)
        && CollectionValues.SequenceEqual(Categories, other.Categories) && CollectionValues.SequenceEqual(Parents, other.Parents)
        && CollectionValues.SequenceEqual(Memberships, other.Memberships) && CollectionValues.SequenceEqual(Overrides, other.Overrides);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.SequenceHash(Frameworks), CollectionValues.SequenceHash(Categories),
        CollectionValues.SequenceHash(Parents), CollectionValues.SequenceHash(Memberships), CollectionValues.SequenceHash(Overrides));
}

public enum VocabularyCategoryAxis { Content, Function }
public sealed record VocabularyClassificationFramework(Guid Id, string Key, int Version)
{
    public bool Equals(VocabularyClassificationFramework? other) => other is not null && Id == other.Id && CollectionValues.Same(Key, other.Key) && Version == other.Version;
    public override int GetHashCode() => HashCode.Combine(Id, CollectionValues.StringHash(Key), Version);
}

/// <summary>Framework identity/key identify bundled definitions; custom categories have neither.</summary>
public sealed record VocabularyCategory
{
    private IReadOnlyList<VocabularyMeaningLabel> _labels = Array.Empty<VocabularyMeaningLabel>();
    public VocabularyCategory(Guid id, VocabularyCategoryAxis axis, IReadOnlyList<VocabularyMeaningLabel> labels) { Id = id; Axis = axis; Labels = labels; }
    public Guid Id { get; }
    public VocabularyCategoryAxis Axis { get; init; }
    public IReadOnlyList<VocabularyMeaningLabel> Labels { get => _labels; init => _labels = CollectionValues.List(value); }
    public string Definition { get; init; } = "";
    public Guid? FrameworkId { get; init; }
    public string? Key { get; init; }
    public bool IsRetired { get; init; }
    public VocabularySemanticProvenance Provenance { get; init; } = new();
    public bool Equals(VocabularyCategory? other) => other is not null && Id == other.Id && Axis == other.Axis
        && CollectionValues.SequenceEqual(Labels, other.Labels) && CollectionValues.Same(Definition, other.Definition)
        && FrameworkId == other.FrameworkId && CollectionValues.Same(Key, other.Key) && IsRetired == other.IsRetired && Provenance == other.Provenance;
    public override int GetHashCode() => HashCode.Combine(Id, Axis, CollectionValues.SequenceHash(Labels), CollectionValues.StringHash(Definition), FrameworkId, CollectionValues.StringHash(Key), IsRetired, Provenance);
}
public sealed record VocabularyCategoryParent(Guid Id, Guid CategoryId, Guid ParentId);
public sealed record VocabularyMeaningCategoryMembership(Guid Id, Guid NodeId, Guid CategoryId)
{
    public VocabularySemanticProvenance Provenance { get; init; } = new();
}
/// <summary>Null labels/definition inherit; an empty definition explicitly clears the catalog definition.</summary>
public sealed record VocabularyCategoryOverride(Guid CategoryId)
{
    private IReadOnlyList<VocabularyMeaningLabel>? _labels;
    public Guid Id => CategoryId;
    public IReadOnlyList<VocabularyMeaningLabel>? Labels { get => _labels; init => _labels = CollectionValues.OptionalList(value); }
    public string? Definition { get; init; }
    public bool IsHidden { get; init; }
    public bool Equals(VocabularyCategoryOverride? other) => other is not null && CategoryId == other.CategoryId
        && CollectionValues.SequenceEqual(Labels, other.Labels) && CollectionValues.Same(Definition, other.Definition) && IsHidden == other.IsHidden;
    public override int GetHashCode() => HashCode.Combine(CategoryId, CollectionValues.SequenceHash(Labels), CollectionValues.StringHash(Definition), IsHidden);
}
