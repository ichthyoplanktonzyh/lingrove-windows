using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.Core.Review;

namespace Cotabby.App.Tests;

public sealed class AnkiReviewSettingsTests
{
    private static readonly string[] Keys = ["ankiReviewAddress", "ankiReviewDeck", "ankiReviewModel", "ankiReviewMapping", "ankiReviewRecognition", "ankiReviewGap"];
    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "anki-settings-v1.json")));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", document.RootElement.GetProperty("macReference").GetString());
        Assert.Equal(60, document.RootElement.GetProperty("cases").GetArrayLength());
        Assert.Equal("fa6f2af61d65b4abe76140b381fd606ea8c79d98bf915512d481ec7dd5dae5c4", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", document.RootElement.GetProperty("cases").EnumerateArray().Select(r => Text(r, "id")).Order(StringComparer.Ordinal))))));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public void CompleteOriginalSettingsTrace(string id, JsonElement row)
    {
        var current = new AppSettings(); var credentials = new MemoryCredentials(); int saves = 0;
        if (row.TryGetProperty("seed", out var seeds)) foreach (var seed in seeds.EnumerateObject())
        {
            var descriptor = seed.Value; string type = Text(descriptor, "type");
            var value = type == "array" ? JsonSerializer.SerializeToElement(new[] { "unexpected" }) : descriptor.GetProperty("value").Clone();
            SetRaw(current, seed.Name, value);
        }
        var settings = new AnkiReviewSettings(() => current, () => saves++, credentials); var trace = new List<object> { Snapshot(settings, current) };
        if (row.TryGetProperty("actions", out var actions)) foreach (var action in actions.EnumerateArray())
        {
            object? outcome = null;
            try
            {
                switch (Text(action, "op"))
                {
                    case "address": settings.Address = Text(action, "value"); break;
                    case "deck": settings.Deck = Text(action, "value"); break;
                    case "model": settings.Model = Text(action, "value"); break;
                    case "mapping": settings.Mapping = action.GetProperty("value").EnumerateObject().ToDictionary(p => Field(p.Name), p => p.Value.GetString()!); break;
                    case "recognition": settings.DefaultRecognition = action.GetProperty("value").GetBoolean(); break;
                    case "gap": settings.DefaultGap = action.GetProperty("value").GetBoolean(); break;
                    case "saveKey": settings.SaveKey(Text(action, "value")); break;
                    case "readKey": outcome = new { key = settings.ReadKey() }; break;
                    case "connection": var connection = settings.Connection(); outcome = new { endpoint = connection.Endpoint, key = connection.ApiKey }; break;
                    case "restart":
                        current = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(current))!;
                        settings = new AnkiReviewSettings(() => current, () => saves++, credentials); break;
                    default: throw new InvalidOperationException("Unknown action");
                }
            }
            catch (AnkiReviewException error) { outcome = new { error = error.Message }; }
            trace.Add(new { state = Snapshot(settings, current), result = outcome });
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(trace);
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\nExpected: " + expected + "\nActual: " + actual);
    }
    private static string Text(JsonElement r, string key) => r.GetProperty(key).GetString()!;
    private static ReviewField Field(string key) => JsonSerializer.Deserialize<ReviewField>(JsonSerializer.Serialize(key));
    private static string FieldName(ReviewField key) => JsonSerializer.SerializeToElement(key).GetString()!;
    private static object Snapshot(AnkiReviewSettings s, AppSettings current)
    {
        Dictionary<string, string>? saved = null; bool? isArray = null;
        if (current.AnkiReviewMappingValue is { ValueKind: JsonValueKind.String } data && data.TryGetBytesFromBase64(out var bytes))
        {
            if (ReviewMappingCodec.Decode(bytes) is { } decoded) saved = decoded.ToDictionary(p => FieldName(p.Key), p => p.Value);
            try { using var document = JsonDocument.Parse(bytes); isArray = document.RootElement.ValueKind == JsonValueKind.Array; }
            catch (JsonException) { isArray = false; }
        }
        return new { address = s.Address, deck = s.Deck, model = s.Model, mapping = s.Mapping.ToDictionary(p => FieldName(p.Key), p => p.Value),
            effectiveMapping = s.EffectiveMapping.ToDictionary(p => FieldName(p.Key), p => p.Value), recognition = s.DefaultRecognition, gap = s.DefaultGap,
            hasSavedDeck = s.HasSavedDeck, savedKeys = Keys.Where(key => Raw(current, key).HasValue).ToArray(), savedMapping = saved, savedMappingIsArray = isArray };
    }
    private static JsonElement? Raw(AppSettings s, string key) => key switch
    { "ankiReviewAddress" => s.AnkiReviewAddressValue, "ankiReviewDeck" => s.AnkiReviewDeckValue, "ankiReviewModel" => s.AnkiReviewModelValue, "ankiReviewMapping" => s.AnkiReviewMappingValue, "ankiReviewRecognition" => s.AnkiReviewRecognitionValue, "ankiReviewGap" => s.AnkiReviewGapValue, _ => throw new InvalidOperationException() };
    private static void SetRaw(AppSettings s, string key, JsonElement value)
    {
        switch (key)
        {
            case "ankiReviewAddress": s.AnkiReviewAddressValue = value; break; case "ankiReviewDeck": s.AnkiReviewDeckValue = value; break;
            case "ankiReviewModel": s.AnkiReviewModelValue = value; break; case "ankiReviewMapping": s.AnkiReviewMappingValue = value; break;
            case "ankiReviewRecognition": s.AnkiReviewRecognitionValue = value; break; case "ankiReviewGap": s.AnkiReviewGapValue = value; break;
            default: throw new InvalidOperationException();
        }
    }
    [Fact]
    public void CredentialsAreReadOnlyByExplicitActionsAfterEndpointAdmissionAndNeverPersisted()
    {
        var current = new AppSettings(); var key = new MemoryCredentials(); int saves = 0;
        var settings = new AnkiReviewSettings(() => current, () => saves++, key);
        _ = settings.Mapping; _ = settings.EffectiveMapping; Assert.Equal(0, key.Reads); Assert.Equal(0, saves);
        settings.SaveKey("  fixture-secret  "); Assert.Equal("  fixture-secret  ", key.Key); Assert.Equal(0, key.Reads); Assert.Equal(0, saves);
        settings.Address = "http://example.com"; key.FailRead = true;
        Assert.Equal("Use a local AnkiConnect HTTP address.", Assert.Throws<AnkiReviewException>(() => settings.Connection()).Message); Assert.Equal(0, key.Reads);
        settings.Address = "http://127.0.0.1:8765";
        Assert.Equal("test credential failure", Assert.Throws<AnkiReviewException>(() => settings.Connection()).Message); Assert.Equal(1, key.Reads);
        key.FailRead = false; Assert.Equal("  fixture-secret  ", settings.Connection().ApiKey); Assert.DoesNotContain("fixture-secret", JsonSerializer.Serialize(current));
        settings.SaveKey(""); Assert.Equal("", settings.ReadKey()); Assert.Equal(2, saves);
    }
    [Fact]
    public void GeneralResetPreservesAllRawReviewPreferencesAndOwnerFollowsReplacement()
    {
        var current = new AppSettings(); using var raw = JsonDocument.Parse("{\"wrong\":true}");
        foreach (string name in Keys) SetRaw(current, name, raw.RootElement.Clone());
        var saved = Keys.ToDictionary(k => k, k => Raw(current, k)!.Value.GetRawText());
        var settings = new AnkiReviewSettings(() => current, () => { }, new MemoryCredentials()); current = current.ResetGeneralPreferences(42);
        foreach (var pair in saved) Assert.Equal(pair.Value, Raw(current, pair.Key)!.Value.GetRawText());
        Assert.Equal("http://127.0.0.1:8765", settings.Address); settings.Deck = "Chosen"; Assert.Equal("Chosen", current.AnkiReviewDeckValue!.Value.GetString());
        Assert.Equal(42, current.WordsAccepted);
    }
    [Fact]
    public void PreferenceSaveFailureKeepsEditedStateAndOriginalMappingArrayWithoutReadingKeys()
    {
        var current = new AppSettings(); var credentials = new MemoryCredentials(); int writes = 0; var changes = new List<string?>();
        var settings = new AnkiReviewSettings(() => current, () => { writes++; throw new IOException("synthetic preference destination failure"); }, credentials);
        settings.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        settings.Deck = "Chosen"; settings.Model = "Custom";
        settings.Mapping = new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID", [ReviewField.Expression] = "Q", [ReviewField.Meaning] = "A" };
        Assert.Equal(3, writes); Assert.Equal(0, credentials.Reads); Assert.Equal("Chosen", settings.Deck); Assert.True(settings.HasSavedDeck);
        Assert.Equal("A", settings.EffectiveMapping[ReviewField.Meaning]); Assert.Contains(nameof(AnkiReviewSettings.EffectiveMapping), changes);
        var encoded = current.AnkiReviewMappingValue!.Value.GetBytesFromBase64(); using var mapping = JsonDocument.Parse(encoded);
        Assert.Equal(JsonValueKind.Array, mapping.RootElement.ValueKind); Assert.Equal(6, mapping.RootElement.GetArrayLength());
        var replacement = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(current))!;
        Assert.Equal("A", new AnkiReviewSettings(() => replacement, () => { }, credentials).Mapping[ReviewField.Meaning]);
    }
    private sealed class MemoryCredentials : IAnkiCredentialStore
    {
        public string Key { get; private set; } = ""; public int Reads { get; private set; } public bool FailRead { get; set; }
        public string ReadKey() { Reads++; if (FailRead) throw new AnkiReviewException("test credential failure"); return Key; }
        public void SaveKey(string value) => Key = value;
    }
}
