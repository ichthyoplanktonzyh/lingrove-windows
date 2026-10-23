namespace Cotabby.Core.Vocabulary;

/// <summary>One retained notebook presentation owns one worker, adapting the original Mac actor.
/// Its asynchronous gate admits operations in order; CPU work runs on the thread pool while the
/// mutable simulation, index table, epoch and bounded coordinate cache stay behind that gate.
/// Only immutable frames cross back to a canvas. Suspend releases particles while retaining the
/// window's coordinate cache; a delayed close cannot suspend a newer epoch.</summary>
public sealed class VocabularyGraphLayoutWorker
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VocabularyGraphSimulation? _simulation;
    private VocabularyGraphNodeId[] _ids = [];
    private Dictionary<VocabularyGraphNodeId, int> _indices = [];
    private Dictionary<VocabularyGraphNodeId, VocabularyGraphPoint> _cached = [];
    private ulong _epoch;
    private VocabularyGraphSnapshot _graph = new();
    private VocabularyGraphSettings _settings = new();

    public Task<VocabularyGraphFrame?> ConfigureAsync(VocabularyGraphSnapshot graph, VocabularyGraphSettings settings, ulong epoch) =>
        SerialAsync(() => Configure(graph, settings, epoch));
    public Task<VocabularyGraphFrame?> AdvanceAsync(ulong epoch, VocabularyGraphNodeId? pinned = null, VocabularyGraphPoint? position = null) =>
        SerialAsync(() => epoch == _epoch && _simulation is not null
            ? Frame(_simulation.Step(pinned is { } id && _indices.TryGetValue(id, out int index) ? index : null, position)) : null);
    public async Task SuspendAsync(ulong epoch, VocabularyGraphNodeId? finalNode = null, VocabularyGraphPoint? finalPosition = null) =>
        await SerialAsync(() => { Suspend(epoch, finalNode, finalPosition); return true; }).ConfigureAwait(false);

    private async Task<T> SerialAsync<T>(Func<T> operation)
    {
        // Take the asynchronous gate before scheduling CPU work: a queued suspend/configure
        // retains call order, and even an uncontended call cannot run a solver tick on WPF's UI.
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(operation).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private VocabularyGraphFrame? Configure(VocabularyGraphSnapshot graph, VocabularyGraphSettings settings, ulong epoch)
    {
        if (epoch < _epoch) return null;
        _epoch = epoch; settings = settings.Normalized;
        bool forceChanged = _settings.Center != settings.Center || _settings.Repulsion != settings.Repulsion
            || _settings.LinkStrength != settings.LinkStrength || _settings.LinkDistance != settings.LinkDistance;
        _settings = settings;
        var nextIds = graph.Nodes.Select(node => node.Id).ToArray();
        bool topologyChanged = !nextIds.SequenceEqual(_ids) || !graph.Edges.SequenceEqual(_graph.Edges);
        _graph = graph;
        if (!topologyChanged && !forceChanged && _simulation is not null) return Frame();
        Dictionary<VocabularyGraphNodeId, VocabularyGraphPoint> velocities = [];
        if (_simulation?.Current is { } previous)
            for (int i = 0; i < _ids.Length; i++) { _cached[_ids[i]] = previous.Positions[i]; velocities[_ids[i]] = previous.Velocities[i]; }
        _ids = nextIds;
        _indices = _ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        if (_ids.Length == 0) { _simulation = null; return new(); }
        var positions = new VocabularyGraphPoint[_ids.Length];
        for (int i = 0; i < _ids.Length; i++)
        {
            var id = _ids[i]; double angle = i * 2.399963229728653, radius = 60 * Math.Sqrt(i + 1);
            double x = Math.Cos(angle), y = Math.Sin(angle);
            if (_cached.TryGetValue(id, out var old)) positions[i] = old;
            else if (id.Selection is not null)
            {
                var parent = _cached.GetValueOrDefault(VocabularyGraphNodeId.Headword(id.EntryId));
                positions[i] = new(parent.X + x * settings.LinkDistance, parent.Y + y * settings.LinkDistance);
            }
            else positions[i] = new(x * radius, y * radius);
        }
        var links = _graph.Edges.Where(edge => _indices.ContainsKey(edge.Source) && _indices.ContainsKey(edge.Target))
            .Select(edge => new VocabularyGraphSimulation.Edge(_indices[edge.Source], _indices[edge.Target])).ToArray();
        _simulation = new(positions, _ids.Select(id => velocities.GetValueOrDefault(id)).ToArray(), links,
            new(settings.Center, settings.Repulsion, settings.LinkStrength, settings.LinkDistance));
        if (_cached.Count > Math.Max(4096, _ids.Length * 2)) _cached = _cached.Where(pair => _indices.ContainsKey(pair.Key)).ToDictionary();
        return Frame();
    }
    private void Suspend(ulong epoch, VocabularyGraphNodeId? finalNode, VocabularyGraphPoint? finalPosition)
    {
        if (epoch < _epoch) return;
        _epoch = epoch;
        if (_simulation?.Current is { } snapshot)
            for (int i = 0; i < _ids.Length; i++) _cached[_ids[i]] = snapshot.Positions[i];
        if (finalNode is { } id && finalPosition is { } position) _cached[id] = position;
        if (_cached.Count > 4096)
        {
            var retained = _ids.Take(4096).ToHashSet();
            _cached = _cached.Where(pair => retained.Contains(pair.Key)).ToDictionary();
        }
        _simulation = null; _ids = []; _indices = []; _graph = new();
    }
    private VocabularyGraphFrame Frame(VocabularyGraphSimulation.Snapshot? value = null)
    {
        var snapshot = value ?? _simulation?.Current;
        if (snapshot is null) return new();
        Dictionary<VocabularyGraphNodeId, VocabularyGraphPoint> positions = [];
        for (int i = 0; i < _ids.Length; i++)
        {
            var point = snapshot.Positions[i];
            positions[_ids[i]] = double.IsFinite(point.X) && double.IsFinite(point.Y) ? point : default;
        }
        return new() { Positions = positions, IsRunning = snapshot.IsRunning };
    }
}
