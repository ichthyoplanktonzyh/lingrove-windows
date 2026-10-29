using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Inference.Tests.Vocabulary;

public sealed class FrozenVocabularyReviewStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-review-" + Guid.NewGuid().ToString("N"));
    private string FileName => Path.Combine(_root, FrozenVocabularyStoreFactory.CanonicalFileName);
    public FrozenVocabularyReviewStoreTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    private static F.VocabularyReview Card(Guid id) => new(new ReviewPlan(id) { Layout = ReviewLayoutPolicy.Preset(0) });
    private static async Task<(Guid Entry, Guid Sense)> Seed(ICanonicalVocabularyStore store)
    {
        await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there")]);
        var snapshot = await store.ReadAsync(); return (snapshot.Document.Catalog[0].Id, snapshot.Document.Catalog[0].Senses[0].Id);
    }
    [Fact]
    public async Task CardCasTemplateAndReceiptPersistWithAcknowledgedRevisionsAndReopen()
    {
        Guid entry, sense; var a = Card(Guid.NewGuid()); var b = Card(Guid.NewGuid());
        await using (var store = FrozenVocabularyStoreFactory.Create(FileName, false))
        {
            (entry, sense) = await Seed(store);
            var first = await store.SaveReviewAsync(entry, sense, a, null, a.Plan.Id);
            var second = await store.SaveReviewAsync(entry, sense, b, null, b.Plan.Id);
            var edited = a with { Plan = a.Plan with { Name = "edited" } };
            var third = await store.SaveReviewAsync(entry, sense, edited, a, a.Plan.Id);
            Assert.Equal(first.Revision + 2, third.Revision); Assert.Equal(new[] { edited, b }, third.Document.Collections[0].ReviewCards);
            await Assert.ThrowsAsync<VocabularyException>(() => store.SaveReviewAsync(entry, sense, a, a, a.Plan.Id));
            Assert.Equal(third.Revision, (await store.ReadAsync()).Revision);
            var binding = new F.AnkiNoteBinding(Guid.NewGuid(), sense, "http://localhost:8765", "profile", 42, "model", "deck", "ID",
                new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = sense.ToString("D") }, new SourceDate(814759445.1234567));
            var receipt = await store.RecordAnkiBindingAsync(entry, sense, binding, a.Plan.Id);
            Assert.Equal(binding, receipt.Document.Collections[0].ReviewCards![0].Bindings[0]);
            var removed = await store.RemoveReviewAsync(entry, sense, b.Plan.Id, b);
            Assert.Equal(receipt.Revision + 1, removed.Revision);
            var template = new ReviewLayoutTemplate(Guid.NewGuid(), "Template", ReviewLayoutPolicy.Preset(0));
            var saved = await store.SaveTemplateAsync(template);
            var missing = await store.RemoveTemplateAsync(Guid.NewGuid());
            Assert.Equal(saved.Revision + 1, missing.Revision); Assert.Equal(saved.Document, missing.Document);
        }
        await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(FileName, installBundledClassification: false);
        var snapshot = await reopened.ReadAsync();
        Assert.Single(snapshot.Document.Collections[0].ReviewCards!);
        Assert.Single(snapshot.Document.ReviewTemplates!);
    }
    [Fact]
    public async Task CompetingDraftsHaveOneWinnerAndOnlyCommittedSnapshotIsPublished()
    {
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false); var (entry, sense) = await Seed(store);
        var card = Card(Guid.NewGuid());
        var start = await store.SaveReviewAsync(entry, sense, card, null, card.Plan.Id);
        var published = new TaskCompletionSource<FrozenVocabularyImportSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = store.Subscribe(snapshot => { if (snapshot.Revision > start.Revision) published.TrySetResult(snapshot); });
        var a = card with { Plan = card.Plan with { Name = "A" } };
        var b = card with { Plan = card.Plan with { Name = "B" } };
        async Task<FrozenVocabularyImportSnapshot?> Attempt(F.VocabularyReview value)
        { try { return await store.SaveReviewAsync(entry, sense, value, card, card.Plan.Id); } catch (VocabularyException) { return null; } }
        var results = await Task.WhenAll(Attempt(a), Attempt(b));
        var winner = Assert.Single(results, value => value is not null)!;
        Assert.Equal(start.Revision + 1, winner.Revision);
        Assert.Equal(winner, await published.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(winner, await store.ReadAsync());
    }
    [Fact]
    public async Task SqlFailureAndCancellationDoNotPublishOrMutate()
    {
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false); var (entry, sense) = await Seed(store);
        var baseline = await store.ReadAsync(); int notifications = 0;
        using var subscription = store.Subscribe(snapshot => { if (snapshot.Revision > baseline.Revision) Interlocked.Increment(ref notifications); });
        using var connection = VocabularySQLiteConnection.OpenNamed(FileName);
        connection.Execute("CREATE TRIGGER reject_review BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
        await Assert.ThrowsAnyAsync<VocabularyException>(() => store.SaveReviewAsync(entry, sense, Card(Guid.NewGuid()), null));
        connection.Execute("DROP TRIGGER reject_review");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RemoveTemplateAsync(Guid.NewGuid(), cancel.Token));
        var after = await store.ReadAsync(); Assert.Equal(baseline, after); Assert.Equal(0, notifications);
    }
    [Fact]
    public async Task MissingImageIsRejectedAndUnreferencedSuppliedMediaIsIgnored()
    {
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false); var (entry, sense) = await Seed(store);
        var before = await store.ReadAsync();
        var image = new VocabularyImageReference(new string('a', 64), 1, 1, 20);
        var block = new VocabularyContentBlock(Guid.NewGuid(), new VocabularyBlockContent.Image(image));
        var part = new ReviewCardPart(Guid.NewGuid(), ReviewContentSource.Custom, content: [block]);
        var layout = new ReviewLayout([part], []);
        var template = new ReviewLayoutTemplate(Guid.NewGuid(), "Image", layout);
        await Assert.ThrowsAsync<VocabularyException>(() => store.SaveTemplateAsync(template));
        var supplied = new Dictionary<string, VocabularyPreparedImage> { [image.Filename] = new(image, [1, 2, 3]) };
        Assert.Equal(before, await store.ReadAsync());
        var saved = await store.SaveReviewAsync(entry, sense, Card(Guid.NewGuid()), null, importedImages: supplied);
        Assert.Equal(before.Revision + 1, saved.Revision);
    }
    [Fact]
    public async Task RemovingLastImageReferenceCommitsEvenWhenFileWasAlreadyMissing()
    {
        var image = new VocabularyImageReference(new string('a', 64), 1, 1, 20);
        var block = new VocabularyContentBlock(Guid.NewGuid(), new VocabularyBlockContent.Image(image));
        var part = new ReviewCardPart(Guid.NewGuid(), ReviewContentSource.Custom, content: [block]);
        var template = new ReviewLayoutTemplate(Guid.NewGuid(), "Existing", new ReviewLayout([part], []));
        using (var database = FrozenVocabularySQLiteDatabase.OpenFile(FileName))
        {
            var before = database.Read();
            database.Write(before.Document with { ReviewTemplates = [template] }, before.Document, before.Revision);
        }
        await using var store = FrozenVocabularyStoreFactory.Create(FileName, false);
        var baseline = await store.ReadAsync();
        var removed = await store.RemoveTemplateAsync(template.Id);
        Assert.Equal(baseline.Revision + 1, removed.Revision);
        Assert.Empty(removed.Document.ReviewTemplates!);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
