using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Resources;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class FrozenVocabularySemanticStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "canonical-semantic-" + Guid.NewGuid().ToString("N"));
    private string FileName => Path.Combine(_root, FrozenVocabularyStoreFactory.CanonicalFileName);
    public FrozenVocabularySemanticStoreTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    private static F.VocabularyMeaningNode Node(string name) => new(Guid.NewGuid(), [new VocabularyMeaningLabel(name)]) { CreatedAt = new F.SourceDate(814759445.1234567), UpdatedAt = new F.SourceDate(814759445.1234567) };
    [Fact]
    public async Task IndependentSemanticClassificationTransactionsAcknowledgeAndReopen()
    {
        var a = Node("a"); var b = Node("b"); F.FrozenVocabularyImportSnapshot last;
        await using (var store = FrozenVocabularyStoreFactory.Create(FileName))
        {
            var initial = await store.ReadAsync(); var baseline = initial.Document.Semantics;
            var first = await store.UpdateSemanticsAsync(baseline with { Nodes = [a] }, baseline);
            var next = await store.UpdateSemanticsAsync(baseline with { Nodes = [b] }, baseline);
            Assert.Equal(first.Revision + 1, next.Revision); Assert.Equal(new[] { a.Id, b.Id }, next.Document.Semantics.Nodes.Select(value => value.Id));
            var cat = next.Document.Classification; var category = cat.Categories[0];
            var membership = new VocabularyMeaningCategoryMembership(Guid.NewGuid(), a.Id, category.Id);
            var saved = await store.UpdateClassificationAsync(cat with { Memberships = [membership], Overrides = [new(category.Id) { Definition = "local" }] }, cat);
            var unchanged = await store.UpdateClassificationAsync(saved.Document.Classification, saved.Document.Classification);
            Assert.Equal(saved.Revision + 1, unchanged.Revision);
            var edited = next.Document.Semantics with { Nodes = [b] };
            last = await store.UpdateSemanticsAsync(edited, next.Document.Semantics);
            Assert.Empty(last.Document.Classification.Memberships); Assert.Single(last.Document.Classification.Overrides);
            Assert.Equal(a.CreatedAt, first.Document.Semantics.Nodes[0].CreatedAt);
        }
        await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(FileName);
        Assert.Equal(last, await reopened.ReadAsync());
    }
    [Fact]
    public async Task SameRowConflictAndSqlFailureLeaveAcknowledgedStateUnchanged()
    {
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false); var initial = await store.ReadAsync();
        var node = Node("a"); var saved = await store.UpdateSemanticsAsync(new() { Nodes = [node] }, initial.Document.Semantics);
        var baseline = saved.Document.Semantics; var changed = baseline with { Nodes = [node with { Definition = "first" }] };
        var current = await store.UpdateSemanticsAsync(changed, baseline);
        int notifications = 0;
        using var subscription = store.Subscribe(snapshot => { if (snapshot.Revision > current.Revision) Interlocked.Increment(ref notifications); });
        await Assert.ThrowsAsync<VocabularyException>(() => store.UpdateSemanticsAsync(baseline with { Nodes = [node with { Definition = "second" }] }, baseline));
        Assert.Equal(current, await store.ReadAsync()); Assert.Equal(0, notifications);
        using var connection = VocabularySQLiteConnection.OpenNamed(FileName);
        connection.Execute("CREATE TRIGGER reject_semantic BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
        await Assert.ThrowsAnyAsync<VocabularyException>(() => store.UpdateSemanticsAsync(current.Document.Semantics, current.Document.Semantics));
        Assert.Equal(current, await store.ReadAsync()); Assert.Equal(0, notifications);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
