using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Actual archive ownership/commit observations are supplied by the complete pinned Mac actor.</summary>
public sealed class VocabularyArchiveParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "vocabulary-archive-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    internal static VocabularyCapture Capture(VocabularyExample? usage = null) => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "core", Example: usage);
    private static Guid Id(int value) => VocabularySQLiteParityTests.Id(value);
    private static string Error(VocabularyException error) => char.ToLowerInvariant(error.Failure.ToString()[0]) + error.Failure.ToString()[1..];
    private static VocabularyDocument Sense(VocabularyDocument doc, Func<VocabularySense, VocabularySense> change) => VocabularySQLiteParityTests.Sense(doc, change);
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CompleteOriginalActorOwnsExpectedCacheAndCommitOutcomes(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        var actual = JsonSerializer.SerializeToNode(await Observe(row.GetProperty("operation").GetString()!));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    }
    private static async Task<object> Observe(string mode)
    {
        // The retained alias is test fault/interleaving setup only. The archive owns/disposes the
        // real memory database; no fake persistence answers the behavior under test.
        var db = VocabularySQLiteDatabase.CreateMemory(); await using var archive = new VocabularyArchive(() => db);
        if (mode == "fresh") { var s = await archive.LoadAsync(); return new { revision = s.Revision, entries = s.Document.Entries.Count }; }
        if (mode == "removeMissing") { var s = await archive.RemoveAsync(Capture()); return new { revision = s.Revision, entries = s.Document.Entries.Count }; }
        if (mode == "removeMissingEntry") { var s = await archive.RemoveEntryAsync(Id(999)); return new { revision = s.Revision, entries = s.Document.Entries.Count }; }
        var old = VocabularySQLiteParityTests.Document();
        if (mode == "userExplanation") old = Sense(old, sense => sense with { Examples = [sense.Examples[0] with { MeaningOrigin = VocabularyMeaningOrigin.User }] });
        if (mode == "explanationMissing")
        {
            var record = old.Entries[0].Senses[0].Explanations[0]; var s = await archive.SaveExplanationAsync(record);
            return new { revision = s.Revision, entries = s.Document.Entries.Count };
        }
        if (mode is "save" or "duplicateSave")
        {
            var usage = old.Entries[0].Examples[0]; var capture = Capture(usage); await archive.SaveAsync(capture);
            var s = mode == "save" ? await archive.LoadAsync() : await archive.SaveAsync(capture);
            return new { revision = s.Revision, entries = s.Document.Entries.Count, senses = s.Document.Entries[0].Senses.Count,
                usages = s.Document.Entries[0].Examples.Count, usageIDRetained = s.Document.Entries[0].Examples[0].Id == usage.Id, contains = VocabularyPolicy.Contains(capture, s.Document.Entries) };
        }
        if (mode == "invalidLoad")
        {
            var bad = Sense(old, sense => sense with { Examples = [sense.Examples[0] with { Ranges = [new(999, 1)] }] }); db.Write(bad, new(), 0);
            var error = await Assert.ThrowsAsync<VocabularyException>(() => archive.LoadAsync());
            return new { error = Error(error), preserved = db.Read().Document == bad };
        }
        if (mode == "rollback") return await RollbackProbe(old);
        await archive.CommitAsync(old);
        if (mode == "commitBeforeLoad") { var s = await archive.LoadAsync(); return new { equal = s.Document == old, revision = s.Revision }; }
        if (mode == "removeStandalone")
        {
            var s = await archive.RemoveAsync(Capture()); var sense = s.Document.Entries[0].Senses[0];
            return new { revision = s.Revision, saved = sense.IsSavedWithoutContext, general = sense.Explanations.Count, usages = sense.Examples.Count, note = sense.Note };
        }
        if (mode == "createRemoveSense")
        {
            await archive.CreateSenseAsync(Id(1), Id(4)); var s = await archive.RemoveSenseAsync(Id(1), Id(4));
            return new { revision = s.Revision, senses = s.Document.Entries[0].Senses.Count, originalRetained = s.Document.Entries[0].Senses[0].Id == Id(2) };
        }
        if (mode == "moveUsage")
        {
            await archive.CreateSenseAsync(Id(1), Id(4)); var s = await archive.MoveExampleAsync(Id(1), Id(3), Id(2), Id(4));
            return new { revision = s.Revision, sourceEmpty = s.Document.Entries[0].Senses[0].Examples.Count == 0,
                ownerRetained = s.Document.Entries[0].Senses[1].Examples[0].Id == Id(3), records = s.Document.Entries[0].Senses[1].Examples[0].Explanations.Count };
        }
        if (mode == "mergeSense")
        {
            await archive.CreateSenseAsync(Id(1), Id(4)); var s = await archive.MergeSensesAsync(Id(1), Id(2), Id(4)); var sense = s.Document.Entries[0].Senses[0];
            return new { revision = s.Revision, senses = s.Document.Entries[0].Senses.Count, alias = sense.MergedFrom.Contains(Id(2)), owner = sense.Id == Id(4), usages = sense.Examples.Count };
        }
        if (mode == "updatePreservesCapture")
        {
            var usage = new VocabularyExample(Id(9), "Another bank.", "", [new(8, 4)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch);
            await archive.SaveAsync(Capture(usage)); var edited = Sense(old, sense => sense with { Note = "edited" }).Entries[0];
            var s = await archive.UpdateAsync(edited, old.Entries[0]); return new { revision = s.Revision, usages = s.Document.Entries[0].Examples.Count, note = s.Document.Entries[0].Senses[0].Note };
        }
        if (mode == "editConflict")
        {
            var first = Sense(old, sense => sense with { Note = "winner" }); await archive.UpdateAsync(first.Entries[0], old.Entries[0]);
            var second = Sense(old, sense => sense with { Note = "stale" }); var error = await Assert.ThrowsAsync<VocabularyException>(() => archive.UpdateAsync(second.Entries[0], old.Entries[0]));
            var s = await archive.LoadAsync(); return new { error = Error(error), revision = s.Revision, note = s.Document.Entries[0].Senses[0].Note };
        }
        if (mode == "prune")
        {
            var next = Sense(old, sense => sense with { Examples = [sense.Examples[0] with { Text = "The bank!" }] }); var s = await archive.CommitAsync(next);
            return new { revision = s.Revision, general = s.Document.Entries[0].Senses[0].Explanations.Count, usage = s.Document.Entries[0].Examples[0].Explanations.Count };
        }
        if (mode is "explanation" or "invalidExplanation" or "userExplanation")
        {
            var input = mode == "userExplanation" ? old.Entries[0].Examples[0].Explanations[0].Input : old.Entries[0].Senses[0].Explanations[0].Input;
            var record = new LearningExplanationRecord(Id(101), input, LearningLanguage.Chinese,
                new(mode == "invalidExplanation" ? "" : "generated", "explanation"), "fixture", DateTimeOffset.UnixEpoch);
            var s = await archive.SaveExplanationAsync(record); var sense = s.Document.Entries[0].Senses[0]; var usage = sense.Examples[0];
            return new { revision = s.Revision, meaning = mode == "userExplanation" ? usage.Meaning : sense.Meaning,
                recordID = (mode == "userExplanation" ? usage.Explanations[^1].Id : sense.Explanations[^1].Id).ToString("D") };
        }
        if (mode == "staleBaseline")
        {
            var winner = Sense(old, sense => sense with { Note = "winner" }); db.Write(winner, old, 1);
            var next = Sense(old, sense => sense with { Note = "stale" }); var error = await Assert.ThrowsAsync<VocabularyException>(() => archive.CommitAsync(next));
            var s = await archive.LoadAsync(); return new { error = Error(error), preserved = s.Document == winner, revision = s.Revision };
        }
        if (mode == "invalidCommit")
        {
            var next = Sense(old, sense => sense with { Examples = [sense.Examples[0] with { Ranges = [new(999, 1)] }] });
            var error = await Assert.ThrowsAsync<VocabularyException>(() => archive.CommitAsync(next)); var s = await archive.LoadAsync();
            return new { error = Error(error), revision = s.Revision, preserved = s.Document == old };
        }
        if (mode == "missingImage")
        {
            var next = Sense(old, sense => sense with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(new(new string('a', 64), 2, 3, 4), ""))] });
            var error = await Assert.ThrowsAsync<VocabularyException>(() => archive.UpdateAsync(next.Entries[0], old.Entries[0])); var s = await archive.LoadAsync();
            return new { error = Error(error), revision = s.Revision, preserved = s.Document == old };
        }
        throw new InvalidOperationException("Unknown fixed archive mode.");
    }
    private static async Task<object> RollbackProbe(VocabularyDocument old)
    {
        var c = VocabularySQLiteConnection.OpenMemory(); VocabularySQLiteDatabase.CreateSchema(c); var db = VocabularySQLiteDatabase.Admit(c);
        await using var archive = new VocabularyArchive(() => db); await archive.CommitAsync(old);
        c.Execute("CREATE TRIGGER fail_sense BEFORE UPDATE ON senses BEGIN SELECT RAISE(ABORT,'test'); END");
        var next = Sense(old, sense => sense with { Note = "not committed" }); var error = await Assert.ThrowsAsync<VocabularyException>(() => archive.CommitAsync(next));
        var s = await archive.LoadAsync(); return new { error = Error(error), revision = s.Revision, cachePreserved = s.Document == old, diskPreserved = db.Read().Document == old };
    }
}
