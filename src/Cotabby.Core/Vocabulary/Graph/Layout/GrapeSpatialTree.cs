namespace Cotabby.Core.Vocabulary;

// Adapted from Grape 1.1.0 BufferedKDTree/KDBox/KDTreeNode, MIT Copyright (c) 2023 Zhen Li.
// See resources/ThirdParty/Grape/LICENSE and UPSTREAM.md for the unchanged Swift provenance.
/// <summary>Simulation-owned 2D specialization of the original buffered spatial tree. Managed
/// child indices replace rebased unsafe pointers; quadrant order, clustered leaf insertion,
/// bounding boxes, and post-child mass accumulation remain the original algorithm.</summary>
internal sealed class GrapeSpatialTree
{
    internal sealed class Node
    {
        public GrapeVector Low, High, Position, Weighted;
        public int Children = -1, Count;
        public List<int>? Indices;
        public GrapeVector Center => (High + Low) / 2;
    }
    private readonly List<Node> _buffer = [];
    private int _validCount;
    public Node this[int index] => _buffer[index];
    public void Reset(GrapeVector[] positions)
    {
        var low = positions[0]; var high = low;
        foreach (var p in positions)
        {
            low = new(Math.Min(p.X, low.X), Math.Min(p.Y, low.Y));
            high = new(Math.Max(p.X, high.X), Math.Max(p.Y, high.Y));
        }
        // The original UnsafeArray overload adds one to BOTH maxima, unlike its Array overload.
        // A new shape may reuse only a prefix of the pool. Release every old leaf list,
        // matching upstream reset/disposeNodeIndices even for now-inactive suffix nodes.
        for (int i = 0; i < _validCount; i++) _buffer[i].Indices = null;
        _validCount = 0;
        Allocate(low, high + new GrapeVector(1, 1));
        for (int i = 0; i < positions.Length; i++) Add(0, i, positions[i]);
    }
    private int Allocate(GrapeVector low, GrapeVector high)
    {
        int index = _validCount++;
        if (index == _buffer.Count) _buffer.Add(new());
        var node = _buffer[index]; node.Low = low; node.High = high; node.Position = default;
        node.Weighted = default; node.Children = -1; node.Count = 0; node.Indices = null;
        return index;
    }
    private static int Direction(GrapeVector point, GrapeVector center) => (point.X >= center.X ? 1 : 0) | (point.Y >= center.Y ? 2 : 0);
    private void Add(int index, int particle, GrapeVector point)
    {
        var node = _buffer[index];
        if (node.Children >= 0) Add(node.Children + Direction(point, node.Center), particle, point);
        else if (node.Indices is null)
        {
            node.Indices = [particle]; node.Position = point;
        }
        else if ((node.Position - point).LengthSquared > 1e-5 * 1e-5)
        {
            var center = node.Center; int children = _validCount;
            for (int j = 0; j < 4; j++)
                Allocate(new((j & 1) != 0 ? center.X : node.Low.X, (j & 2) != 0 ? center.Y : node.Low.Y),
                    new((j & 1) != 0 ? node.High.X : center.X, (j & 2) != 0 ? node.High.Y : center.Y));
            node.Children = children;
            var existing = _buffer[children + Direction(node.Position, center)];
            existing.Indices = node.Indices; existing.Position = node.Position;
            existing.Count = node.Count; existing.Weighted = node.Weighted;
            node.Indices = null; node.Position = default;
            Add(children + Direction(point, center), particle, point);
        }
        else node.Indices.Add(particle);
        node.Count++; node.Weighted += point;
    }
    public void Visit(Func<Node, bool> shouldVisitChildren) => Visit(0, shouldVisitChildren);
    private void Visit(int index, Func<Node, bool> body)
    {
        var node = _buffer[index];
        if (body(node) && node.Children >= 0)
            for (int i = 0; i < 4; i++) Visit(node.Children + i, body);
    }
}
