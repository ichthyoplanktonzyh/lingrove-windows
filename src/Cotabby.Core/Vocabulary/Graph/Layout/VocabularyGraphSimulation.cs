using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>Serially owned adaptation of the stable GraphSimulation adapter and its Grape 1.1.0
/// force implementation (MIT, Copyright (c) 2023 Zhen Li; resources/ThirdParty/Grape).
/// One layout worker owns its particles, cooling and random stream. Only detached snapshots
/// leave this boundary; this type owns no timer, dispatcher, archive or UI lifetime.</summary>
public sealed class VocabularyGraphSimulation
{
    public readonly record struct Parameters(double Center, double Repulsion, double LinkStrength, double LinkDistance);
    public readonly record struct Edge(int Source, int Target);
    /// <summary>A snapshot copies both particle arrays; later steps cannot mutate an older frame.</summary>
    public sealed record Snapshot
    {
        public IReadOnlyList<VocabularyGraphPoint> Positions { get; }
        public IReadOnlyList<VocabularyGraphPoint> Velocities { get; }
        public bool IsRunning { get; }
        internal Snapshot(GrapeVector[] positions, GrapeVector[] velocities, bool running)
        {
            Positions = CollectionValues.List(positions.Select(p => p.Point));
            Velocities = CollectionValues.List(velocities.Select(p => p.Point)); IsRunning = running;
        }
    }
    private readonly GrapeVector[] _positions, _velocities;
    private readonly GrapeVector?[] _fixation;
    private readonly Edge[] _edges;
    private readonly double[] _bias;
    private readonly Parameters _parameters;
    private readonly GrapeSpatialTree _manyBodyTree = new(), _collisionTree = new();
    private double _alpha = .65;
    private uint _random = 1;
    private int? _pinned;
    private const double AlphaMinimum = .003, VelocityDecay = .65;
    public VocabularyGraphSimulation(IReadOnlyList<VocabularyGraphPoint> positions, IReadOnlyList<VocabularyGraphPoint> velocities,
        IReadOnlyList<Edge> edges, Parameters parameters)
    {
        if (positions.Count == 0 || positions.Count != velocities.Count) throw new ArgumentException("A nonempty matching particle pair is required.");
        _positions = positions.Select(GrapeVector.From).ToArray(); _velocities = velocities.Select(GrapeVector.From).ToArray();
        _fixation = new GrapeVector?[positions.Count]; _parameters = parameters;
        _edges = edges.Where(edge => edge.Source < positions.Count && edge.Target < positions.Count).ToArray();
        if (_edges.Any(edge => edge.Source < 0 || edge.Target < 0)) throw new ArgumentException("Particle indices cannot be negative.", nameof(edges));
        var degrees = new int[positions.Count];
        foreach (var edge in _edges) { degrees[edge.Source]++; degrees[edge.Target]++; }
        _bias = _edges.Select(edge => (double)degrees[edge.Source] / (degrees[edge.Target] + degrees[edge.Source])).ToArray();
    }
    public Snapshot Current => new(_positions, _velocities, _alpha >= AlphaMinimum);
    public Snapshot Step(int? pinned = null, VocabularyGraphPoint? position = null)
    {
        // The app maps stable IDs to valid indices. Reject an invalid direct call instead of
        // retaining an index that the original adapter would trap on after the physics tick.
        if (pinned is { } invalid && (invalid < 0 || invalid >= _positions.Length)) throw new ArgumentOutOfRangeException(nameof(pinned));
        if (_pinned is { } previous && previous != pinned)
        {
            _fixation[previous] = null; _velocities[previous] = default; _alpha = Math.Max(_alpha, .3);
        }
        _pinned = pinned;
        if (pinned is { } index && position is { } target)
        {
            _fixation[index] = GrapeVector.From(target); _velocities[index] = default; _alpha = Math.Max(_alpha, .3);
        }
        if (_alpha >= AlphaMinimum)
        {
            _alpha += 0 - _alpha * .035;
            ManyBody(); Links(); Position(); Collide();
            for (int i = 0; i < _positions.Length; i++)
            {
                if (_fixation[i] is { } fixedPoint) _positions[i] = fixedPoint;
                else { _velocities[i] *= VelocityDecay; _positions[i] += _velocities[i]; }
            }
        }
        if (_pinned is { } pinnedIndex) _velocities[pinnedIndex] = default;
        return Current;
    }
    private void ManyBody()
    {
        _manyBodyTree.Reset(_positions);
        for (int i = 0; i < _positions.Length; i++)
        {
            int particle = i; var position = _positions[i]; var force = new GrapeVector();
            _manyBodyTree.Visit(node =>
            {
                if (node.Count == 0) return false;
                var vector = node.Weighted / node.Count - position;
                double width = node.High.X - node.Low.X, squared = vector.Jiggled(ref _random).LengthSquared;
                bool farEnough = squared * (.9 * .9) > width * width;
                if (squared < 1) squared = Math.Sqrt(1 * squared);
                if (farEnough)
                {
                    force += vector * (-_parameters.Repulsion * _alpha * node.Count / squared); return false;
                }
                if (node.Children >= 0) return true;
                if (node.Indices is not null)
                {
                    if (node.Indices.Contains(particle)) return false;
                    force += vector * (-_parameters.Repulsion * _alpha * node.Count / squared); return false;
                }
                return true;
            });
            // Upstream's many-body force writes POSITION immediately, not velocity. Its tree
            // still contains the pre-force positions for the rest of this ordered traversal.
            _positions[i] += force / 1;
        }
    }
    private void Links()
    {
        for (int i = 0; i < _edges.Length; i++)
        {
            var edge = _edges[i]; double bias = _bias[i];
            var vector = (_positions[edge.Target] - _positions[edge.Source]).Jiggled(ref _random);
            double length = vector.Length;
            length = (length - _parameters.LinkDistance) / length * _parameters.LinkStrength * VelocityDecay * _alpha;
            vector *= length;
            _velocities[edge.Target] -= vector * bias; _velocities[edge.Source] += vector * (1 - bias);
        }
    }
    private void Position()
    {
        // The original composition applies every x lane before every y lane.
        for (int i = 0; i < _positions.Length; i++)
            _velocities[i] = _velocities[i] with { X = _velocities[i].X + (0 - _positions[i].X) * _parameters.Center * _alpha };
        for (int i = 0; i < _positions.Length; i++)
            _velocities[i] = _velocities[i] with { Y = _velocities[i].Y + (0 - _positions[i].Y) * _parameters.Center * _alpha };
    }
    private void Collide()
    {
        _collisionTree.Reset(_positions);
        for (int i = 0; i < _positions.Length; i++)
        {
            int particle = i; var position = _positions[i] + _velocities[i];
            _collisionTree.Visit(node =>
            {
                if (node.Indices is not null)
                {
                    foreach (int j in node.Indices)
                    {
                        if (j <= particle) continue;
                        var delta = position - (_positions[j] + _velocities[j]);
                        if (delta.LengthSquared < 56 * 56)
                        {
                            double length = delta.Jiggled(ref _random).Length;
                            length = (56 - length) / length * 1;
                            delta *= length;
                            _velocities[particle] += delta * .5; _velocities[j] -= delta * (1 - .5);
                        }
                    }
                    return false;
                }
                // Empty delegates have radius zero; internal delegates have the fixed radius28.
                double radius = (node.Count == 0 ? 0 : 28) + 28;
                return !(node.Low.X > position.X + radius || node.Low.Y > position.Y + radius
                    || node.High.X < position.X - radius || node.High.Y < position.Y - radius);
            });
        }
    }
}
