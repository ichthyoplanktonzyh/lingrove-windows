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
public sealed record VocabularyClassificationFramework(Guid Id, string Key, long Version)
{
    public bool Equals(VocabularyClassificationFramework? other) => other is not null && Id == other.Id && CollectionValues.Same(Key, other.Key) && Version == other.Version;
    public override int GetHashCode() => HashCode.Combine(Id, CollectionValues.StringHash(Key), Version);
}

/// <summary>Framework identity/key identify bundled definitions; custom categories have neither.</summary>
public sealed record VocabularyCategory
{
    private IReadOnlyList<VocabularyMeaningLabel> _labels = Array.Empty<VocabularyMeaningLabel>();
    private IReadOnlyList<VocabularyCategoryExample> _examples = Array.Empty<VocabularyCategoryExample>();
    private IReadOnlyList<VocabularyCategoryReference> _references = Array.Empty<VocabularyCategoryReference>();
    private IReadOnlyList<VocabularyCategoryLocalization> _localizations = Array.Empty<VocabularyCategoryLocalization>();
    private string _definition = "", _scopeNote = "";
    private VocabularySemanticProvenance _provenance = new();
    public VocabularyCategory(Guid id, VocabularyCategoryAxis axis, IReadOnlyList<VocabularyMeaningLabel> labels) { Id = id; Axis = axis; Labels = labels; }
    public Guid Id { get; }
    public VocabularyCategoryAxis Axis { get; init; }
    public IReadOnlyList<VocabularyMeaningLabel> Labels { get => _labels; init => _labels = CollectionValues.List(value); }
    public string Definition { get => _definition; init => _definition = value ?? ""; }
    public IReadOnlyList<VocabularyCategoryExample> Examples { get => _examples; init => _examples = CollectionValues.List(value ?? []); }
    public string ScopeNote { get => _scopeNote; init => _scopeNote = value ?? ""; }
    public IReadOnlyList<VocabularyCategoryReference> References { get => _references; init => _references = CollectionValues.List(value ?? []); }
    public IReadOnlyList<VocabularyCategoryLocalization> Localizations { get => _localizations; init => _localizations = CollectionValues.List(value ?? []); }
    public Guid? FrameworkId { get; init; }
    public string? Key { get; init; }
    public bool IsRetired { get; init; }
    public VocabularySemanticProvenance Provenance { get => _provenance; init => _provenance = value ?? new(); }
    public bool Equals(VocabularyCategory? other) => other is not null && Id == other.Id && Axis == other.Axis
        && CollectionValues.SequenceEqual(Labels, other.Labels) && CollectionValues.Same(Definition, other.Definition)
        && CollectionValues.SequenceEqual(Examples, other.Examples) && CollectionValues.Same(ScopeNote, other.ScopeNote) && CollectionValues.SequenceEqual(References, other.References) && CollectionValues.SequenceEqual(Localizations, other.Localizations)
        && FrameworkId == other.FrameworkId && CollectionValues.Same(Key, other.Key) && IsRetired == other.IsRetired && Provenance == other.Provenance;
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(Id); hash.Add(Axis); hash.Add(CollectionValues.SequenceHash(Labels)); hash.Add(CollectionValues.StringHash(Definition)); hash.Add(CollectionValues.SequenceHash(Examples)); hash.Add(CollectionValues.StringHash(ScopeNote)); hash.Add(CollectionValues.SequenceHash(References)); hash.Add(CollectionValues.SequenceHash(Localizations)); hash.Add(FrameworkId); hash.Add(CollectionValues.StringHash(Key)); hash.Add(IsRetired); hash.Add(Provenance); return hash.ToHashCode(); }
}
public sealed record VocabularyCategoryExample(string Text, string? Language = null)
{
    public bool Equals(VocabularyCategoryExample? other) => other is not null && CollectionValues.Same(Text, other.Text) && CollectionValues.Same(Language, other.Language);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.StringHash(Text), CollectionValues.StringHash(Language));
}
public sealed record VocabularyCategoryLocalization(string Language, string? Name = null, string? Definition = null, string? ScopeNote = null)
{
    public bool Equals(VocabularyCategoryLocalization? other) => other is not null && CollectionValues.Same(Language, other.Language) && CollectionValues.Same(Name, other.Name) && CollectionValues.Same(Definition, other.Definition) && CollectionValues.Same(ScopeNote, other.ScopeNote);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.StringHash(Language), CollectionValues.StringHash(Name), CollectionValues.StringHash(Definition), CollectionValues.StringHash(ScopeNote));
}
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyCategoryReferenceRelationship>))]
public enum VocabularyCategoryReferenceRelationship { [System.Text.Json.Serialization.JsonStringEnumMemberName("adapted")] Adapted, [System.Text.Json.Serialization.JsonStringEnumMemberName("combined")] Combined, [System.Text.Json.Serialization.JsonStringEnumMemberName("narrower")] Narrower }
public sealed record VocabularyCategoryReference(string Source, string Identifier, string Url, VocabularyCategoryReferenceRelationship Relationship, string? Attribution = null, string? License = null)
{
    public bool Equals(VocabularyCategoryReference? other) => other is not null && CollectionValues.Same(Source, other.Source) && CollectionValues.Same(Identifier, other.Identifier) && CollectionValues.Same(Url, other.Url) && Relationship == other.Relationship && CollectionValues.Same(Attribution, other.Attribution) && CollectionValues.Same(License, other.License);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.StringHash(Source), CollectionValues.StringHash(Identifier), CollectionValues.StringHash(Url), Relationship, CollectionValues.StringHash(Attribution), CollectionValues.StringHash(License));
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
    private IReadOnlyList<VocabularyCategoryExample>? _examples;
    private IReadOnlyList<VocabularyCategoryReference>? _references;
    private IReadOnlyList<VocabularyCategoryLocalization>? _localizations;
    public Guid Id => CategoryId;
    public IReadOnlyList<VocabularyMeaningLabel>? Labels { get => _labels; init => _labels = CollectionValues.OptionalList(value); }
    public string? Definition { get; init; }
    public IReadOnlyList<VocabularyCategoryExample>? Examples { get => _examples; init => _examples = CollectionValues.OptionalList(value); }
    public string? ScopeNote { get; init; }
    public IReadOnlyList<VocabularyCategoryReference>? References { get => _references; init => _references = CollectionValues.OptionalList(value); }
    public IReadOnlyList<VocabularyCategoryLocalization>? Localizations { get => _localizations; init => _localizations = CollectionValues.OptionalList(value); }
    public bool IsHidden { get; init; }
    public bool Equals(VocabularyCategoryOverride? other) => other is not null && CategoryId == other.CategoryId
        && CollectionValues.SequenceEqual(Labels, other.Labels) && CollectionValues.Same(Definition, other.Definition) && CollectionValues.SequenceEqual(Examples, other.Examples) && CollectionValues.Same(ScopeNote, other.ScopeNote) && CollectionValues.SequenceEqual(References, other.References) && CollectionValues.SequenceEqual(Localizations, other.Localizations) && IsHidden == other.IsHidden;
    public override int GetHashCode() => HashCode.Combine(CategoryId, CollectionValues.SequenceHash(Labels), CollectionValues.StringHash(Definition), CollectionValues.SequenceHash(Examples), CollectionValues.StringHash(ScopeNote), CollectionValues.SequenceHash(References), CollectionValues.SequenceHash(Localizations), IsHidden);
}
