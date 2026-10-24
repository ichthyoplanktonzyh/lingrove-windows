using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Review;
using Cotabby.App.Settings;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.ReviewTesting;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Complete source batch traces use the actual controller/single-session/Store/SQLite
/// graph. The context serializes presentation like MainActor; the shared server supplies effects
/// only. Every test owns its queue and drains accepted work before releasing Store and context.</summary>
public sealed class AnkiReviewBatchSessionTests
{
    private static readonly JsonElement Corpus = ReadCorpus();
    private static JsonElement ReadCorpus()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "anki-batch-v1.json")));
        return d.RootElement.Clone();
    }
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static string Text(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var found) ? found.GetString() ?? fallback : fallback;
    private static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out var found) && found.ValueKind == JsonValueKind.True;
    private static int Number(JsonElement value, string key, int fallback) => value.TryGetProperty(key, out var found) ? found.GetInt32() : fallback;
    public static IEnumerable<object[]> Cases()
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString()); Assert.Equal("en_US", Corpus.GetProperty("referenceLocale").GetString());
        var rows = Corpus.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(34, rows.Length);
        Assert.Equal("223d91348f88aa8d2b4158802bcdc0dbf0573da2cee05859fafb8c68692aea02", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()! }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task CompleteOriginalBatchTrace(string id)
    {
        using var ui = new BatchUiContext();
        await ui.RunAsync(async deadline =>
        {
            var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
            var seed = JsonNode.Parse(row.GetRawText())!; seed["secondSense"] ??= true;
            var archive = new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory);
            await using var store = new VocabularyStore(archive, SynchronizationContext.Current!);
            var document = AnkiReviewSessionTests.Seed(JsonSerializer.SerializeToElement(seed));
            if (row.TryGetProperty("cards", out var cards))
            {
                var graph = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(document))!;
                graph["entries"]![0]!["senses"]![0]!["reviewCards"] = new JsonArray(cards.EnumerateArray().Select(card => (JsonNode)new JsonObject { ["plan"] = JsonNode.Parse(card.GetRawText()), ["bindings"] = new JsonArray(), ["preservedPlans"] = new JsonArray() }).ToArray());
                document = VocabularyBackupCodec.DecodeDocument(Encoding.UTF8.GetBytes(graph.ToJsonString()));
            }
            if (Flag(row, "alias")) { var e = document.Entries[0]; document = document with { Entries = [e with { Senses = e.Senses.Select((sense, index) => index == 0 ? sense with { MergedFrom = [Id(9)] } : sense).ToArray() }] }; }
            await archive.CommitAsync(document); await store.LoadAsync();
            var current = new AppSettings(); var settings = new AnkiReviewSettings(() => current, () => { }, new AnkiTestCredentials()); var client = new SessionTestServer(); var speech = new SessionTestSpeech();
            if (row.TryGetProperty("settings", out var initial)) Preferences(initial, settings);
            var controller = new AnkiReviewController(settings, store, client, speech: Flag(row, "speech") ? speech : null);
            if (Flag(row, "prime")) { var e = store.Entries[0]; Assert.True(await new AnkiReviewSession(controller, e, e.Senses[0]).SendAsync()); }
            if (Flag(row, "primeCards"))
            {
                var e = store.Entries[0]; foreach (var card in e.Senses[0].ReviewCards ?? []) Assert.True(await new AnkiReviewSession(controller, e, store.Entries[0].Senses[0], cardId: card.Plan.Id).SendAsync());
            }
            var selections = (row.TryGetProperty("selections", out var selected) ? selected.EnumerateArray().Select(v => v.GetInt32()) : new[] { 2, 8 }).Select(n => new VocabularySelection(Id(1), Id(n))).ToArray();
            var batch = new AnkiReviewBatchSession(controller, selections, Flag(row, "flexible"));
            async Task<object> State()
            {
                var disk = await archive.LoadAsync();
                var items = batch.Items.Select(item => new { id = item.Id.ToString("D"), entryID = item.Selection.EntryId.ToString("D"), senseID = item.Selection.SenseId.ToString("D"), cardID = item.CardId?.ToString("D"), expression = item.Expression, meaning = item.Meaning,
                    status = char.ToLowerInvariant(item.Status.ToString()[0]) + item.Status.ToString()[1..], label = item.Status.Label(), message = item.Message, canonicalSenseID = batch.Resolve(item)?.Sense.Id.ToString("D") }).ToArray();
                return new { items, busy = batch.Busy, work = batch.Work is not null, message = batch.Message, empty = batch.EmptySenseCount, complete = batch.CompleteCount,
                    settings = new { deck = settings.Deck, recognition = settings.DefaultRecognition, gap = settings.DefaultGap, hasSavedDeck = settings.HasSavedDeck },
                    store = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(new VocabularyDocument { Entries = store.Entries, ReviewTemplates = disk.Document.ReviewTemplates })), disk = JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(disk.Document)), server = client.Snapshot(), exports = speech.Exports,
                    failures = controller.Failures.ToDictionary(p => p.Key.ToString("D"), p => p.Value) };
            }
            var trace = new List<object> { new { state = await State(), outcome = (object?)null } };
            try
            {
                foreach (var a in row.GetProperty("actions").EnumerateArray())
                {
                    deadline.ThrowIfCancellationRequested();
                    object? outcome = null;
                    switch (Text(a, "op"))
                    {
                        case "start": batch.Start(a.TryGetProperty("only", out var only) ? batch.Items[only.GetInt32()].Id : null); if (batch.Work is { } started) await started.WaitAsync(deadline); break;
                        case "startTwice": batch.Start(); var first = batch.Work; outcome = batch.Busy; batch.Start(); batch.StartApply("Ignored", 2, 4); if (first is not null) await first.WaitAsync(deadline); break;
                        case "apply": batch.StartApply(Text(a, "deck"), Number(a, "cards", 0), Number(a, "audio", 0)); if (batch.Work is { } applying) await applying.WaitAsync(deadline); break;
                        case "stopBeforeRun": batch.Start(); var stopping = batch.Work; await batch.StopAsync(); if (stopping is not null) await stopping.WaitAsync(deadline); break;
                        case "stopBeforeApply": batch.StartApply("Other", 2, 4); var stoppedApply = batch.Work; await batch.StopAsync(); if (stoppedApply is not null) await stoppedApply.WaitAsync(deadline); break;
                        case "stopAudio":
                            speech.Pause = true; batch.Start(); var recording = batch.Work;
                            try { await speech.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                            finally { await batch.StopAsync(); if (recording is not null) await recording; speech.Pause = false; }
                            break;
                        case "stopAfterFirst": var currentBatch = batch; Task? cancel = null; client.AfterNote = () => cancel = currentBatch.StopAsync(); batch.Start(); var partial = batch.Work; if (partial is not null) await partial.WaitAsync(deadline); if (cancel is not null) await cancel; break;
                        case "removeMedia": client.RemoveMedia(); break;
                        case "newBatch": batch = new(controller, selections, Flag(row, "flexible")); break;
                        case "settings": Preferences(a, settings); break;
                        case "afterMetadata": client.AfterMetadata = () => Preferences(a, settings); break;
                        case "profileMetadata": client.ProfileAfterMetadata = Text(a, "value"); break;
                        case "decks": client.Decks = a.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray(); break;
                        case "failAdd": client.FailAdd = true; break;
                        case "loseAdd": client.LoseAdd = true; break;
                        case "edit": client.Edit(Number(a, "id", 1001), Text(a, "field", "Meaning"), Text(a, "value")); break;
                        case "begin": batch.BeginPreparation(batch.Items[Number(a, "index", 0)].Id); break;
                        case "refresh": batch.Refresh(batch.Items[Number(a, "index", 0)].Id); break;
                        case "prepareSave": var item = batch.Items[Number(a, "index", 0)]; var source = batch.Resolve(item)!.Value; var draft = new AnkiReviewSession(controller, source.Entry, source.Sense, cardId: item.CardId); draft.Plan = draft.Plan with { Answer = Text(a, "value", "edited") }; outcome = await draft.SaveAsync(); break;
                        case "merge": outcome = await store.MergeSensesAsync(Id(1), Id(2), Id(8)); break;
                        case "remove": outcome = await store.RemoveSenseAsync(Id(1), Id(Number(a, "sense", 2))); break;
                        default: throw new InvalidOperationException("Unknown batch action");
                    }
                    trace.Add(new { state = await State(), outcome });
                }
                var observation = AnkiReviewSessionTests.NormalizeObservation(JsonSerializer.SerializeToNode(new { trace, calls = client.Calls })); var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText());
                Assert.True(JsonNode.DeepEquals(expected, observation), id + "\n" + string.Join("\n", AnkiReviewSessionTests.Differences(expected, observation).Take(30)));
            }
            finally { await batch.StopAsync(); if (batch.Work is { } remaining) await remaining; }
        });
    }
    private static void Preferences(JsonElement a, AnkiReviewSettings settings)
    {
        if (a.TryGetProperty("deck", out var deck)) settings.Deck = deck.GetString()!;
        if (a.TryGetProperty("recognition", out var recognition)) settings.DefaultRecognition = recognition.GetBoolean();
        if (a.TryGetProperty("gap", out var gap)) settings.DefaultGap = gap.GetBoolean();
        if (a.TryGetProperty("model", out var model)) settings.Model = model.GetString()!;
        if (a.TryGetProperty("address", out var address)) settings.Address = address.GetString()!;
    }
}
