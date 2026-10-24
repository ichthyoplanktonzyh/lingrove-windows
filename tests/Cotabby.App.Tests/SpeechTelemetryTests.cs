using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Runtime;
using Cotabby.App.Settings;
using Cotabby.App.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;

namespace Cotabby.App.Tests;

/// <summary>Complete original source traces drive the actual observer, preference Store and
/// codec. Clock/structured log collection are explicit effects. Only generated correlation/row
/// identities and uninjectable telemetry wall time are projected; stage durations remain exact.</summary>
public sealed class SpeechTelemetryTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-telemetry-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Text(Corpus, "macReference"));
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(78, rows.Length);
        Assert.Equal("a7ab20a4021b11b01f253e88eb9f0a03f9c1ebfaf86a36de1258718cb8358cbd", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => Text(r, "id")).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { Text(r, "id") });
    }
    [Theory, MemberData(nameof(Cases))]
    public void CompleteOriginalTelemetryAndHistory(string id)
    {
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => Text(r, "id") == id); string scope = Text(row, "scope");
        if (scope == "metadata")
        {
            var metadata = JsonSerializer.SerializeToNode(new { sources = ModelRequestSources.All.Select(s => new { id = Name(s), label = s.Label() }), analysis = ModelRequestSource.Analysis.Label(), outcomes = new[] { ModelRequestOutcome.Completed, ModelRequestOutcome.Failed, ModelRequestOutcome.Cancelled }.Select(s => new { id = Name(s), label = s.Label() }) });
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), metadata)); return;
        }
        var current = new AppSettings(); if (row.TryGetProperty("seedBase64", out var seed)) current.PerformanceMetricEntriesValue = JsonSerializer.SerializeToElement(seed.GetString());
        var failures = new List<Exception>(); PerformanceMetricsStore NewStore() => new(() => current, () => { }, (error, _) => failures.Add(error));
        PerformanceMetricsStore? store = Flag(row, "noMetrics") ? null : NewStore();
        double clock = 10; bool tracking = Flag(row, "tracking"); int reads = 0; var events = new JsonArray(); var ids = new List<Guid>();
        var tokens = new Dictionary<string, SpeechPlaybackToken>(); var sessions = new Dictionary<string, Guid>();
        SpeechPlaybackToken Token(string key) { if (!tokens.TryGetValue(key, out var value)) tokens[key] = value = new(Id(tokens.Count + 1)); return value; }
        Guid Session(string key) { if (!sessions.TryGetValue(key, out var value)) sessions[key] = value = Id(sessions.Count + 1000); return value; }
        var telemetry = new SpeechTelemetry((context, phase, details) => events.Add(new JsonObject
        {
            ["context"] = new JsonObject { ["requestID"] = context.RequestId, ["operationID"] = context.OperationId, ["source"] = Name(context.Source), ["modelName"] = context.ModelName, ["engine"] = context.Engine },
            ["phase"] = phase, ["details"] = JsonSerializer.SerializeToNode(details),
        }), store, () => { reads++; return tracking; }, () => clock);
        void Begin(string name, string model) => telemetry.Begin(Token(name), model, "llama", LearningLanguage.English);
        JsonObject State()
        {
            JsonNode? raw = null;
            if (current.PerformanceMetricEntriesValue is { ValueKind: JsonValueKind.String } data && data.TryGetBytesFromBase64(out var bytes))
            {
                try { raw = LearningSpeechSettingsTests.RawJson(bytes); }
                catch (Exception error) when (error is JsonException or ArgumentException) { raw = new JsonObject { ["dataBase64"] = Convert.ToBase64String(bytes) }; }
            }
            var runtime = telemetry.Runtime;
            return new() { ["runtime"] = new JsonObject { ["sessionID"] = runtime.SessionId?.ToString("D").ToUpperInvariant(), ["generation"] = runtime.Generation.ToString(CultureInfo.InvariantCulture), ["phase"] = Name(runtime.Phase), ["label"] = runtime.Phase.Label(), ["processID"] = runtime.ProcessId, ["modelName"] = runtime.ModelName, ["files"] = JsonSerializer.SerializeToNode(runtime.LoadedModelFilenames.Order(CanonicalStringComparer.Instance)) },
                ["entries"] = store is null ? null : LearningSpeechSettingsTests.RawJson(PerformanceMetricCodec.Encode(store.Entries)), ["raw"] = raw, ["events"] = events.DeepClone(), ["ids"] = JsonSerializer.SerializeToNode(ids.Select(i => i.ToString("D").ToUpperInvariant())), ["trackingReads"] = reads };
        }
        var trace = new JsonArray(State());
        foreach (var action in row.GetProperty("actions").EnumerateArray())
        {
            switch (Text(action, "op"))
            {
                case "clock": clock = action.GetProperty("value").GetDouble(); break;
                case "tracking": tracking = action.GetProperty("value").GetBoolean(); break;
                case "begin": Begin(Text(action, "token"), Text(action, "model", "model")); break;
                case "finish": telemetry.Finish(Token(Text(action, "token")), Enum<ModelRequestOutcome>(action, "outcome", "completed"), action.TryGetProperty("errorType", out var error) ? error.GetString() : null); break;
                case "generated": telemetry.Generated(Token(Text(action, "token")), Number(action, "bytes")); break;
                case "generation": telemetry.GenerationCompleted(Token(Text(action, "token"))); break;
                case "started": telemetry.PlaybackStarted(Token(Text(action, "token"))); break;
                case "prepared": telemetry.PlaybackPrepared(Token(Text(action, "token")), Number(action, "milliseconds")); break;
                case "many": for (int i = 0; i < Number(action, "count"); i++) { string name = "t" + i; Begin(name, "model" + i); telemetry.Finish(Token(name), ModelRequestOutcome.Completed); } break;
                case "runtime":
                    var snapshot = new SpeechRuntimeSnapshot { SessionId = action.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.String ? Session(session.GetString()!) : null,
                        Generation = ulong.Parse(Text(action, "generation", "0"), CultureInfo.InvariantCulture), Phase = Enum<SpeechLocalRuntimePhase>(action, "phase", "loading"), ProcessId = action.TryGetProperty("processID", out var pid) ? pid.GetInt32() : null,
                        ModelName = Text(action, "model", "model"), LoadedModelFilenames = action.TryGetProperty("files", out var files) ? files.EnumerateArray().Select(v => v.GetString()!).ToHashSet(CanonicalStringComparer.Instance) : new HashSet<string>() };
                    telemetry.Receive(new(snapshot, action.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String ? Token(token.GetString()!) : null,
                        ulong.Parse(Text(action, "sequence", "0"), CultureInfo.InvariantCulture), Optional(action, "load"), action.TryGetProperty("reason", out var reason) ? reason.GetString() : null)); break;
                case "record":
                    ids.Add(store!.Record(Text(action, "model", "model"), Number(action, "latency", 300), Epoch.AddSeconds(action.TryGetProperty("timestamp", out var time) ? time.GetDouble() : 771692800),
                        Enum<ModelRequestSource>(action, "source", "completion"), Enum<ModelRequestOutcome>(action, "outcome", "completed"), action.TryGetProperty("requestID", out var requestId) ? requestId.GetString() : null,
                        Optional(action, "load"), Optional(action, "first"), Optional(action, "usable"), Optional(action, "display"))); break;
                case "recordMany": for (int i = 0; i < Number(action, "count"); i++) ids.Add(store!.Record("model", i, Epoch.AddSeconds(771692800 + i))); break;
                case "load": case "display":
                    int index = (int)Number(action, "index"); var entryId = index < 0 ? Id(999) : ids[index];
                    if (Text(action, "op") == "load") store!.RecordLoad(entryId, Number(action, "elapsed")); else store!.RecordDisplay(entryId, Number(action, "elapsed")); break;
                case "clear": store!.Clear(); break;
                case "restartStore": store = NewStore(); break;
                default: throw new InvalidOperationException("Unknown telemetry action");
            }
            trace.Add(State());
        }
        Assert.Empty(failures); var actual = Normalize(trace, scope); var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText());
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\n" + string.Join("\n", AnkiReviewSessionTests.Differences(expected, actual).Take(20)));
    }
    private static JsonNode? Normalize(JsonNode node, string scope)
    {
        var ids = new HashSet<string>(); var requests = new HashSet<string>(); Collect(node);
        void Collect(JsonNode? value)
        {
            if (value is JsonObject o)
            {
                if (o["id"] is JsonValue id && o.ContainsKey("modelName") && o.ContainsKey("timestamp") && !id.GetValue<string>().StartsWith("00000000-", StringComparison.Ordinal)) ids.Add(id.GetValue<string>());
                if (o["ids"] is JsonArray rowIds) foreach (var rowId in rowIds) if (!rowId!.GetValue<string>().StartsWith("00000000-", StringComparison.Ordinal)) ids.Add(rowId.GetValue<string>());
                if (o["requestID"] is JsonValue r && o["operationID"] is JsonValue op) { requests.Add(r.GetValue<string>()); requests.Add(op.GetValue<string>()); }
                foreach (var p in o) Collect(p.Value);
            }
            else if (value is JsonArray a) foreach (var item in a) Collect(item);
        }
        var idMap = new Dictionary<string, string>(); var requestMap = new Dictionary<string, string>(); return Visit(node);
        JsonNode? Visit(JsonNode? value)
        {
            if (value is JsonObject o) return new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, scope == "telemetry" && p.Key == "timestamp" && o.ContainsKey("id") && o.ContainsKey("modelName") ? JsonValue.Create("captured") : Visit(p.Value))));
            if (value is JsonArray a) return new JsonArray(a.Select(Visit).ToArray());
            if (value is JsonValue v && v.TryGetValue<string>(out var text))
            {
                if (ids.Contains(text)) { if (!idMap.TryGetValue(text, out var id)) idMap[text] = id = "entry-" + (idMap.Count + 1); return JsonValue.Create(id); }
                if (requests.Contains(text)) { if (!requestMap.TryGetValue(text, out var id)) requestMap[text] = id = "request-" + (requestMap.Count + 1); return JsonValue.Create(id); }
            }
            return value?.DeepClone();
        }
    }
    [Fact]
    public void RuntimeAssetsAreOwnedAndCanonicalValueEqualityDoesNotRewriteDisplayNames()
    {
        var names = new HashSet<string> { "é.gguf" };
        var first = new SpeechRuntimeSnapshot { ModelName = "é", LoadedModelFilenames = names };
        names.Add("later.gguf");
        var second = new SpeechRuntimeSnapshot { ModelName = "e\u0301", LoadedModelFilenames = new HashSet<string> { "e\u0301.gguf" } };
        Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(first.LoadedModelFilenames); Assert.Equal("e\u0301", second.ModelName);
    }
    [Fact]
    public void RetainedHistorySnapshotsAndGeneralResetKeepAcknowledgedRowsAndTheirBytes()
    {
        var values = new AppSettings(); var store = new PerformanceMetricsStore(() => values, () => { }, (_, _) => Assert.Fail("Unexpected persistence failure"));
        var id = store.Record("model", 20, Epoch, ModelRequestSource.Speech); var retained = store.Entries;
        store.RecordLoad(id, -1); store.RecordDisplay(id, 0);
        Assert.Null(retained[0].ModelLoadMs); Assert.Null(retained[0].DisplayMs);
        Assert.Equal(0, store.Entries[0].ModelLoadMs); Assert.Equal(0, store.Entries[0].DisplayMs);
        Assert.Throws<NotSupportedException>(() => ((IList<PerformanceMetricEntry>)retained)[0] = retained[0] with { ModelName = "tampered" });
        string saved = values.PerformanceMetricEntriesValue!.Value.GetRawText(); values = values.ResetGeneralPreferences(50);
        Assert.Equal(saved, values.PerformanceMetricEntriesValue!.Value.GetRawText());
        var restored = new PerformanceMetricsStore(() => values, () => { }, (_, _) => Assert.Fail("Unexpected persistence failure")); Assert.Equal(store.Entries, restored.Entries);
    }
    [Fact]
    public void OptOutRetainsStructuredEventsAndWindowsSaveFailuresKeepTheInMemoryRecord()
    {
        var values = new AppSettings(); var failures = new List<int>();
        var store = new PerformanceMetricsStore(() => values, () => throw new IOException("fixture save failure"), (_, count) => failures.Add(count));
        var events = new List<IReadOnlyDictionary<string, string>>();
        var telemetry = new SpeechTelemetry((_, _, details) => events.Add(details), store);
        var token = new SpeechPlaybackToken(); telemetry.Begin(token, "model", "llama", LearningLanguage.English); telemetry.Finish(token, ModelRequestOutcome.Completed);
        Assert.Equal(3, events.Count); Assert.Empty(store.Entries); Assert.Null(values.PerformanceMetricEntriesValue);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)events[0])["text"] = "tampered");
        store.Record("model", 10, Epoch); Assert.Single(store.Entries); Assert.Equal(new[] { 1 }, failures);
        Assert.NotNull(values.PerformanceMetricEntriesValue);
    }
    private static readonly DateTimeOffset Epoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static Guid Id(int index) => Guid.Parse($"00000000-0000-0000-0000-{index:x12}");
    private static bool Flag(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement row, string key, string fallback = "") => row.TryGetProperty(key, out var value) ? value.GetString() ?? fallback : fallback;
    private static long Number(JsonElement row, string key, long fallback = 0) => row.TryGetProperty(key, out var value) ? value.GetInt64() : fallback;
    private static long? Optional(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.GetInt64() : null;
    private static string Name<T>(T value) => JsonSerializer.SerializeToElement(value).GetString()!;
    private static T Enum<T>(JsonElement row, string key, string fallback) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(Text(row, key, fallback)))!;
}
