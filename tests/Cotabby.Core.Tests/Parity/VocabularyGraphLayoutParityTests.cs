using System.Text.Json;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Complete original GraphSimulation/Grape snapshots from the pinned Swift package.
/// Both particle arrays and running state are checked at each milestone, including cooling,
/// pin handoff, clustered/quadrant trees, degree bias and immediate position mutation.</summary>
public sealed class VocabularyGraphLayoutParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-graph-layout-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedOriginalPackageKeepsBothParticleArraysAndRunningState(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(31, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Packages/LingroveGraphLayout/Sources/LingroveGraphLayout/GraphSimulation.swift", row.GetProperty("macSource").GetString());
        var input = row.GetProperty("input"); var p = input.GetProperty("parameters");
        var simulation = new VocabularyGraphSimulation(Points(input.GetProperty("positions")), Points(input.GetProperty("velocities")),
            input.GetProperty("edges").EnumerateArray().Select(edge => new VocabularyGraphSimulation.Edge(edge[0].GetInt32(), edge[1].GetInt32())).ToArray(),
            new(p.GetProperty("center").GetDouble(), p.GetProperty("repulsion").GetDouble(), p.GetProperty("linkStrength").GetDouble(), p.GetProperty("linkDistance").GetDouble()));
        var actual = new List<VocabularyGraphSimulation.Snapshot> { simulation.Current };
        foreach (var step in input.GetProperty("steps").EnumerateArray())
        {
            int? pinned = step.TryGetProperty("pinned", out var index) ? index.GetInt32() : null;
            VocabularyGraphPoint? target = step.TryGetProperty("position", out var position) ? Point(position) : null;
            var snapshot = simulation.Current;
            for (int i = 0; i < step.GetProperty("count").GetInt32(); i++) snapshot = simulation.Step(pinned, target);
            actual.Add(snapshot);
        }
        var expected = row.GetProperty("expected"); Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            Assert.Equal(expected[i].GetProperty("running").GetBoolean(), actual[i].IsRunning);
            AssertPoints(id, i, "positions", expected[i].GetProperty("positions"), actual[i].Positions);
            AssertPoints(id, i, "velocities", expected[i].GetProperty("velocities"), actual[i].Velocities);
        }
    }
    private static VocabularyGraphPoint Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
    private static VocabularyGraphPoint[] Points(JsonElement value) => value.EnumerateArray().Select(Point).ToArray();
    private static void AssertPoints(string id, int snapshot, string kind, JsonElement expected, IReadOnlyList<VocabularyGraphPoint> actual)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            AssertNumber(expected[i][0].GetDouble(), actual[i].X, $"{id}/{snapshot}/{kind}/{i}/x");
            AssertNumber(expected[i][1].GetDouble(), actual[i].Y, $"{id}/{snapshot}/{kind}/{i}/y");
        }
    }
    private static void AssertNumber(double expected, double actual, string path) => Assert.True(double.IsFinite(actual)
        && Math.Abs(expected - actual) <= Math.Max(1e-8, Math.Abs(expected) * 1e-10), $"{path}: expected {expected:R}, actual {actual:R}");
}
