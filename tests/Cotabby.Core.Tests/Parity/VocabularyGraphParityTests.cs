using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Original graph projection/model/camera observations. Recorded platform substring
/// facts isolate deterministic graph rules; a small separate subset exercises the actual locale
/// adapter. Neither suite claims a layout solver, canvas, persisted preferences or native frames.</summary>
public sealed class VocabularyGraphParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-graph-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalGraphKeepsIdentityProjectionSettingsAndCameraRules(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(45, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Row(id); Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal(id.StartsWith("p-", StringComparison.Ordinal) ? "Cotabby/Support/Vocabulary/VocabularyGraphProjection.swift" : "Cotabby/Models/Vocabulary/VocabularyGraphModels.swift", row.GetProperty("macSource").GetString());
        AssertJson(JsonNode.Parse(row.GetProperty("expected").GetRawText()), JsonSerializer.SerializeToNode(Observe(row)));
    }
    [Theory]
    [InlineData("p-head-query")]
    [InlineData("p-head-accent")]
    [InlineData("p-sense-accent")]
    public void ActualCaseOnlyHeadwordAdapterMatchesTheExplicitSourceSubset(string id)
    {
        var search = new WindowsVocabularyTextSearch(CultureInfo.GetCultureInfo("en-US"));
        foreach (var fact in Row(id).GetProperty("headFacts").EnumerateArray())
            Assert.Equal(fact.GetProperty("contains").GetBoolean(), search.HeadwordContains(fact.GetProperty("field").GetString()!, fact.GetProperty("term").GetString()!));
    }
    private static JsonElement Row(string id) => Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
    private sealed class RecordedFacts(JsonElement row) : IVocabularyGraphTextSearching
    {
        public bool HeadwordContains(string field, string term) => Lookup("headFacts", field, term);
        public bool Contains(string field, string term) => Lookup("senseFacts", field, term);
        private bool Lookup(string key, string field, string term)
        {
            var values = row.GetProperty(key).EnumerateArray().Where(value => value.GetProperty("field").GetString() == field && value.GetProperty("term").GetString() == term)
                .Select(value => value.GetProperty("contains").GetBoolean()).Distinct().ToArray();
            if (values.Length != 1) throw new InvalidOperationException("Missing or inconsistent actual Mac graph search fact.");
            return values[0];
        }
    }
    private static object Identity(VocabularyGraphNodeId id) => new { entry = id.EntryId.ToString("D"), sense = id.SenseId?.ToString("D") };
    private static string Key(VocabularyGraphNodeId id) => id.EntryId.ToString("D") + (id.SenseId?.ToString("D") ?? "");
    private static double[] Point(VocabularyGraphPoint point) => [point.X, point.Y];
    private static object Camera(VocabularyGraphCamera camera) => new { scale = camera.Scale, offset = Point(camera.Offset) };
    private static object Observe(JsonElement row)
    {
        string mode = row.GetProperty("id").GetString()!;
        if (mode.StartsWith("p-", StringComparison.Ordinal))
        {
            var document = VocabularyBackupCodec.DecodeDocument(Encoding.UTF8.GetBytes(row.GetProperty("document").GetRawText()));
            var expanded = row.GetProperty("expanded").EnumerateArray().Select(value => value.GetGuid()).ToHashSet();
            var graph = VocabularyGraphProjection.Make(document.Entries, expanded, row.GetProperty("query").GetString()!, new RecordedFacts(row));
            return new { nodes = graph.Nodes.Select(node => new { id = Identity(node.Id), label = node.Label }),
                edges = graph.Edges.Select(edge => new { source = Identity(edge.Source), target = Identity(edge.Target) }),
                headwordCount = graph.HeadwordCount, matches = graph.Matches.OrderBy(Key, StringComparer.Ordinal).Select(Identity), query = graph.SearchQuery };
        }
        if (mode.StartsWith("s-", StringComparison.Ordinal))
        {
            var settings = mode switch
            {
                "s-low" => new() { NodeSize = -1, LinkWidth = -1, Center = -1, Repulsion = -1, LinkStrength = -1, LinkDistance = -1 },
                "s-high" => new() { NodeSize = 9999, LinkWidth = 9999, Center = 9999, Repulsion = 9999, LinkStrength = 9999, LinkDistance = 9999 },
                "s-finite" => new() { NodeSize = 21.5, LinkWidth = 2.3, Center = .04, Repulsion = 333, LinkStrength = .3, LinkDistance = 140 },
                "s-nonfinite" => new() { NodeSize = double.PositiveInfinity, LinkWidth = double.NegativeInfinity, Center = double.NaN,
                    Repulsion = double.NaN, LinkStrength = double.PositiveInfinity, LinkDistance = double.NegativeInfinity },
                "s-label-all" => new() { Labels = VocabularyGraphLabels.All }, "s-label-focused" => new() { Labels = VocabularyGraphLabels.Focused },
                _ => new VocabularyGraphSettings()
            };
            var safe = settings.Normalized;
            return new { nodeSize = safe.NodeSize, linkWidth = safe.LinkWidth, labels = safe.Labels.ToString().ToLowerInvariant(), center = safe.Center,
                repulsion = safe.Repulsion, linkStrength = safe.LinkStrength, linkDistance = safe.LinkDistance };
        }
        var camera = new VocabularyGraphCamera(); var size = new VocabularyGraphSize(900, 600); var pointer = new VocabularyGraphPoint(123, 233);
        if (mode == "c-screen") return new { point = Point(camera.Screen(new(10, -20), size)) };
        if (mode == "c-world") return new { point = Point((camera with { Scale = 2, Offset = new(30, -40) }).World(pointer, size)) };
        if (mode.StartsWith("c-zoom", StringComparison.Ordinal))
        {
            double factor = mode switch { "c-zoom-min" => .0001, "c-zoom-max" => 100, "c-zoom-zero" => 0, "c-zoom-negative" => -1,
                "c-zoom-nan" => double.NaN, "c-zoom-infinite" => double.PositiveInfinity, _ => 2 };
            var before = camera.World(pointer, size); camera = camera.Zoom(factor, pointer, size);
            return new { camera = Camera(camera), before = Point(before), after = Point(camera.World(pointer, size)) };
        }
        VocabularyGraphPoint[] positions = [new(-100, 50), new(200, -75)];
        switch (mode)
        {
            case "c-fit-empty": positions = []; camera = camera with { Scale = 1.3, Offset = new(12, 34) }; break;
            case "c-fit-single": positions = [new(400, -50)]; break;
            case "c-fit-reverse": Array.Reverse(positions); break;
            case "c-fit-small": size = new(100, 80); break;
            case "c-fit-zero-width": size = new(0, 600); break;
            case "c-fit-zero-height": size = new(900, 0); break;
            case "c-fit-large": positions = [new(-10000, -5000), new(10000, 5000)]; break;
            case "c-fit-repeated": positions = [new(400, -50), new(400, -50)]; break;
        }
        camera = camera.Fit(positions, size); return new { camera = Camera(camera), screen = positions.Select(point => Point(camera.Screen(point, size))) };
    }
    private static void AssertJson(JsonNode? expected, JsonNode? actual)
    {
        if (expected is JsonObject left && actual is JsonObject right)
        { Assert.Equal(left.Select(pair => pair.Key).Order(), right.Select(pair => pair.Key).Order()); foreach (var pair in left) AssertJson(pair.Value, right[pair.Key]); return; }
        if (expected is JsonArray a && actual is JsonArray b)
        { Assert.Equal(a.Count, b.Count); for (int i = 0; i < a.Count; i++) AssertJson(a[i], b[i]); return; }
        if (expected is JsonValue x && actual is JsonValue y && x.GetValueKind() == JsonValueKind.Number && y.GetValueKind() == JsonValueKind.Number)
        { double value = x.GetValue<double>(), other = y.GetValue<double>(); Assert.True(Math.Abs(value - other) <= Math.Max(1e-9, Math.Abs(value) * 1e-12), $"{value:R} != {other:R}"); return; }
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{expected} != {actual}");
    }
}
