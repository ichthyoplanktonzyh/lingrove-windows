using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Resources;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class FrozenVocabularySemanticStoreTests : IDisposable
{
    [Fact]
    public async Task CurrentCatalogPersistsGuidanceThroughRealDatabase()
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory, installBundledClassification: true, loadCatalog: VocabularyClassificationCatalog.CurrentBundled);
        var snapshot = await store.ReadAsync(); Assert.Equal(4, snapshot.Document.Classification.Frameworks.Single().Version); var category = snapshot.Document.Classification.Categories.First(c => c.References.Count > 0); Assert.NotEmpty(category.Localizations);
        var next = await store.UpdateClassificationAsync(snapshot.Document.Classification, snapshot.Document.Classification); var roundtrip = (await store.ReadAsync()).Document.Classification.Categories.Single(c => c.Id == category.Id); Assert.Equal(category, roundtrip); Assert.True(next.Revision > snapshot.Revision);
    }
    [Fact]
    public async Task MeaningOrganizationCommitsTogetherAndConflictDoesNotPublishHalf()
    {
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false); var initial = await store.ReadAsync(); var category = new VocabularyCategory(Guid.NewGuid(), VocabularyCategoryAxis.Content, [new VocabularyMeaningLabel("category")]);
        var baseClassification = (await store.UpdateClassificationAsync(initial.Document.Classification with { Categories = [category] }, initial.Document.Classification)).Document.Classification;
        var node = Node("meaning"); var sem = initial.Document.Semantics with { Nodes = [node] }; var membership = new VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, category.Id); var classification = baseClassification with { Memberships = [membership] };
        var concurrent = await store.UpdateClassificationAsync(baseClassification with { Categories = [category with { Definition = "other" }] }, baseClassification);
        var conflicting = classification with { Categories = [category with { Definition = "mine" }] };
        var organization = Assert.IsAssignableFrom<F.ICanonicalVocabularyMeaningOrganizationStore>(store); await Assert.ThrowsAsync<VocabularyException>(() => organization.UpdateMeaningOrganizationAsync(sem, initial.Document.Semantics, conflicting, baseClassification)); Assert.Equal(concurrent, await store.ReadAsync());
        var saved = await organization.UpdateMeaningOrganizationAsync(sem, initial.Document.Semantics, classification, baseClassification); Assert.Equal(concurrent.Revision + 1, saved.Revision); Assert.Equal(node, Assert.Single(saved.Document.Semantics.Nodes)); Assert.Equal(membership, Assert.Single(saved.Document.Classification.Memberships)); Assert.Equal("other", saved.Document.Classification.Categories.Single().Definition);
    }
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
