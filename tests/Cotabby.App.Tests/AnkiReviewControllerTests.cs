using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Actual controller/navigation with scripted effects; the committed oracle runs both
/// complete original Mac files. No fake reimplements profile/status/lease/template decisions.</summary>
public sealed class AnkiReviewControllerTests
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static string Text(IReadOnlyDictionary<string, AnkiValue> r, string key, string fallback = "") => r.GetValueOrDefault(key)?.String ?? fallback;
    private static bool Flag(IReadOnlyDictionary<string, AnkiValue> r, string key, bool fallback = false) => r.GetValueOrDefault(key) is AnkiValue.Boolean b ? b.Value : fallback;
    private static int Number(IReadOnlyDictionary<string, AnkiValue> r, string key, int fallback = 0) => r.GetValueOrDefault(key) is AnkiValue.Number n ? (int)n.Value : fallback;
    private static string Field(ReviewField field) => JsonSerializer.SerializeToElement(field).GetString()!;
    private static ReviewField Field(string field) => JsonSerializer.Deserialize<ReviewField>(JsonSerializer.Serialize(field));
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "anki-controller-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(167, rows.Length);
        Assert.Equal("d33323cd4ea584cec32b504c220f01e21c7712e465846bddbd7e7ddcebfd0c77", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task CompleteOriginalControllerAndNavigationTrace(string id, JsonElement row)
    {
        var input = JsonSerializer.Deserialize<AnkiValue>(row.GetRawText())!.Object!;
        var current = new AppSettings(); var credentials = new MemoryCredentials();
        var settings = new AnkiReviewSettings(() => current, () => { }, credentials); string speech = "speech";
        void Change(AnkiValue? raw)
        {
            if (raw?.Object is not { } c) return;
            if (c.GetValueOrDefault("address")?.String is { } address) settings.Address = address;
            if (c.GetValueOrDefault("model")?.String is { } model) settings.Model = model;
            if (c.GetValueOrDefault("deck")?.String is { } deck) settings.Deck = deck;
            if (c.GetValueOrDefault("key")?.String is { } key) settings.SaveKey(key);
            if (c.GetValueOrDefault("speech")?.String is { } nextSpeech) speech = nextSpeech;
            if (c.GetValueOrDefault("recognition") is AnkiValue.Boolean recognition) settings.DefaultRecognition = recognition.Value;
            if (c.GetValueOrDefault("gap") is AnkiValue.Boolean gap) settings.DefaultGap = gap.Value;
            if (c.GetValueOrDefault("mapping")?.Object is { } mapping) settings.Mapping = mapping.ToDictionary(p => Field(p.Key), p => p.Value.String!);
        }
        Change(input.GetValueOrDefault("initial")); var entry = Entry(input);
        await using var store = new VocabularyStore(new VocabularyArchive(() =>
        {
            var database = VocabularySQLiteDatabase.CreateMemory();
            if (Flag(input, "loaded")) database.Write(new() { Entries = [entry] }, new(), 0);
            return database;
        }), new SynchronizationContext());
        if (Flag(input, "loaded")) { await store.LoadAsync(); Assert.True(store.IsLoaded); }
        var client = new ScriptedClient(input, Change); int activations = 0, configures = 0;
        var controller = new AnkiReviewController(settings, store, client, () => Flag(input, "noSpeech") ? null : speech, () => activations++);
        controller.Configure = () => configures++;
        ReviewOperationScope? saved = null;
        var trace = new List<object> { new { state = Snapshot(controller), outcome = (object?)null } };
        foreach (var action in input["actions"].Array!)
        {
            var a = action.Object!; object? outcome = null;
            try
            {
                switch (Text(a, "op"))
                {
                    case "test": await controller.TestConnectionAsync(a.GetValueOrDefault("model")?.String); break;
                    case "refresh": await controller.RefreshFieldsAsync(); break;
                    case "scope": saved = await controller.OperationScopeAsync(); outcome = Scope(saved); break;
                    case "matches": outcome = controller.Matches(saved!); break;
                    case "verify": await controller.VerifyAsync(saved!); break;
                    case "change": Change(a.GetValueOrDefault("value")); break;
                    case "invalidate": controller.InvalidateConnection(); break;
                    case "profile": outcome = await controller.ProfileAsync(settings.Connection()); break;
                    case "catalog": var catalog = await controller.FetchCatalogAsync(settings.Connection()); outcome = new { profile = catalog.Profile, decks = catalog.Decks, models = catalog.Models }; break;
                    case "fields": outcome = await controller.FieldsAsync(Text(a, "model", "Custom"), settings.Connection(), Flag(a, "create"), a.GetValueOrDefault("expectedProfile")?.String); break;
                    case "note": var note = await controller.NoteAsync(Number(a, "id", 10), settings.Connection()); outcome = note is null ? null : new { id = note.Id, model = note.Model, fields = note.Fields }; break;
                    case "strings": outcome = AnkiReviewController.Strings(a["value"]); break;
                    case "begin": controller.Begin(Id(Number(a, "id", 2)), a.ContainsKey("card") ? Id(Number(a, "card")) : null); break;
                    case "finish": controller.Finish(Id(Number(a, "id", 2)), a.ContainsKey("card") ? Id(Number(a, "card")) : null,
                        Text(a, "error") == "cancelled" ? new OperationCanceledException() : a.GetValueOrDefault("error")?.String is { } error ? new AnkiReviewException(error) : null, Flag(a, "missing"), Flag(a, "wrote", true)); break;
                    case "status": outcome = new { status = controller.Status(entry, entry.Senses[0], Flag(input, "card") ? Id(4) : null), synced = controller.IsSynchronized(entry, entry.Senses[0], Flag(input, "card") ? Id(4) : null) }; break;
                    case "current": var value = controller.Current(Id(Number(a, "entry", 1)), Id(Number(a, "sense", 2))); outcome = new[] { value.Entry.Id.ToString("D"), value.Sense.Id.ToString("D") }; break;
                    case "open": outcome = await controller.OpenAsync(Binding(a)); break;
                    case "deck": outcome = await controller.ReviewDeckAsync(Flag(a, "binding") ? Binding(a) : null); break;
                    case "menu": await controller.OpenReviewFromMenuAsync(); break;
                    case "template": await controller.UpdatePresetTemplateAsync(); break;
                    case "connectionEqual": outcome = new AnkiConnection(Text(a, "left"), Text(a, "leftKey")).Equals(new AnkiConnection(Text(a, "right"), Text(a, "rightKey"))); break;
                    default: throw new InvalidOperationException("Unknown controller fixture action.");
                }
            }
            catch (Exception error) { outcome = new { error = error.Message }; }
            trace.Add(new { state = Snapshot(controller), outcome });
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(new { trace, calls = client.Calls, activations, configures });
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\nExpected: " + expected + "\nActual: " + actual);
    }
    private static object Snapshot(AnkiReviewController c) => new
    {
        catalog = c.Catalog is { } catalog ? new { profile = catalog.Profile, decks = catalog.Decks, models = catalog.Models } : null,
        message = c.ConnectionMessage, testing = c.Testing, fields = c.ModelFields, sendingSense = c.SendingSenseId?.ToString("D"), sendingReview = c.SendingReviewId?.ToString("D"),
        failures = c.Failures.ToDictionary(p => p.Key.ToString("D"), p => p.Value), missing = c.Missing.Select(v => v.ToString("D")).Order(StringComparer.Ordinal).ToArray(),
        unresolved = c.Unresolved.Select(v => v.ToString("D")).Order(StringComparer.Ordinal).ToArray(), updatingTemplate = c.UpdatingTemplate,
    };
    private static object Scope(ReviewOperationScope s) => new { endpoint = s.Connection.Endpoint, key = s.Connection.ApiKey, profile = s.Profile, model = s.Model,
        mapping = s.Mapping.ToDictionary(p => Field(p.Key), p => p.Value), speech = s.SpeechId, deck = s.DefaultDeck, savedDeck = s.HadSavedDeck, recognition = s.DefaultRecognition, gap = s.DefaultGap };
    private static AnkiNoteBinding Binding(IReadOnlyDictionary<string, AnkiValue> r) => new(Id(6), Id(2), Text(r, "endpoint", "http://127.0.0.1:8765"), Text(r, "profile", "Profile"), Flag(r, "noNote") ? null : 10,
        "Custom", "Default", "LingroveID", AnkiReviewPolicy.PresetMapping, new Dictionary<string, string> { ["LingroveID"] = Id(2).ToString("D") });
    private static VocabularyEntry Entry(IReadOnlyDictionary<string, AnkiValue> r)
    {
        string mode = Text(r, "review", "none"); bool card = Flag(r, "card"); var time = DateTimeOffset.UnixEpoch;
        var sense = new VocabularySense(Id(2), "meaning", time, time) { IsSavedWithoutContext = true };
        var entry = new VocabularyEntry(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense], time, time);
        if (mode != "none")
        {
            var plan = new ReviewPlan(Id(card ? 4 : 3)); if (mode == "badContent") plan = plan with { Answer = "" };
            if (mode is "audioStale" or "audioCurrent") plan = plan with { AudioKinds = new HashSet<ReviewAudioKind> { ReviewAudioKind.Expression } };
            if (mode == "audioCurrent") plan = plan with { AudioReceipts = new Dictionary<ReviewAudioKind, ReviewAudioReceipt> { [ReviewAudioKind.Expression] = new("ignored", SpeechExportPolicy.Digest(Encoding.UTF8.GetBytes("bank")), "ignored", "ignored", "audio.wav") } };
            var review = new VocabularyReview(plan);
            if (mode != "unbound")
            {
                IReadOnlyDictionary<string, string> fields = new Dictionary<string, string>();
                try { fields = AnkiReviewPolicy.Fields(ReviewCardPolicy.Content(entry, sense, plan), sense.Id, AnkiReviewPolicy.PresetMapping, new Dictionary<string, string>(), AnkiReviewPolicy.AudioFields(plan)); } catch (AnkiReviewException) { }
                if (mode == "stale") fields = new Dictionary<string, string>(fields) { ["Meaning"] = "remote change" };
                var binding = new AnkiNoteBinding(Id(6), sense.Id, "http://127.0.0.1:8765", "Profile", mode == "ready" ? null : 10, "Custom", "Default", "LingroveID", mode == "badMap" ? new Dictionary<ReviewField, string>() : AnkiReviewPolicy.PresetMapping, fields);
                review = review with { Bindings = [binding], ActiveBindingId = mode == "choice" ? null : binding.Id };
            }
            sense = card ? sense with { ReviewCards = [review] } : sense with { Review = review };
        }
        return entry with { Senses = [sense] };
    }
    private static AnkiValue Project(AnkiValue value) => value switch
    {
        AnkiValue.Text t when Encoding.UTF8.GetByteCount(t.Value) > 2000 => new AnkiValue.Map(new Dictionary<string, AnkiValue> { ["bytes"] = new AnkiValue.Number(Encoding.UTF8.GetByteCount(t.Value)), ["sha256"] = new AnkiValue.Text(SpeechExportPolicy.Digest(Encoding.UTF8.GetBytes(t.Value))) }),
        AnkiValue.Map m => new AnkiValue.Map(m.Values.ToDictionary(p => p.Key, p => Project(p.Value))),
        AnkiValue.Sequence s => new AnkiValue.Sequence(s.Values.Select(Project).ToArray()), _ => value,
    };
    private sealed class ScriptedClient(IReadOnlyDictionary<string, AnkiValue> input, Action<AnkiValue?> mutate) : IAnkiConnecting
    {
        private readonly Dictionary<string, int> _counts = [];
        public List<object> Calls { get; } = [];
        public Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken cancellationToken = default)
        {
            Calls.Add(new { action, @params = Project(new AnkiValue.Map(parameters)), endpoint = connection.Endpoint, key = connection.ApiKey });
            int index = _counts.GetValueOrDefault(action); _counts[action] = index + 1;
            var responses = input.GetValueOrDefault("responses")?.Object?.GetValueOrDefault(action)?.Array;
            var spec = responses is not null && index < responses.Count ? responses[index].Object! : new Dictionary<string, AnkiValue>();
            mutate(spec.GetValueOrDefault("change"));
            if (spec.GetValueOrDefault("error")?.String is { } error) throw new AnkiReviewException(error);
            if (spec.TryGetValue("value", out var value)) return Task.FromResult(value);
            if (spec.GetValueOrDefault("template")?.String is { } template) return Task.FromResult<AnkiValue>(template == "legacy" ? new AnkiValue.Map(AnkiNoteTemplate.LegacyTemplates) : template == "current" ? new AnkiValue.Map(AnkiNoteTemplate.Templates) : AnkiValue.Strings(new Dictionary<string, string> { ["custom"] = "custom" }));
            if (spec.GetValueOrDefault("style")?.String is { } style) return Task.FromResult(AnkiValue.Strings(new Dictionary<string, string> { ["css"] = style == "legacy" ? AnkiNoteTemplate.LegacyCss : "custom css" }));
            AnkiValue result = action switch
            {
                "getActiveProfile" => new AnkiValue.Text("Profile"), "deckNames" => AnkiValue.List(["Default", "Moved"]),
                "modelNames" => AnkiValue.List(["Custom", AnkiReviewPolicy.PresetName, ReviewLayoutPolicy.Model]),
                "modelFieldNames" => AnkiValue.List(Enum.GetValues<ReviewField>().Select(Field).ToArray()),
                "notesInfo" => JsonSerializer.Deserialize<AnkiValue>("[{\"noteId\":10,\"modelName\":\"Custom\",\"fields\":{\"LingroveID\":{\"value\":\"00000000-0000-0000-0000-000000000002\"}}}]")!,
                "findCards" => new AnkiValue.Sequence([new AnkiValue.Number(20)]), "cardsInfo" => new AnkiValue.Sequence([AnkiValue.Strings(new Dictionary<string, string> { ["deckName"] = "Moved" })]),
                "guiDeckOverview" => new AnkiValue.Boolean(true), "modelTemplates" => new AnkiValue.Map(AnkiNoteTemplate.Templates),
                "modelStyling" => AnkiValue.Strings(new Dictionary<string, string> { ["css"] = AnkiNoteTemplate.Css }), _ => AnkiValue.Null,
            };
            return Task.FromResult(result);
        }
    }
    private sealed class MemoryCredentials : IAnkiCredentialStore
    { private string _key = "fixture-key"; public string ReadKey() => _key; public void SaveKey(string value) => _key = value; }

    [Fact]
    public async Task ARepeatedProbeCannotStartAnotherProbeAndAnAwaitedModelChangeCannotPublish()
    {
        var current = new AppSettings(); var settings = new AnkiReviewSettings(() => current, () => { }, new MemoryCredentials());
        await using var store = new VocabularyStore(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory), new SynchronizationContext());
        var client = new GatedClient("getActiveProfile"); var controller = new AnkiReviewController(settings, store, client);
        Task first = controller.TestConnectionAsync();
        try
        {
            await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(controller.Testing);
            await controller.TestConnectionAsync(); Assert.Equal(1, client.Calls);
            settings.Model = "Other";
        }
        finally { client.Release.TrySetResult(new AnkiValue.Text("Profile")); }
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(controller.Testing); Assert.Null(controller.Catalog); Assert.Empty(controller.ModelFields); Assert.Null(controller.ConnectionMessage);
    }
    [Fact]
    public async Task RepeatedMenuNavigationSharesItsInFlightGuardAndActivatesOnlyAfterAcknowledgement()
    {
        var current = new AppSettings(); var settings = new AnkiReviewSettings(() => current, () => { }, new MemoryCredentials());
        await using var store = new VocabularyStore(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory), new SynchronizationContext());
        int activations = 0; var client = new GatedClient("deckNames"); var controller = new AnkiReviewController(settings, store, client, activateAnki: () => activations++);
        Task first = controller.OpenReviewFromMenuAsync();
        try
        {
            await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await controller.OpenReviewFromMenuAsync();
            Assert.Equal(1, client.Calls); Assert.Equal(0, activations);
        }
        finally { client.Release.TrySetResult(AnkiValue.List(["Default"])); }
        await first.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, activations); Assert.Null(controller.ConnectionMessage);
    }
    [Fact]
    public async Task CancellationBeforeACallCannotDispatchToTheInjectedClient()
    {
        var current = new AppSettings(); var settings = new AnkiReviewSettings(() => current, () => { }, new MemoryCredentials());
        await using var store = new VocabularyStore(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory), new SynchronizationContext());
        var client = new GatedClient("getActiveProfile"); var controller = new AnkiReviewController(settings, store, client);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.ProfileAsync(settings.Connection(), cancellation.Token));
        Assert.Equal(0, client.Calls); Assert.False(client.Started.Task.IsCompleted);
    }
    private sealed class GatedClient(string actionToHold) : IAnkiConnecting
    {
        private readonly ScriptedClient _fallback = new(new Dictionary<string, AnkiValue>(), _ => { });
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AnkiValue> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public async Task<AnkiValue> PerformAsync(string action, IReadOnlyDictionary<string, AnkiValue> parameters, AnkiConnection connection, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (action == actionToHold && Started.TrySetResult(true)) return await Release.Task.WaitAsync(cancellationToken);
            return await _fallback.PerformAsync(action, parameters, connection, cancellationToken);
        }
    }
}
