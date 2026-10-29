using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Archive-owned independent meaning values, retained through lexical projection unchanged.
/// These declarations do not classify text or validate semantic references/cycles by themselves.</summary>
public sealed record VocabularySemanticIndex
{
    private IReadOnlyList<VocabularyMeaningNode> _nodes = Array.Empty<VocabularyMeaningNode>();
    private IReadOnlyList<VocabularyUsagePattern> _patterns = Array.Empty<VocabularyUsagePattern>();
    private IReadOnlyList<VocabularyMeaningMapping> _mappings = Array.Empty<VocabularyMeaningMapping>();
    private IReadOnlyList<VocabularyMeaningRelation> _relations = Array.Empty<VocabularyMeaningRelation>();
    private IReadOnlyList<VocabularyRetrievalCue> _cues = Array.Empty<VocabularyRetrievalCue>();
    private IReadOnlyList<VocabularyCueTarget> _cueTargets = Array.Empty<VocabularyCueTarget>();
    public IReadOnlyList<VocabularyMeaningNode> Nodes { get => _nodes; init => _nodes = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyUsagePattern> Patterns { get => _patterns; init => _patterns = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyMeaningMapping> Mappings { get => _mappings; init => _mappings = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyMeaningRelation> Relations { get => _relations; init => _relations = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyRetrievalCue> Cues { get => _cues; init => _cues = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyCueTarget> CueTargets { get => _cueTargets; init => _cueTargets = CollectionValues.List(value); }
    public bool Equals(VocabularySemanticIndex? other) => other is not null
        && CollectionValues.SequenceEqual(Nodes, other.Nodes)
        && CollectionValues.SequenceEqual(Patterns, other.Patterns)
        && CollectionValues.SequenceEqual(Mappings, other.Mappings)
        && CollectionValues.SequenceEqual(Relations, other.Relations)
        && CollectionValues.SequenceEqual(Cues, other.Cues)
        && CollectionValues.SequenceEqual(CueTargets, other.CueTargets);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CollectionValues.SequenceHash(Nodes));
        hash.Add(CollectionValues.SequenceHash(Patterns));
        hash.Add(CollectionValues.SequenceHash(Mappings));
        hash.Add(CollectionValues.SequenceHash(Relations));
        hash.Add(CollectionValues.SequenceHash(Cues));
        hash.Add(CollectionValues.SequenceHash(CueTargets));
        return hash.ToHashCode();
    }
}
/// <summary>An independent concept/function, never an expression sense or category membership.
/// Its label identities survive translation; semantic edits remain explicit archive operations.</summary>
public sealed record VocabularyMeaningNode
{
    private IReadOnlyList<VocabularyMeaningLabel> _labels = Array.Empty<VocabularyMeaningLabel>();
    private IReadOnlyList<VocabularyMeaningRole> _roles = Array.Empty<VocabularyMeaningRole>();
    public VocabularyMeaningNode(Guid id, IReadOnlyList<VocabularyMeaningLabel> labels)
    { Id = id; Labels = labels; }
    public Guid Id { get; }
    public IReadOnlyList<VocabularyMeaningLabel> Labels { get => _labels; init => _labels = CollectionValues.List(value); }
    public VocabularyMeaningNodeKind Kind { get; init; } = VocabularyMeaningNodeKind.Concept;
    public string Definition { get; init; } = "";
    public string ScopeNote { get; init; } = "";
    public IReadOnlyList<VocabularyMeaningRole> Roles { get => _roles; init => _roles = CollectionValues.List(value); }
    public VocabularySemanticProvenance Provenance { get; init; } = new();
    public SourceDate CreatedAt { get; init; } = SourceDate.FromWindows(DateTimeOffset.UtcNow);
    public SourceDate UpdatedAt { get; init; } = SourceDate.FromWindows(DateTimeOffset.UtcNow);
    public string Title => Labels.FirstOrDefault(label => label.Preferred)?.Text ?? Labels.FirstOrDefault()?.Text ?? "";
    public bool Equals(VocabularyMeaningNode? other) => other is not null
        && Id == other.Id
        && CollectionValues.SequenceEqual(Labels, other.Labels)
        && Kind == other.Kind
        && CollectionValues.Same(Definition, other.Definition)
        && CollectionValues.Same(ScopeNote, other.ScopeNote)
        && CollectionValues.SequenceEqual(Roles, other.Roles)
        && Provenance == other.Provenance
        && CreatedAt == other.CreatedAt
        && UpdatedAt == other.UpdatedAt;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(CollectionValues.SequenceHash(Labels));
        hash.Add(Kind);
        hash.Add(CollectionValues.StringHash(Definition));
        hash.Add(CollectionValues.StringHash(ScopeNote));
        hash.Add(CollectionValues.SequenceHash(Roles));
        hash.Add(Provenance);
        hash.Add(CreatedAt);
        hash.Add(UpdatedAt);
        return hash.ToHashCode();
    }
}
