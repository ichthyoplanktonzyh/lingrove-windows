using System.Text.Json;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Original actor transcripts exercise the real package through expansion, filtering,
/// cooling, epoch changes, close/reopen, and both exact cache thresholds. The 1000-node case
/// compares four positions and checks finiteness/count for the whole frame; it sets no time quota.</summary>
public sealed class VocabularyGraphWorkerParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-graph-worker-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalActorKeepsEpochIdentityCacheAndCoolingAcrossTheTranscript(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(27, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Row(id); Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyGraphLayoutWorker.swift", row.GetProperty("macSource").GetString());
        var probes = row.TryGetProperty("probes", out var probe) ? probe.EnumerateArray().Select(p => p.GetString()!).ToHashSet() : null;
        var worker = new VocabularyGraphLayoutWorker(); var actual = new List<VocabularyGraphFrame?>();
        foreach (var action in row.GetProperty("actions").EnumerateArray())
        {
            ulong epoch = action.GetProperty("epoch").GetUInt64();
            switch (action.GetProperty("op").GetString())
            {
                case "configure": actual.Add(await worker.ConfigureAsync(Graph(action), Settings(action), epoch)); break;
                case "advance":
                    VocabularyGraphNodeId? pinned = action.TryGetProperty("pinned", out var pin) ? Identity(pin.GetString()!) : null;
                    VocabularyGraphPoint? point = action.TryGetProperty("position", out var position) ? Point(position) : null;
                    int count = action.TryGetProperty("count", out var repeat) ? repeat.GetInt32() : 1;
                    VocabularyGraphFrame? frame = null;
                    for (int i = 0; i < count; i++) frame = await worker.AdvanceAsync(epoch, pinned, point);
                    actual.Add(frame); break;
                case "suspend":
                    await worker.SuspendAsync(epoch, action.TryGetProperty("finalNode", out var final) ? Identity(final.GetString()!) : null,
                        action.TryGetProperty("finalPosition", out var finalPoint) ? Point(finalPoint) : null); actual.Add(null); break;
                default: throw new InvalidOperationException("Unknown original worker action.");
            }
        }
        var expected = row.GetProperty("expected"); Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++) AssertFrame(expected[i], actual[i], probes);
    }
    [Fact]
    public async Task ConcurrentlyQueuedCallsKeepTheirAdmissionOrderAndOlderFramesRemainFrozen()
    {
        var input = Row("advance").GetProperty("actions")[0]; var worker = new VocabularyGraphLayoutWorker(); var graph = Graph(input);
        var configure = worker.ConfigureAsync(graph, new(), 1);
        var ticks = Enumerable.Range(0, 20).Select(_ => worker.AdvanceAsync(1)).ToArray();
        var suspend = worker.SuspendAsync(2); var reopen = worker.ConfigureAsync(graph, new(), 3);
        var initial = await configure; var replies = await Task.WhenAll(ticks); await suspend; var restored = await reopen;
        AssertFrame(Row("advance").GetProperty("expected")[0], initial, null);
        AssertFrame(Row("advance").GetProperty("expected")[1], replies[^1], null);
        Assert.NotNull(replies[0]); var firstPositions = replies[0]!.Positions.ToDictionary();
        for (int i = 0; i < 5; i++) _ = await worker.AdvanceAsync(3);
        Assert.Equal(firstPositions.OrderBy(p => Key(p.Key)), replies[0]!.Positions.OrderBy(p => Key(p.Key)));
        Assert.NotNull(restored); Assert.Equal(replies[^1]!.Positions.OrderBy(p => Key(p.Key)), restored.Positions.OrderBy(p => Key(p.Key)));
    }
    [Fact]
    public async Task WorkerDoesNotRequireTheCallersUiSynchronizationContextToComplete()
    {
        var context = new RecordingContext(); var previous = SynchronizationContext.Current; Task<VocabularyGraphFrame?> task;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            task = new VocabularyGraphLayoutWorker().ConfigureAsync(Graph(Row("head-seeds").GetProperty("actions")[0]), new(), 1);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.NotNull(await task.WaitAsync(TimeSpan.FromSeconds(10))); Assert.Equal(0, context.Posts);
    }
    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback d, object? state) { Interlocked.Increment(ref Posts); ThreadPool.QueueUserWorkItem(_ => d(state)); }
    }
    private static JsonElement Row(string id) => Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
    private static Guid Uuid(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static VocabularyGraphNodeId Identity(string key)
    {
        string[] parts = key.Split('_'); int entry = int.Parse(parts[0].AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
        return new(Uuid(entry), parts.Length == 1 ? null : Uuid(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
    }
    private static string Key(VocabularyGraphNodeId id)
    {
        int entry = Convert.ToInt32(id.EntryId.ToString("N")[20..], 16);
        return id.SenseId is { } sense ? $"s{entry}_{Convert.ToInt32(sense.ToString("N")[20..], 16)}" : $"h{entry}";
    }
    private static VocabularyGraphPoint Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
    private static VocabularyGraphSnapshot Graph(JsonElement action)
    {
        string[] keys = action.TryGetProperty("nodes", out var nodes) ? nodes.EnumerateArray().Select(n => n.GetString()!).ToArray()
            : Enumerable.Range(action.TryGetProperty("start", out var start) ? start.GetInt32() : 1, action.GetProperty("count").GetInt32()).Select(n => $"h{n}").ToArray();
        var labels = action.TryGetProperty("labels", out var values) ? values.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!) : [];
        return new() { Nodes = keys.Select(key => new VocabularyGraphNode(Identity(key), labels.GetValueOrDefault(key) ?? key)).ToArray(),
            Edges = action.TryGetProperty("edges", out var edges) ? edges.EnumerateArray().Select(e => new VocabularyGraphEdge(Identity(e[0].GetString()!), Identity(e[1].GetString()!))).ToArray() : [],
            HeadwordCount = keys.Count(key => !key.Contains('_')),
            Matches = action.TryGetProperty("matches", out var matches) ? matches.EnumerateArray().Select(m => Identity(m.GetString()!)).ToHashSet() : new HashSet<VocabularyGraphNodeId>(),
            SearchQuery = action.TryGetProperty("query", out var query) ? query.GetString()! : "" };
    }
    private static VocabularyGraphSettings Settings(JsonElement action)
    {
        var defaults = new VocabularyGraphSettings(); if (!action.TryGetProperty("settings", out var p)) return defaults;
        double Value(string name, double fallback) => p.TryGetProperty(name, out var value) ? value.GetDouble() : fallback;
        return new() { NodeSize = Value("nodeSize", defaults.NodeSize), LinkWidth = Value("linkWidth", defaults.LinkWidth),
            Center = Value("center", defaults.Center), Repulsion = Value("repulsion", defaults.Repulsion),
            LinkStrength = Value("linkStrength", defaults.LinkStrength), LinkDistance = Value("linkDistance", defaults.LinkDistance),
            Labels = p.TryGetProperty("labels", out var label) ? Enum.Parse<VocabularyGraphLabels>(label.GetString()!, true) : defaults.Labels };
    }
    private static void AssertFrame(JsonElement expected, VocabularyGraphFrame? actual, HashSet<string>? probes)
    {
        if (expected.ValueKind == JsonValueKind.Null) { Assert.Null(actual); return; }
        Assert.NotNull(actual); Assert.Equal(expected.GetProperty("count").GetInt32(), actual.Positions.Count);
        Assert.Equal(expected.GetProperty("running").GetBoolean(), actual.IsRunning);
        Assert.Equal(expected.GetProperty("finite").GetBoolean(), actual.Positions.Values.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)));
        var positions = actual.Positions.Where(p => probes is null || probes.Contains(Key(p.Key))).ToDictionary(p => Key(p.Key), p => p.Value);
        var wanted = expected.GetProperty("positions"); Assert.Equal(wanted.EnumerateObject().Select(p => p.Name).Order(), positions.Keys.Order());
        foreach (var p in wanted.EnumerateObject())
        {
            var found = positions[p.Name]; AssertNumber(p.Value[0].GetDouble(), found.X); AssertNumber(p.Value[1].GetDouble(), found.Y);
        }
    }
    private static void AssertNumber(double expected, double actual) => Assert.True(double.IsFinite(actual)
        && Math.Abs(expected - actual) <= Math.Max(1e-8, Math.Abs(expected) * 1e-10), $"{expected:R} != {actual:R}");
}
