using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

public sealed class AnkiFieldsParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "anki-fields-v1.json"))).RootElement.Clone();
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { Text(row, "id") });
    [Theory, MemberData(nameof(Cases))]
    public void OriginalFieldRules(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(76, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        Assert.Equal("d07dcfb0c6b28be1e9e1f4e13e4a5dbc25323b63e1b76079ceb5d6d844dd859f", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Corpus.GetProperty("cases").EnumerateArray().Select(r => Text(r, "id")).Order(StringComparer.Ordinal))))));
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => Text(r, "id") == id);
        Assert.Equal("Cotabby/Support/Review/AnkiReviewPolicy.swift", Text(row, "macSource")); Assert.Equal("source-policy-extension", Text(row, "provenanceKind"));
        object actual;
        try { actual = new { valid = true, value = Observe(row) }; }
        catch (AnkiReviewException error) { actual = new { valid = false, error = error.Message }; }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var result = JsonSerializer.SerializeToNode(actual);
        Assert.True(JsonNode.DeepEquals(expected, result), id + "\nExpected: " + expected + "\nActual: " + result);
    }
    private static string Text(JsonElement row, string key, string fallback = "") => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;
    private static bool Flag(JsonElement row, string key, bool fallback) => row.TryGetProperty(key, out var value) ? value.GetBoolean() : fallback;
    private static Dictionary<string, string> Map(JsonElement row, string key) => row.TryGetProperty(key, out var value)
        ? value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!) : [];
    private static ReviewField Field(string text) => JsonSerializer.Deserialize<ReviewField>(JsonSerializer.Serialize(text));
    private static string FieldName(ReviewField field) => JsonSerializer.SerializeToElement(field).GetString()!;
    private static IReadOnlyDictionary<ReviewField, string> FieldMap(JsonElement row, string key, bool preset = false) => row.TryGetProperty(key, out _)
        ? Map(row, key).ToDictionary(p => Field(p.Key), p => p.Value) : preset ? AnkiReviewPolicy.PresetMapping : new Dictionary<ReviewField, string>();
    private static VocabularyContentBlock[] Blocks() =>
    [
        new(Id(7), new VocabularyBlockContent.Text(new("A <hint>&"))),
        new(Id(8), new VocabularyBlockContent.Image(new(new string('a', 64), 2, 3, 4), "image<&>")),
    ];
    private static ReviewResolvedPart[]? Parts(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.EnumerateArray().Select(p => Text(p, "kind") switch
    {
        "text" => (ReviewResolvedPart)new ReviewResolvedPart.Text(Text(p, "text")),
        "audio" => new ReviewResolvedPart.Audio(Enum.Parse<ReviewAudioKind>(Text(p, "audio"), true)),
        _ => new ReviewResolvedPart.Blocks(Blocks()),
    }).ToArray() : null;
    private static ReviewCardContent Material(JsonElement row) => new(Text(row, "expression", "bank<&>"), Text(row, "answer", "answer<&>"), "The bank.", [new(4, 4)],
        "translation", "explanation", "notes", "The ___.", "bank", Flag(row, "recognition", true), Flag(row, "gap", true))
        { SenseContent = Flag(row, "blocks", false) ? Blocks() : [], Front = Parts(row, "front"), Back = Parts(row, "back") };
    private static ReviewPlan Plan(JsonElement row) => new(Id(1))
    {
        Recognition = Flag(row, "recognition", true), Gap = Flag(row, "gap", true),
        AudioKinds = row.TryGetProperty("audioKinds", out var kinds) ? kinds.EnumerateArray().Select(v => Enum.Parse<ReviewAudioKind>(v.GetString()!, true)).ToHashSet() : null,
        AudioReceipts = row.TryGetProperty("receipts", out var receipts) ? receipts.EnumerateObject().ToDictionary(p => Enum.Parse<ReviewAudioKind>(p.Name, true),
            p => new ReviewAudioReceipt("fingerprint", "hash", "http://localhost", "profile", p.Value.GetString()!)) : null,
    };
    private static object Observe(JsonElement row)
    {
        switch (Text(row, "kind", "fields"))
        {
            case "preset": return new { name = AnkiReviewPolicy.PresetName, mapping = AnkiReviewPolicy.PresetMapping.ToDictionary(p => FieldName(p.Key), p => p.Value) };
            case "conflicts": return AnkiReviewPolicy.Conflicts(Map(row, "base"), Map(row, "local"), Map(row, "remote")).Select(c => new { id = c.Id, local = c.Local, remote = c.Remote }).ToArray();
            case "removes":
                var binding = new AnkiNoteBinding(Id(3), Id(2), "http://localhost", "profile", Flag(row, "hasNote", true) ? 10 : null,
                    "model", "deck", "LingroveID", FieldMap(row, "mapping", true), Map(row, "fields"));
                return AnkiReviewPolicy.RemovesExistingCard(Plan(row), binding);
            case "audio": return AnkiReviewPolicy.AudioFields(Plan(row)).ToDictionary(p => FieldName(p.Key), p => p.Value);
            default:
                var overrides = Map(row, "overrides");
                if (row.TryGetProperty("repeatCount", out var repeat)) overrides[Text(row, "repeatField", "Meaning")] = string.Concat(Enumerable.Repeat(Text(row, "repeatText", "a"), repeat.GetInt32()));
                return AnkiReviewPolicy.Fields(Material(row), Id(2), FieldMap(row, "mapping", true), overrides, FieldMap(row, "audio")).ToDictionary(p => p.Key, p =>
                    Encoding.UTF8.GetByteCount(p.Value) <= 4096 ? (object)p.Value : new { bytes = Encoding.UTF8.GetByteCount(p.Value), sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(p.Value))) });
        }
    }
    [Fact]
    public void FieldAndConflictOutputsOwnTheirInputsAndUseCanonicalValueEquality()
    {
        var mapping = new Dictionary<ReviewField, string>(AnkiReviewPolicy.PresetMapping); var overrides = new Dictionary<string, string> { ["Meaning"] = "remote" };
        using var row = JsonDocument.Parse("{}"); var fields = AnkiReviewPolicy.Fields(Material(row.RootElement), Id(2), mapping, overrides);
        mapping.Clear(); overrides["Meaning"] = "changed"; Assert.Equal("remote", fields["Meaning"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)fields)["Meaning"] = "bad");
        var local = new Dictionary<string, string> { ["é"] = "mine" }; var remote = new Dictionary<string, string> { ["e\u0301"] = "theirs" };
        var conflicts = AnkiReviewPolicy.Conflicts(new Dictionary<string, string>(), local, remote); local.Clear(); remote.Clear();
        Assert.Single(conflicts); Assert.Equal(new AnkiFieldConflict("e\u0301", "mine", "theirs"), conflicts[0]);
        Assert.Equal(new AnkiFieldConflict("é", "mine", "theirs").GetHashCode(), conflicts[0].GetHashCode());
        Assert.Throws<NotSupportedException>(() => ((IList<AnkiFieldConflict>)conflicts).Clear());
    }
    [Fact]
    public void UndefinedNetFieldsAndMalformedMappedTextFailBeforeUse()
    {
        using var row = JsonDocument.Parse("{}"); var mapping = new Dictionary<ReviewField, string>(AnkiReviewPolicy.PresetMapping) { [(ReviewField)999] = "unexpected" };
        Assert.Throws<AnkiReviewException>(() => AnkiReviewPolicy.Fields(Material(row.RootElement), Id(2), mapping, new Dictionary<string, string>()));
        mapping.Remove((ReviewField)999); mapping[ReviewField.Meaning] = null!;
        Assert.Throws<AnkiReviewException>(() => AnkiReviewPolicy.Fields(Material(row.RootElement), Id(2), mapping, new Dictionary<string, string>()));
        mapping[ReviewField.Meaning] = "\ud800";
        Assert.Throws<AnkiReviewException>(() => AnkiReviewPolicy.Fields(Material(row.RootElement), Id(2), mapping, new Dictionary<string, string>()));
        var plan = new ReviewPlan(Id(1)) { AudioKinds = new HashSet<ReviewAudioKind> { (ReviewAudioKind)999 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => AnkiReviewPolicy.AudioFields(plan));
    }
}
