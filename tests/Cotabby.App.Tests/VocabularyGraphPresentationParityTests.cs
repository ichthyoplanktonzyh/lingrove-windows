using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Tests;

public sealed class VocabularyGraphPresentationParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "vocabulary-graph-presentation-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Where(row => row.GetProperty("id").GetString()!.StartsWith("p-", StringComparison.Ordinal))
        .Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalWindowStatePreservesFocusExpansionCommandsAndDurableSettingsOnly(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(34, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/UI/Vocabulary/VocabularyGraphPresentation.swift", row.GetProperty("macSource").GetString());
        var preferences = new MemoryPreferences(row.TryGetProperty("data", out var data) ? Convert.FromBase64String(data.GetString()!) : null);
        var p = new VocabularyGraphPresentation(preferences); var actual = new JsonArray { Describe(p, preferences, null) };
        foreach (var action in row.GetProperty("actions").EnumerateArray())
        {
            ulong? epoch = null;
            switch (action.GetProperty("op").GetString())
            {
                case "toggle": p.Toggle(Uuid(action.GetProperty("entry").GetInt32())); break;
                case "reveal": p.Reveal(action.TryGetProperty("entry", out var entry) ? new(Uuid(entry.GetInt32()), Uuid(action.GetProperty("sense").GetInt32())) : null); break;
                case "collapse": p.Collapse(); break;
                case "clear": p.ClearFocus(); break;
                case "send": p.Send(Enum.Parse<VocabularyGraphCommand>(action.GetProperty("command").GetString()!, true)); break;
                case "epoch": epoch = p.NextEpoch(); break;
                case "settings": p.Settings = VocabularyGraphSettingsCodec.Decode(Convert.FromBase64String(action.GetProperty("data").GetString()!))!; break;
                case "camera": p.Camera = new() { Scale = 2, Offset = new(30, -40) }; p.HasCamera = true; p.FitsInitialLayout = false; break;
                case "restart": p = new(preferences); break;
                default: throw new InvalidOperationException("Unknown graph presentation action.");
            }
            actual.Add(Describe(p, preferences, epoch));
        }
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    internal sealed class MemoryPreferences(byte[]? original = null) : IVocabularyGraphPreferences
    {
        private byte[]? _data = original?.ToArray();
        public byte[]? ReadSettings() => _data?.ToArray();
        public void WriteSettings(ReadOnlySpan<byte> data) => _data = data.ToArray();
        public string BrowserMode { get; set; } = "list";
    }
    internal static Guid Uuid(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static JsonNode? Describe(VocabularyGraphPresentation p, MemoryPreferences preferences, ulong? epoch)
    {
        var data = preferences.ReadSettings(); JsonNode? saved = null;
        // Saved raw data may be corrupt. Valid corpus payloads are UTF-8 in presentation cases;
        // codec cases separately exercise the original BOM/UTF-16 decoding boundary.
        if (data is not null) { try { saved = JsonNode.Parse(data); } catch (JsonException) { } }
        var s = p.Settings;
        return JsonSerializer.SerializeToNode(new { expanded = p.Expanded.Select(id => id.ToString("D")).Order(StringComparer.Ordinal), focused = p.FocusedEntryId?.ToString("D"),
            serial = p.CommandSerial, command = p.Command.ToString()[..1].ToLowerInvariant() + p.Command.ToString()[1..],
            settings = new { nodeSize = s.NodeSize, linkWidth = s.LinkWidth, labels = s.Labels.ToString().ToLowerInvariant(), center = s.Center, repulsion = s.Repulsion,
                linkStrength = s.LinkStrength, linkDistance = s.LinkDistance }, hasCamera = p.HasCamera, fitsInitial = p.FitsInitialLayout,
            camera = new { scale = p.Camera.Scale, offset = new[] { p.Camera.Offset.X, p.Camera.Offset.Y } }, epoch, hasData = data is not null, saved });
    }
}
