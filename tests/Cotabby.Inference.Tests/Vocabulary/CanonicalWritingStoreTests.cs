using Cotabby.Core.LanguageLearning;
using LiveRecord = Cotabby.Core.LanguageLearning.LearningExplanationRecord;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class CanonicalWritingStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-canonical-writing-" + Guid.NewGuid().ToString("N"));
    private string FileName => Path.Combine(_root, FrozenVocabularyStoreFactory.CanonicalFileName);
    public CanonicalWritingStoreTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    private static VocabularyCapture Capture(string text, Guid id) => new("hello", "hello", LearningLanguage.English, VocabularyEntryKind.Word,
        Example: new(id, text, "你好", [new(0, 5)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UtcNow,
            translationLanguage: LearningLanguage.Chinese, sourceApplicationId: "example.editor"));
    [Fact]
    public async Task WritingAndImportShareOneStoreAndReopenWithExactUncollection()
    {
        var old = Path.Combine(_root, "vocabulary.sqlite"); byte[] sentinel = [1, 2, 3, 4]; File.WriteAllBytes(old, sentinel);
        await Assert.ThrowsAsync<VocabularyException>(() => FrozenVocabularyStoreFactory.OpenAsync(old));
        await using (var store = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false))
        {
            var one = Capture("hello there", Guid.NewGuid()); var two = Capture("hello friend", Guid.NewGuid());
            var first = await store.SaveCaptureAsync(one); var second = await store.SaveCaptureAsync(two);
            Assert.True(first.Changed); Assert.True(second.Changed); Assert.NotEqual(first.Destination!.Value.SenseId, second.Destination!.Value.SenseId);
            var before = await store.ReadAsync(); Assert.Equal(2, before.Document.Sources.Count); Assert.Equal(2, before.Document.CollectedEntries[0].Examples.Count);
            var occurrence = before.Document.Occurrences.Single(x => x.Id == one.Example!.Id);
            var source = before.Document.Sources.Single(x => x.Id == occurrence.SourceId);
            Assert.Equal("hello there", source.Text); Assert.Equal("example.editor", source.SourceApplicationId);
            Assert.Equal(Cotabby.Core.Vocabulary.Resources.VocabularySourceKind.Captured, source.Kind);
            Assert.Equal(one.Example!.Ranges, occurrence.Ranges); Assert.Contains(before.Document.Interpretations, x => x.Id == one.Example.Id);
            var input = LearningExplanationPolicy.Input(one); var explanation = new LiveRecord(Guid.NewGuid(), input, LearningLanguage.Chinese,
                new("greeting", "Used as a greeting."), "local", DateTimeOffset.UtcNow);
            var recorded = await store.SaveExplanationAsync(explanation); Assert.True(recorded.Changed);
            Assert.Equal(explanation.Id, (await store.FindExplanationAsync(input, LearningLanguage.Chinese))!.Id);
            var adopted = await store.AdoptExplanationAsync(explanation, first.Destination!.Value); Assert.True(adopted.Changed);
            var updated = await store.ReadAsync(); var usage = updated.Document.CollectedEntries[0].Examples.Single(x => x.Id == one.Example!.Id);
            Assert.Equal(VocabularyMeaningOrigin.User, usage.MeaningOrigin); Assert.Equal("greeting", usage.Meaning);
            var refresh = explanation with { Id = Guid.NewGuid(), Result = new("different", "A refreshed explanation.") };
            Assert.True((await store.SaveExplanationAsync(refresh)).Changed);
            usage = (await store.ReadAsync()).Document.CollectedEntries[0].Examples.Single(x => x.Id == one.Example!.Id);
            Assert.Equal("greeting", usage.Meaning); Assert.Equal(VocabularyMeaningOrigin.User, usage.MeaningOrigin);
            var removed = await store.RemoveCaptureAsync(one); Assert.True(removed.Changed);
            Assert.Null(await store.FindExplanationAsync(input, LearningLanguage.Chinese));
            Assert.False((await store.AdoptExplanationAsync(refresh, first.Destination!.Value)).Changed);
            var after = await store.ReadAsync(); Assert.Equal(2, after.Document.Sources.Count); Assert.Equal(2, after.Document.Interpretations.Count);
            Assert.Single(after.Document.CollectedEntries[0].Examples); Assert.Equal(two.Example!.Id, after.Document.CollectedEntries[0].Examples[0].Id);
            Assert.False((await store.RemoveCaptureAsync(one)).Changed);
            var imported = await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "world", Meaning: "earth")]); Assert.True(imported.HasChanges);
        }
        await using (var reopened = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false))
        {
            var snapshot = await reopened.ReadAsync(); Assert.Equal(2, snapshot.Document.Catalog.Count);
            Assert.Equal(2, snapshot.Document.Sources.Count); Assert.Equal(2, snapshot.Document.Interpretations.Count);
        }
        Assert.Equal(sentinel, File.ReadAllBytes(old));
    }
    [Fact]
    public async Task InvalidOrCancelledCaptureDoesNotChangeRevision()
    {
        await using var store = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveCaptureAsync(Capture("hello there", Guid.NewGuid()), cancelled.Token));
        Assert.Equal(0UL, (await store.ReadAsync()).Revision);
        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveCaptureAsync(new("", "", LearningLanguage.English, VocabularyEntryKind.Word)));
        Assert.Equal(0UL, (await store.ReadAsync()).Revision);
    }
    [Fact]
    public async Task UncollectedRemovalKeepsPersonalNoteAndRepeatSaveUsesStableIdentity()
    {
        await using var store = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false);
        var imported = await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Note: "authored")]);
        var selection = imported.Selection!.Value;
        var capture = new VocabularyCapture("hello", "hello", LearningLanguage.English, VocabularyEntryKind.Word, Meaning: "greeting", SenseId: selection.SenseId);
        var first = await store.SaveCaptureAsync(capture); var again = await store.SaveCaptureAsync(capture);
        Assert.Equal(first.Destination, again.Destination); Assert.Equal(first.Revision + 1, again.Revision);
        Assert.True((await store.RemoveCaptureAsync(capture)).Changed);
        var doc = (await store.ReadAsync()).Document; Assert.Equal("authored", doc.Collections.Single().Note);
        Assert.False(doc.Collections.Single().IsSavedWithoutContext); Assert.Single(doc.Catalog[0].Senses);
    }
    [Fact]
    public async Task InvalidEnrichedExplanationDoesNotBlockAnOtherwiseValidCapture()
    {
        await using var store = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false);
        var example = new Cotabby.Core.Vocabulary.VocabularyExample(Guid.NewGuid(), "hello there", "", [], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UtcNow);
        var input = new LearningExplanationInput("hello", "hello", LearningLanguage.English, VocabularyEntryKind.Word,
            "hello there", [], LearningExplanationContext.Sentence);
        var explanation = new LiveRecord(Guid.NewGuid(), input, LearningLanguage.English, new("hello", "greeting"), "local", DateTimeOffset.UtcNow);
        var capture = new VocabularyCapture("hello", "hello", LearningLanguage.English, VocabularyEntryKind.Word, Example: example, Explanation: explanation);
        Assert.True((await store.SaveCaptureAsync(capture)).Changed);
        var doc = (await store.ReadAsync()).Document; Assert.Empty(doc.Interpretations[0].Explanations);
    }
    [Fact]
    public async Task MeaningChangesInvalidateConfirmedSemanticEvidence()
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory();
        await using var store = new FrozenVocabularyImportStore(database);
        var capture = Capture("hello there", Guid.NewGuid()); var saved = await store.SaveCaptureAsync(capture);
        var before = await store.ReadAsync(); var senseId = saved.Destination!.Value.SenseId;
        var nodeId = Guid.NewGuid(); var node = new Cotabby.Core.Vocabulary.Frozen.VocabularyMeaningNode(nodeId,
            [new Cotabby.Core.Vocabulary.Resources.VocabularyMeaningLabel("greeting") { Language = LearningLanguage.English }]);
        var mapping = new Cotabby.Core.Vocabulary.Resources.VocabularyMeaningMapping(nodeId, senseId) { EvidenceIds = [capture.Example!.Id] };
        var seeded = before.Document with { Semantics = new() { Nodes = [node], Mappings = [mapping] } };
        database.Write(seeded, before.Document, before.Revision);
        var input = LearningExplanationPolicy.Input(capture);
        var record = new LiveRecord(Guid.NewGuid(), input, LearningLanguage.English, new("greeting", "A greeting."), "local", DateTimeOffset.UtcNow);
        Assert.True((await store.SaveExplanationAsync(record)).Changed);
        var result = await store.ReadAsync(); var changed = Assert.Single(result.Document.Semantics.Mappings);
        Assert.Empty(changed.EvidenceIds); Assert.Equal(Cotabby.Core.Vocabulary.Resources.VocabularySemanticStatus.Proposed, changed.Provenance.Status);
    }
    [Fact]
    public async Task ResolvingUnknownCompletenessPrunesOnlyStaleExplanation()
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory();
        await using var store = new FrozenVocabularyImportStore(database);
        var capture = Capture("hello there", Guid.NewGuid()); await store.SaveCaptureAsync(capture);
        var before = await store.ReadAsync(); var claim = before.Document.Interpretations[0];
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("11:hello there3:0:57:unknown")));
        var input = new Cotabby.Core.Vocabulary.Resources.LearningExplanationInput("hello", "hello", LearningLanguage.English, VocabularyEntryKind.Word,
            "hello there", [new(0, 5)], Cotabby.Core.Vocabulary.Resources.LearningExplanationContext.Unknown, key);
        var old = new Cotabby.Core.Vocabulary.Frozen.LearningExplanationRecord(Guid.NewGuid(), input, LearningLanguage.English,
            new("greeting", "A greeting."), "local", new SourceDate(0));
        var unknown = claim with { Completeness = Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness.Unknown, Explanations = [old] };
        var seeded = before.Document with { Interpretations = [unknown] }; database.Write(seeded, before.Document, before.Revision);
        Assert.True((await store.SaveCaptureAsync(capture)).Changed);
        var next = (await store.ReadAsync()).Document.Interpretations[0];
        Assert.Equal(capture.Example!.Id, next.Id); Assert.Equal(Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness.Sentence, next.Completeness);
        Assert.Empty(next.Explanations);
    }
    [Fact]
    public async Task FailedSqlCommitRollsBackTheWholeCaptureAndPublishesNothing()
    {
        await using var store = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false);
        using var connection = VocabularySQLiteConnection.OpenNamed(FileName);
        connection.Execute("CREATE TRIGGER reject_state BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
        int notifications = 0; using var subscription = store.Subscribe(_ => Interlocked.Increment(ref notifications));
        await Assert.ThrowsAnyAsync<VocabularyException>(() => store.SaveCaptureAsync(Capture("hello there", Guid.NewGuid())));
        var snapshot = await store.ReadAsync(); Assert.Equal(0UL, snapshot.Revision); Assert.Empty(snapshot.Document.Catalog);
        Assert.Empty(snapshot.Document.Sources); Assert.Empty(snapshot.Document.Interpretations); Assert.Equal(0, notifications);
    }
    [Fact]
    public async Task DisposalClosesAdmissionAndDrainsPreviouslyAcceptedCapture()
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory(); var store = new FrozenVocabularyImportStore(database);
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var blocker = Task.Run(() => database.Access(true, () => { held.Set(); release.Wait(); return true; }));
        Assert.True(held.Wait(TimeSpan.FromSeconds(10)));
        var accepted = store.SaveCaptureAsync(Capture("hello there", Guid.NewGuid()));
        var disposal = store.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ReadAsync());
            Assert.False(disposal.IsCompleted);
        }
        finally { release.Set(); }
        Assert.True((await accepted.WaitAsync(TimeSpan.FromSeconds(10))).Changed);
        await blocker.WaitAsync(TimeSpan.FromSeconds(10)); await disposal.WaitAsync(TimeSpan.FromSeconds(10));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
