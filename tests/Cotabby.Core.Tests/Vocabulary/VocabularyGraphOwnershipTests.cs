using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyGraphOwnershipTests
{
    [Fact]
    public void CopiedGraphAndFrameRetainIdentityCollectionsAndCanonicalValueEquality()
    {
        Guid entry = Guid.NewGuid(), sense = Guid.NewGuid(); var head = VocabularyGraphNodeId.Headword(entry); var child = VocabularyGraphNodeId.Sense(new(entry, sense));
        var nodes = new List<VocabularyGraphNode> { new(head, "café"), new(child, "meaning") }; var edges = new List<VocabularyGraphEdge> { new(head, child) };
        var matches = new HashSet<VocabularyGraphNodeId> { child }; var positions = new Dictionary<VocabularyGraphNodeId, VocabularyGraphPoint> { [head] = new(1, 2) };
        var graph = new VocabularyGraphSnapshot { Nodes = nodes, Edges = edges, Matches = matches, HeadwordCount = 1 };
        var frame = new VocabularyGraphFrame { Positions = positions, IsRunning = true };
        nodes.Clear(); edges.Clear(); matches.Clear(); positions[head] = new(99, 99);
        Assert.Equal(2, graph.Nodes.Count); Assert.Single(graph.Edges); Assert.Contains(child, graph.Matches); Assert.NotEqual(head, child);
        Assert.Null(head.Selection); Assert.Equal(new VocabularySelection(entry, sense), child.Selection); Assert.Equal(new(1, 2), frame.Positions[head]);
        var equal = new VocabularyGraphSnapshot { Nodes = [new(head, "cafe\u0301"), new(child, "meaning")], Edges = [new(head, child)], Matches = new HashSet<VocabularyGraphNodeId> { child }, HeadwordCount = 1 };
        Assert.Equal(equal, graph); Assert.Equal(equal.GetHashCode(), graph.GetHashCode());
        Assert.Equal(new VocabularyGraphFrame { Positions = new Dictionary<VocabularyGraphNodeId, VocabularyGraphPoint> { [head] = new(1, 2) }, IsRunning = true }, frame);
    }
}
