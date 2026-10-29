using System.Collections.Frozen;
using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>Window-local graph identity is the original durable headword or exact sense identity,
/// never its label/position. A null sense identifies the headword; graph values invent no relation.</summary>
public readonly record struct VocabularyGraphNodeId(Guid EntryId, Guid? SenseId = null)
{
    public VocabularyGraphNodeKind Kind { get; init; }
    public VocabularySelection? Selection => Kind == VocabularyGraphNodeKind.Entry && SenseId is { } id ? new(EntryId, id) : null;
    public static VocabularyGraphNodeId Headword(Guid id) => new(id);
    public static VocabularyGraphNodeId Sense(VocabularySelection selection) => new(selection.EntryId, selection.SenseId);
    public static VocabularyGraphNodeId Meaning(Guid id) => new(id) { Kind = VocabularyGraphNodeKind.Meaning };
    public static VocabularyGraphNodeId Category(Guid id) => new(id) { Kind = VocabularyGraphNodeKind.Category };
    public static VocabularyGraphNodeId Axis(Resources.VocabularyCategoryAxis axis) => new(Guid.Parse(axis == Resources.VocabularyCategoryAxis.Content ? "00000000-0000-0000-0000-000000000001" : "00000000-0000-0000-0000-000000000002")) { Kind = VocabularyGraphNodeKind.Axis };
}
public enum VocabularyGraphNodeKind { Entry, Meaning, Category, Axis }
public enum VocabularyGraphEdgeKind { Ownership, Taxonomy, ExpressesContent, RealizesFunction, Narrower, Opposite, Related }

/// <summary>Projection-owned ID and literal display label travel together in a copied snapshot;
/// equal labels do not collapse distinct learning items, and labels retain canonical equality.</summary>
public sealed record VocabularyGraphNode(VocabularyGraphNodeId Id, string Label)
{
    public Resources.VocabularyMeaningNodeKind? MeaningKind { get; init; }
    public Resources.VocabularyCategoryAxis? Axis { get; init; }
    public int Depth { get; init; }
    public int ItemCount { get; init; }
    public bool IsUnexplored { get; init; }
    public bool HasChildren { get; init; }
    public bool IsExpanded { get; init; }
    public bool Equals(VocabularyGraphNode? other) => other is not null && Id == other.Id && CollectionValues.Same(Label, other.Label) && MeaningKind == other.MeaningKind && Axis == other.Axis && Depth == other.Depth && ItemCount == other.ItemCount && IsUnexplored == other.IsUnexplored && HasChildren == other.HasChildren && IsExpanded == other.IsExpanded;
    public override int GetHashCode() { var h = new HashCode(); h.Add(Id); h.Add(CollectionValues.StringHash(Label)); h.Add(MeaningKind); h.Add(Axis); h.Add(Depth); h.Add(ItemCount); h.Add(IsUnexplored); h.Add(HasChildren); h.Add(IsExpanded); return h.ToHashCode(); }
}
/// <summary>A copied explicit ownership branch, with no inferred similarity or model relationship.</summary>
public readonly record struct VocabularyGraphEdge(VocabularyGraphNodeId Source, VocabularyGraphNodeId Target)
{ public VocabularyGraphEdgeKind Kind { get; init; } public bool IsUnexplored { get; init; } }

/// <summary>Detached rendering projection owned by one notebook window. Rich content, archive
/// handles and model sessions stay outside this graph and any future per-frame simulation.</summary>
public sealed record VocabularyGraphSnapshot
{
    private IReadOnlyList<VocabularyGraphNode> _nodes = [];
    private IReadOnlyList<VocabularyGraphEdge> _edges = [];
    private IReadOnlySet<VocabularyGraphNodeId> _matches = FrozenSet<VocabularyGraphNodeId>.Empty;
    private IReadOnlyDictionary<VocabularyGraphNodeId, VocabularyGraphPoint> _treePositions = CollectionValues.Map<VocabularyGraphNodeId, VocabularyGraphPoint>([]);
    public IReadOnlyList<VocabularyGraphNode> Nodes { get => _nodes; init => _nodes = CollectionValues.List(value); }
    public IReadOnlyList<VocabularyGraphEdge> Edges { get => _edges; init => _edges = CollectionValues.List(value); }
    public int HeadwordCount { get; init; }
    public IReadOnlySet<VocabularyGraphNodeId> Matches { get => _matches; init => _matches = value.ToFrozenSet(); }
    public string SearchQuery { get; init; } = "";
    public bool IsMeaningGraph { get; init; }
    public IReadOnlyDictionary<VocabularyGraphNodeId, VocabularyGraphPoint> TreePositions { get => _treePositions; init => _treePositions = CollectionValues.Map(value); }
    public bool Equals(VocabularyGraphSnapshot? other) => other is not null && HeadwordCount == other.HeadwordCount
        && IsMeaningGraph == other.IsMeaningGraph && CollectionValues.MapEqual(TreePositions, other.TreePositions) && CollectionValues.Same(SearchQuery, other.SearchQuery) && CollectionValues.SequenceEqual(Nodes, other.Nodes)
        && CollectionValues.SequenceEqual(Edges, other.Edges) && CollectionValues.SetEqual(Matches, other.Matches);
    public override int GetHashCode() => HashCode.Combine(HeadwordCount, CollectionValues.StringHash(SearchQuery),
        CollectionValues.SequenceHash(Nodes), CollectionValues.SequenceHash(Edges), CollectionValues.SetHash(Matches), IsMeaningGraph, CollectionValues.MapHash(TreePositions));
}

/// <summary>Copied layout output. The serialized graph worker owns the mutable solver; consumers
/// receive these immutable positions and running state, without borrowing native buffers.</summary>
public sealed record VocabularyGraphFrame
{
    private IReadOnlyDictionary<VocabularyGraphNodeId, VocabularyGraphPoint> _positions = CollectionValues.Map<VocabularyGraphNodeId, VocabularyGraphPoint>([]);
    public IReadOnlyDictionary<VocabularyGraphNodeId, VocabularyGraphPoint> Positions { get => _positions; init => _positions = CollectionValues.Map(value); }
    public bool IsRunning { get; init; }
    public bool Equals(VocabularyGraphFrame? other) => other is not null && IsRunning == other.IsRunning && CollectionValues.MapEqual(Positions, other.Positions);
    public override int GetHashCode() => HashCode.Combine(IsRunning, CollectionValues.MapHash(Positions));
}
