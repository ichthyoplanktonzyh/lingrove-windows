using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyMeaningNodeKind>))]
public enum VocabularyMeaningNodeKind
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("concept")] Concept,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("function")] Function,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyPatternFormality>))]
public enum VocabularyPatternFormality
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unspecified")] Unspecified,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("informal")] Informal,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("neutral")] Neutral,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("formal")] Formal,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyPatternMedium>))]
public enum VocabularyPatternMedium
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unspecified")] Unspecified,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("spoken")] Spoken,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("written")] Written,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("both")] Both,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyMappingRelation>))]
public enum VocabularyMappingRelation
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("expressesContent")] ExpressesContent,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("realizesFunction")] RealizesFunction,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyMeaningRelationKind>))]
public enum VocabularyMeaningRelationKind
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("narrower")] Narrower,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("opposite")] Opposite,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("related")] Related,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularyCueKind>))]
public enum VocabularyCueKind
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("paraphrase")] Paraphrase,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("simpleExpression")] SimpleExpression,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("personal")] Personal,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularySemanticOrigin>))]
public enum VocabularySemanticOrigin
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("user")] User,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("imported")] Imported,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("model")] Model,
}

/// <summary>Closed current Mac resource values; not the older entry-owned archive enum.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<VocabularySemanticStatus>))]
public enum VocabularySemanticStatus
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("confirmed")] Confirmed,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("proposed")] Proposed,
}

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
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
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
/// <summary>One language-specific name/alias for a node; it has preference but no acceptance status.
/// Ambiguous retrieval phrases belong to separate cues rather than changing label identity.</summary>
public sealed record VocabularyMeaningLabel
{
    public VocabularyMeaningLabel(string text)
    { Text = text; }
    public string Text { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public LearningLanguage? Language { get; init; }
    public bool Preferred { get; init; } = true;
    public bool Equals(VocabularyMeaningLabel? other) => other is not null
        && CollectionValues.Same(Text, other.Text)
        && Id == other.Id
        && Language == other.Language
        && Preferred == other.Preferred;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CollectionValues.StringHash(Text));
        hash.Add(Id);
        hash.Add(Language);
        hash.Add(Preferred);
        return hash.ToHashCode();
    }
}
/// <summary>A node-owned semantic role. Pattern syntax slots retain separate identities.</summary>
public sealed record VocabularyMeaningRole
{
    public VocabularyMeaningRole(string name)
    { Name = name; }
    public string Name { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Description { get; init; } = "";
    public bool Equals(VocabularyMeaningRole? other) => other is not null
        && CollectionValues.Same(Name, other.Name)
        && Id == other.Id
        && CollectionValues.Same(Description, other.Description);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CollectionValues.StringHash(Name));
        hash.Add(Id);
        hash.Add(CollectionValues.StringHash(Description));
        return hash.ToHashCode();
    }
}
/// <summary>Syntax associated with one sense. Mapping restrictions name this exact identity;
/// deleting a pattern must not broaden an association to the whole sense.</summary>
public sealed record VocabularyUsagePattern
{
    private IReadOnlyList<VocabularyPatternSlot> _slots = Array.Empty<VocabularyPatternSlot>();
    private IReadOnlyList<string> _collocations = Array.Empty<string>();
    public VocabularyUsagePattern(Guid senseId, string template)
    { SenseId = senseId; Template = template; }
    public Guid SenseId { get; init; }
    public string Template { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public IReadOnlyList<VocabularyPatternSlot> Slots { get => _slots; init => _slots = CollectionValues.List(value); }
    public VocabularyPatternFormality Formality { get; init; } = VocabularyPatternFormality.Unspecified;
    public VocabularyPatternMedium Medium { get; init; } = VocabularyPatternMedium.Unspecified;
    public string Region { get; init; } = "";
    public string Note { get; init; } = "";
    public IReadOnlyList<string> Collocations { get => _collocations; init => _collocations = CollectionValues.List(value); }
    public VocabularySemanticProvenance Provenance { get; init; } = new();
    public bool Equals(VocabularyUsagePattern? other) => other is not null
        && SenseId == other.SenseId
        && CollectionValues.Same(Template, other.Template)
        && Id == other.Id
        && CollectionValues.SequenceEqual(Slots, other.Slots)
        && Formality == other.Formality
        && Medium == other.Medium
        && CollectionValues.Same(Region, other.Region)
        && CollectionValues.Same(Note, other.Note)
        && CollectionValues.SequenceEqual(Collocations, other.Collocations)
        && Provenance == other.Provenance;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SenseId);
        hash.Add(CollectionValues.StringHash(Template));
        hash.Add(Id);
        hash.Add(CollectionValues.SequenceHash(Slots));
        hash.Add(Formality);
        hash.Add(Medium);
        hash.Add(CollectionValues.StringHash(Region));
        hash.Add(CollectionValues.StringHash(Note));
        hash.Add(CollectionValues.SequenceHash(Collocations));
        hash.Add(Provenance);
        return hash.ToHashCode();
    }
}
/// <summary>One pattern-owned syntax position, distinct from a node role even when names match.</summary>
public sealed record VocabularyPatternSlot
{
    public VocabularyPatternSlot(string name)
    { Name = name; }
    public string Name { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Constraint { get; init; } = "";
    public bool Equals(VocabularyPatternSlot? other) => other is not null
        && CollectionValues.Same(Name, other.Name)
        && Id == other.Id
        && CollectionValues.Same(Constraint, other.Constraint);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CollectionValues.StringHash(Name));
        hash.Add(Id);
        hash.Add(CollectionValues.StringHash(Constraint));
        return hash.ToHashCode();
    }
}
/// <summary>An explicit sense-to-node claim, optionally narrowed to a pattern with role/slot bindings.
/// It carries occurrence evidence and authorship/adoption rather than owning lexical material.</summary>
public sealed record VocabularyMeaningMapping
{
    private IReadOnlyList<VocabularyRoleBinding> _roleBindings = Array.Empty<VocabularyRoleBinding>();
    private IReadOnlyList<Guid> _evidenceIds = Array.Empty<Guid>();
    public VocabularyMeaningMapping(Guid nodeId, Guid senseId)
    { NodeId = nodeId; SenseId = senseId; }
    public Guid NodeId { get; init; }
    public Guid SenseId { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? PatternId { get; init; }
    public VocabularyMappingRelation Relation { get; init; } = VocabularyMappingRelation.ExpressesContent;
    public string Conditions { get; init; } = "";
    public string Nuance { get; init; } = "";
    public IReadOnlyList<VocabularyRoleBinding> RoleBindings { get => _roleBindings; init => _roleBindings = CollectionValues.List(value); }
    public IReadOnlyList<Guid> EvidenceIds { get => _evidenceIds; init => _evidenceIds = CollectionValues.List(value); }
    public VocabularySemanticProvenance Provenance { get; init; } = new();
    public bool Equals(VocabularyMeaningMapping? other) => other is not null
        && NodeId == other.NodeId
        && SenseId == other.SenseId
        && Id == other.Id
        && PatternId == other.PatternId
        && Relation == other.Relation
        && CollectionValues.Same(Conditions, other.Conditions)
        && CollectionValues.Same(Nuance, other.Nuance)
        && CollectionValues.SequenceEqual(RoleBindings, other.RoleBindings)
        && CollectionValues.SequenceEqual(EvidenceIds, other.EvidenceIds)
        && Provenance == other.Provenance;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(NodeId);
        hash.Add(SenseId);
        hash.Add(Id);
        hash.Add(PatternId);
        hash.Add(Relation);
        hash.Add(CollectionValues.StringHash(Conditions));
        hash.Add(CollectionValues.StringHash(Nuance));
        hash.Add(CollectionValues.SequenceHash(RoleBindings));
        hash.Add(CollectionValues.SequenceHash(EvidenceIds));
        hash.Add(Provenance);
        return hash.ToHashCode();
    }
}
/// <summary>An explicit mapping between independent semantic-role and syntax-slot IDs.</summary>
public sealed record VocabularyRoleBinding
{
    public VocabularyRoleBinding(Guid roleId, Guid slotId)
    { RoleId = roleId; SlotId = slotId; }
    public Guid RoleId { get; init; }
    public Guid SlotId { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Equals(VocabularyRoleBinding? other) => other is not null
        && RoleId == other.RoleId
        && SlotId == other.SlotId
        && Id == other.Id;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RoleId);
        hash.Add(SlotId);
        hash.Add(Id);
        return hash.ToHashCode();
    }
}
/// <summary>Authored relation between meaning nodes; narrower points from specific to broader.
/// Similar labels or embeddings never create this edge automatically.</summary>
public sealed record VocabularyMeaningRelation
{
    public VocabularyMeaningRelation(Guid sourceId, Guid targetId)
    { SourceId = sourceId; TargetId = targetId; }
    public Guid SourceId { get; init; }
    public Guid TargetId { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public VocabularyMeaningRelationKind Kind { get; init; } = VocabularyMeaningRelationKind.Related;
    public string Note { get; init; } = "";
    public VocabularySemanticProvenance Provenance { get; init; } = new();
    public bool Equals(VocabularyMeaningRelation? other) => other is not null
        && SourceId == other.SourceId
        && TargetId == other.TargetId
        && Id == other.Id
        && Kind == other.Kind
        && CollectionValues.Same(Note, other.Note)
        && Provenance == other.Provenance;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceId);
        hash.Add(TargetId);
        hash.Add(Id);
        hash.Add(Kind);
        hash.Add(CollectionValues.StringHash(Note));
        hash.Add(Provenance);
        return hash.ToHashCode();
    }
}
/// <summary>An ambiguous search phrase, with separate many-to-many node targets instead of synonymy.</summary>
public sealed record VocabularyRetrievalCue
{
    public VocabularyRetrievalCue(string text)
    { Text = text; }
    public string Text { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public LearningLanguage? Language { get; init; }
    public VocabularyCueKind Kind { get; init; } = VocabularyCueKind.Paraphrase;
    public bool Equals(VocabularyRetrievalCue? other) => other is not null
        && CollectionValues.Same(Text, other.Text)
        && Id == other.Id
        && Language == other.Language
        && Kind == other.Kind;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CollectionValues.StringHash(Text));
        hash.Add(Id);
        hash.Add(Language);
        hash.Add(Kind);
        return hash.ToHashCode();
    }
}
/// <summary>One explicit cue-to-node association and its conditions/provenance.</summary>
public sealed record VocabularyCueTarget
{
    public VocabularyCueTarget(Guid cueId, Guid nodeId)
    { CueId = cueId; NodeId = nodeId; }
    public Guid CueId { get; init; }
    public Guid NodeId { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Conditions { get; init; } = "";
    public VocabularySemanticProvenance Provenance { get; init; } = new();
    public bool Equals(VocabularyCueTarget? other) => other is not null
        && CueId == other.CueId
        && NodeId == other.NodeId
        && Id == other.Id
        && CollectionValues.Same(Conditions, other.Conditions)
        && Provenance == other.Provenance;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CueId);
        hash.Add(NodeId);
        hash.Add(Id);
        hash.Add(CollectionValues.StringHash(Conditions));
        hash.Add(Provenance);
        return hash.ToHashCode();
    }
}
/// <summary>Authorship and adoption are independent. Authorized model claims may be confirmed;
/// proposed remains explicit uncertainty and does not imply a second internal classifier.</summary>
public sealed record VocabularySemanticProvenance
{
    public VocabularySemanticOrigin Origin { get; init; } = VocabularySemanticOrigin.User;
    public VocabularySemanticStatus Status { get; init; } = VocabularySemanticStatus.Confirmed;
    public string Source { get; init; } = "";
    public double? Confidence { get; init; }
    public bool Equals(VocabularySemanticProvenance? other) => other is not null
        && Origin == other.Origin
        && Status == other.Status
        && CollectionValues.Same(Source, other.Source)
        && Confidence == other.Confidence;
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Origin);
        hash.Add(Status);
        hash.Add(CollectionValues.StringHash(Source));
        hash.Add(Confidence);
        return hash.ToHashCode();
    }
}
