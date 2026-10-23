using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Actual Store/archive/SQLite transactions, compared with the complete Mac archive and
/// original five Store bodies. Each row owns memory SQL; no user notebook or server is accessed.</summary>
public sealed class VocabularyReviewTransactionTests
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:x12}");
    private static string Text(IReadOnlyDictionary<string, AnkiValue> r, string key, string fallback = "") => r.GetValueOrDefault(key)?.String ?? fallback;
    private static bool Flag(IReadOnlyDictionary<string, AnkiValue> r, string key) => r.GetValueOrDefault(key) is AnkiValue.Boolean { Value: true };
    private static int Number(IReadOnlyDictionary<string, AnkiValue> r, string key, int fallback) => r.GetValueOrDefault(key) is AnkiValue.Number n ? (int)n.Value : fallback;
    private static ReviewLayout Layout(int n) => new([new(Id(n), ReviewContentSource.Expression)], [new(Id(n + 1), ReviewContentSource.Meaning)]);
    private static VocabularyReview Review(int n, string answer = "original", bool flexible = false) => new(new ReviewPlan(Id(n)) { Layout = flexible ? Layout(n + 100) : null, Answer = answer });
    private static AnkiNoteBinding Binding(IReadOnlyDictionary<string, AnkiValue> r)
    {
        Guid origin = Id(Number(r, "origin", 2));
        return new(Id(Number(r, "binding", 60)), origin, "http://localhost", "Profile", Number(r, "note", 10), "Basic", "Default", "ID",
            new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = origin.ToString("D"), ["Q"] = Text(r, "field", "acknowledged") });
    }
    private static VocabularyDocument Seed(IReadOnlyDictionary<string, AnkiValue> r)
    {
        string mode = Text(r, "seed", "both"); var time = DateTimeOffset.UnixEpoch;
        var sense = new VocabularySense(Id(2), "meaning", time, time) { Note = "source note", IsSavedWithoutContext = true, MergedFrom = Flag(r, "alias") ? [Id(11)] : [] };
        if (mode is "legacy" or "both") sense = sense with { Review = Review(20) };
        if (mode is "cards" or "both") sense = sense with { ReviewCards = [Review(21, "first", true), Review(22, "second", true)] };
        if (mode == "empty-cards") sense = sense with { ReviewCards = [] };
        if (Flag(r, "bound") || Flag(r, "twoBindings") || Flag(r, "preserved"))
        {
            var value = Flag(r, "baseCard") ? sense.ReviewCards![0] : sense.Review!;
            if (Flag(r, "bound") || Flag(r, "twoBindings")) value = value with { Bindings = [Binding(new Dictionary<string, AnkiValue>())], ActiveBindingId = Id(60) };
            if (Flag(r, "twoBindings")) value = value with { Bindings = value.Bindings.Append(Binding(new Dictionary<string, AnkiValue> { ["binding"] = new AnkiValue.Number(61), ["note"] = new AnkiValue.Number(11) })).ToArray() };
            if (Flag(r, "preserved")) value = value with { PreservedPlans = [new ReviewPlan(Id(25)) { Name = "preserved", Answer = "keep" }] };
            sense = Flag(r, "baseCard") ? sense with { ReviewCards = [value, sense.ReviewCards![1]] } : sense with { Review = value };
        }
        var document = new VocabularyDocument { Entries = [new(Id(1), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense], time, time)] };
        if (Flag(r, "emptyTemplates")) document = document with { ReviewTemplates = [] };
        if (Flag(r, "templates")) document = document with { ReviewTemplates = [new(Id(70), "First", Layout(170)), new(Id(71), "Second", Layout(172))] };
        if (Flag(r, "maxTemplates")) document = document with { ReviewTemplates = Enumerable.Range(0, 64).Select(i => new ReviewLayoutTemplate(Id(1000 + i), "T" + i, Layout(2000 + 2 * i))).ToArray() };
        return document;
    }
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "vocabulary-review-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(56, rows.Length);
        Assert.Equal("2fa8710b2fcbf1f6015aa5590e86c1812b2241bac26fa3366be7fae53bdfa155", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task OriginalReviewStoreAndArchiveTrace(string id, JsonElement row)
    {
        var input = JsonSerializer.Deserialize<AnkiValue>(row.GetRawText())!.Object!;
        var archive = new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory);
        await using var store = new VocabularyStore(archive, new SynchronizationContext());
        await archive.CommitAsync(Seed(input)); await store.LoadAsync(); Assert.True(store.IsLoaded);
        ulong revision = (await archive.LoadAsync()).Revision; VocabularyReview? expected = null;
        async Task<object> Snapshot()
        {
            var disk = await archive.LoadAsync();
            return new { store = Encoded(new VocabularyDocument { Entries = store.Entries, ReviewTemplates = store.ReviewTemplates.Count == 0 && disk.Document.ReviewTemplates is null ? null : store.ReviewTemplates }),
                disk = Encoded(disk.Document), revision = disk.Revision - revision, loaded = store.IsLoaded, error = store.ErrorMessage is not null, pending = store.Pending.Count };
        }
        VocabularyReview? Live(Guid? card) => store.Entries.FirstOrDefault()?.Senses.FirstOrDefault()?.ReviewValue(card);
        var trace = new List<object> { new { state = await Snapshot(), outcome = (object?)null } };
        foreach (var action in input["actions"].Array!)
        {
            var a = action.Object!; Guid? card = a.ContainsKey("card") ? Id(Number(a, "card", 21)) : null;
            Guid entryId = Id(Number(a, "entry", 1)), senseId = Id(Number(a, "sense", 2)); object? outcome = null;
            try
            {
                switch (Text(a, "op"))
                {
                    case "capture": expected = Live(card); break;
                    case "save": case "external":
                        var value = Flag(a, "fresh") ? Review(Number(a, "valueID", card is null ? 20 : Number(a, "card", 21)), Text(a, "answer", "edited"), card is not null && !Flag(a, "noLayout"))
                            : expected ?? Review(Number(a, "valueID", card is null ? 20 : Number(a, "card", 21)), "original", card is not null && !Flag(a, "noLayout"));
                        value = value with { Plan = value.Plan with { Answer = Text(a, "answer", "edited") } };
                        if (a.ContainsKey("valueID")) value = value with { Plan = value.Plan with { Id = Id(Number(a, "valueID", 20)) } };
                        if (Flag(a, "noLayout")) value = value with { Plan = value.Plan with { Layout = null } };
                        if (Flag(a, "badBinding")) { var bad = Binding(new Dictionary<string, AnkiValue>()) with { Fields = new Dictionary<string, string>() }; value = value with { Bindings = [bad], ActiveBindingId = bad.Id }; }
                        if (Text(a, "op") == "external") _ = await archive.SaveReviewAsync(entryId, senseId, value, Live(card), card);
                        else await store.SaveReviewAsync(entryId, senseId, value, expected, card);
                        break;
                    case "record": await store.RecordAnkiBindingAsync(entryId, senseId, Binding(a), card); break;
                    case "remove": await store.RemoveReviewAsync(entryId, senseId, card!.Value, expected!); break;
                    case "template": case "cancelTemplate":
                        var template = new ReviewLayoutTemplate(Id(Number(a, "id", 70)), Text(a, "name", "Saved"), Flag(a, "badLayout") ? new() : Layout(Number(a, "part", 3000)));
                        using (var cancellation = new CancellationTokenSource())
                        { if (Text(a, "op") == "cancelTemplate") cancellation.Cancel(); await store.SaveTemplateAsync(template, cancellationToken: cancellation.Token); }
                        break;
                    case "removeTemplate": await store.RemoveTemplateAsync(Id(Number(a, "id", 70))); break;
                    case "sharedError": _ = await store.RemoveSenseAsync(Id(999), Id(888)); break;
                    case "reload": await store.LoadAsync(); break;
                    default: throw new InvalidOperationException("Unknown review transaction fixture action.");
                }
            }
            catch (OperationCanceledException) { outcome = new { error = "cancelled" }; }
            catch (Exception error) { outcome = new { error = error.Message }; }
            trace.Add(new { state = await Snapshot(), outcome });
        }
        var expectedTrace = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(trace);
        Assert.True(JsonNode.DeepEquals(expectedTrace, actual), id + "\nExpected: " + expectedTrace + "\nActual: " + actual);
    }
    private static JsonNode Encoded(VocabularyDocument document) => Canonical(JsonNode.Parse(VocabularyBackupCodec.EncodeDocument(document))!)!;
    private static JsonNode? Canonical(JsonNode? value, string key = "")
    {
        if (value is JsonObject obj) return new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value, p.Key))));
        if (value is JsonArray array)
        {
            if (key is "mapping" or "audioReceipts")
            {
                Assert.Equal(0, array.Count % 2);
                return new JsonArray(Enumerable.Range(0, array.Count / 2).OrderBy(i => array[2 * i]!.GetValue<string>(), StringComparer.Ordinal)
                    .SelectMany(i => new[] { array[2 * i]?.DeepClone(), Canonical(array[2 * i + 1]) }).ToArray());
            }
            if (key == "audioKinds") return new JsonArray(array.OrderBy(v => v!.GetValue<string>(), StringComparer.Ordinal).Select(v => v?.DeepClone()).ToArray());
            return new JsonArray(array.Select(v => Canonical(v)).ToArray());
        }
        return value?.DeepClone();
    }
    [Fact]
    public async Task ClosingTheStoreDrainsAnAdmittedReviewMutationAndRejectsAnother()
    {
        using var opened = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var archive = new VocabularyArchive(() => { opened.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return VocabularySQLiteDatabase.CreateMemory(); });
        await using var store = new VocabularyStore(archive, new SynchronizationContext()); var template = new ReviewLayoutTemplate(Id(70), "Saved", Layout(100));
        Task save = store.SaveTemplateAsync(template); Task? close = null;
        try
        {
            Assert.True(opened.Wait(TimeSpan.FromSeconds(10))); close = store.DisposeAsync().AsTask(); Assert.False(close.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => store.RemoveTemplateAsync(template.Id));
        }
        finally { release.Set(); }
        await save.WaitAsync(TimeSpan.FromSeconds(10)); await close!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(store.ReviewTemplates); Assert.Equal(template, store.ReviewTemplates[0]); Assert.Null(store.ErrorMessage);
    }
}
