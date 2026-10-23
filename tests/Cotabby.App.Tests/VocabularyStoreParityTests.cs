using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

public sealed class VocabularyStoreParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "vocabulary-store-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    private static Guid Id(int value) => VocabularySQLiteParityTests.Id(value);
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalMacStoreMethodsSupplyPublicationAndAdoptionOutcomes(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(await Observe(row.GetProperty("operation").GetString()!));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static async Task<object> Observe(string mode)
    {
        var archive = new VocabularyArchive((string?)null); await using var store = new VocabularyStore(archive, new SynchronizationContext());
        var old = VocabularySQLiteParityTests.Document(); var capture = VocabularyArchiveParityTests.Capture();
        if (mode == "fresh")
        {
            bool before = store.IsLoaded; await store.LoadAsync();
            return new { before, loaded = store.IsLoaded, entries = store.Entries.Count, contains = store.Contains(capture) };
        }
        if (mode == "save")
        {
            var usage = capture with { Example = old.Entries[0].Examples[0] }; await store.SaveAsync(usage);
            return new { containsUsage = store.Contains(usage), containsStandalone = store.Contains(capture), loaded = store.IsLoaded, pending = store.IsPending(usage) };
        }
        if (mode == "enrich")
        {
            var record = old.Entries[0].Senses[0].Explanations[0] with { Id = Id(101) };
            store.EnrichCapture = value => value with { Explanation = record }; await store.SaveAsync(capture);
            return new { meaning = store.Entries[0].Senses[0].Meaning, record = store.Explanation(record.Input, LearningLanguage.Chinese)?.Id == record.Id };
        }
        if (mode is "monotonic" or "equalRevision")
        {
            store.Apply(new(4, old)); store.Apply(new(mode == "monotonic" ? 3ul : 4ul, new()));
            return new { entries = store.Entries.Count, contains = store.Contains(capture), loaded = store.IsLoaded };
        }
        if (mode == "pending") return await PendingProbe();
        if (mode == "invalidSave")
        {
            var bad = capture with { Expression = "", DictionaryForm = "" }; await store.SaveAsync(bad);
            return new { error = store.ErrorMessage is not null, pending = store.IsPending(bad), loaded = store.IsLoaded, entries = store.Entries.Count };
        }
        await archive.CommitAsync(old); await store.LoadAsync();
        if (mode == "remove")
        {
            await store.RemoveAsync(capture); return new { contains = store.Contains(capture), usages = store.Entries[0].Examples.Count, note = store.Entries[0].Senses[0].Note };
        }
        var input = old.Entries[0].Senses[0].Explanations[0].Input;
        if (mode == "lookup") return new { same = store.Explanation(input, LearningLanguage.Chinese)?.Id == Id(100), wrongLanguage = store.Explanation(input, LearningLanguage.English) is null };
        var withUsage = capture with { Example = old.Entries[0].Examples[0] };
        var standalone = store.ExplanationDestination(capture)!.Value; var usageDestination = store.ExplanationDestination(withUsage)!.Value;
        if (mode == "destination") return new { entry = standalone.EntryId == Id(1), sense = standalone.SenseId == Id(2), noUsage = standalone.ExampleId is null,
            usage = usageDestination.ExampleId == Id(3), capturedUserMeaning = store.UserMeaning(usageDestination) is null };
        var recordForUsage = new LearningExplanationRecord(Id(101), old.Entries[0].Examples[0].Explanations[0].Input, LearningLanguage.Chinese,
            new("adopted", "explanation"), "fixture", DateTimeOffset.UnixEpoch);
        if (mode == "adoptStandalone")
        {
            var record = recordForUsage with { Input = input }; bool ok = await store.AdoptExplanationAsync(record, standalone);
            return new { ok, meaning = store.UserMeaning(standalone) ?? "missing", origin = store.Entries[0].Senses[0].MeaningOrigin.ToString().ToLowerInvariant() };
        }
        if (mode == "adoptMoved")
        {
            await archive.CreateSenseAsync(Id(1), Id(4)); await store.MoveExampleAsync(Id(1), Id(3), Id(2), Id(4));
            bool ok = await store.AdoptExplanationAsync(recordForUsage, usageDestination);
            return new { ok, owner = store.Entries[0].Senses[1].Examples[0].Id == Id(3), meaning = store.UserMeaning(usageDestination) ?? "missing", sourceEmpty = store.Entries[0].Senses[0].Examples.Count == 0 };
        }
        if (mode == "adoptDeleted") await store.RemoveAsync(withUsage);
        if (mode == "adoptStale")
        {
            var original = store.Entries[0]; var edited = VocabularySQLiteParityTests.Sense(old, sense => sense with { Examples = [sense.Examples[0] with { Text = "The bank!" }] }).Entries[0];
            await store.UpdateAsync(edited, original);
        }
        if (mode is "adoptDeleted" or "adoptStale")
        {
            bool ok = await store.AdoptExplanationAsync(recordForUsage, usageDestination); return new { ok, usages = store.Entries[0].Examples.Count, error = store.ErrorMessage is not null };
        }
        if (mode == "senseAlias")
        {
            await archive.CreateSenseAsync(Id(1), Id(4)); await store.MergeSensesAsync(Id(1), Id(2), Id(4));
            var explicitCapture = capture with { SenseId = Id(2) }; return new { contains = store.Contains(explicitCapture), destination = store.ExplanationDestination(explicitCapture)?.SenseId == Id(4) };
        }
        if (mode == "failureThenSuccess")
        {
            await store.RemoveSenseAsync(Id(999), Id(888)); bool beforeError = store.ErrorMessage is not null; await store.SaveAsync(capture);
            return new { beforeError, afterError = store.ErrorMessage is not null, saved = store.Contains(capture) };
        }
        throw new InvalidOperationException("Unknown fixed store mode.");
    }
    private static async Task<object> PendingProbe()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var archive = new VocabularyArchive((string?)null); await using var store = new VocabularyStore(archive, new SynchronizationContext());
        var capture = VocabularyArchiveParityTests.Capture();
        // Exactly the Swift probe: identical captures enter original change twice; the gates
        // postpone only the actual archive save, without changing annotation or saved content.
        var a = store.Change(capture, async () => { firstEntered.SetResult(); await first.Task; return await archive.SaveAsync(capture); });
        var b = store.Change(capture, async () => { secondEntered.SetResult(); await second.Task; return await archive.SaveAsync(capture); });
        try
        {
            await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            bool before = store.IsPending(capture);
            first.SetResult(); await a.WaitAsync(TimeSpan.FromSeconds(10)); bool middle = store.IsPending(capture);
            second.SetResult(); await b.WaitAsync(TimeSpan.FromSeconds(10));
            return new { before, middle, after = store.IsPending(capture), saved = store.Contains(capture) };
        }
        finally { first.TrySetResult(); second.TrySetResult(); }
    }
}
