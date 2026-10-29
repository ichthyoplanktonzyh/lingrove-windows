using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularyReviewDraftTests
{
    private static F.VocabularyDocument Document() => F.VocabularyEntryImportPolicy.Plan(
        [new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there")], new(), new F.SourceDate(10)).Document;
    private static F.VocabularyReview Card(Guid id) => new(new ReviewPlan(id) { Layout = ReviewLayoutPolicy.Preset(0) });
    private static F.AnkiNoteBinding Binding(Guid id, Guid sense, double date) => new(id, sense, "http://localhost:8765", "profile", 1,
        "model", "deck", "ID", new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = "value" }, new F.SourceDate(date));

    [Fact]
    public void CardCasKeepsConcurrentOtherCardAndAllEvidence()
    {
        var doc = Document(); var entry = doc.Catalog[0]; var sense = entry.Senses[0];
        var a = Card(Guid.NewGuid()); var b = Card(Guid.NewGuid());
        var first = F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, sense.Id, a, null, a.Plan.Id).Document;
        var second = F.VocabularyReviewDraftPolicy.SaveReview(first, entry.Id, sense.Id, b, null, b.Plan.Id).Document;
        var nextA = a with { Plan = a.Plan with { Name = "changed" } };
        var result = F.VocabularyReviewDraftPolicy.SaveReview(second, entry.Id, sense.Id, nextA, a, a.Plan.Id);
        Assert.True(result.NeedsCommit); Assert.Equal(new[] { nextA, b }, result.Document.Collections[0].ReviewCards);
        Assert.Equal(doc.Sources, result.Document.Sources); Assert.Equal(doc.Occurrences, result.Document.Occurrences);
        Assert.Equal(doc.Interpretations, result.Document.Interpretations); Assert.Equal(doc.Catalog, result.Document.Catalog);
        Assert.Throws<VocabularyException>(() => F.VocabularyReviewDraftPolicy.SaveReview(result.Document, entry.Id, sense.Id, a, a, a.Plan.Id));
        Assert.Throws<VocabularyException>(() => F.VocabularyReviewDraftPolicy.SaveReview(doc with { Collections = [] }, entry.Id, sense.Id, a, null, a.Plan.Id));
        Assert.Throws<VocabularyException>(() => F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, sense.Id, a, null, b.Plan.Id));
        F.VocabularyArchiveValidation.Validate(result.Document);
    }

    [Fact]
    public void ReceiptFollowsMergedAliasPreservesPlanAndRequiresChoiceForMultipleBindings()
    {
        var doc = Document(); var entry = doc.Catalog[0]; var sense = entry.Senses[0]; var alias = Guid.NewGuid();
        doc = doc with { Catalog = [entry with { Senses = [sense with { MergedFrom = [alias] }] }] };
        var review = new F.VocabularyReview(new ReviewPlan(Guid.NewGuid()) { Deck = "latest" });
        doc = F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, sense.Id, review, null).Document;
        var first = Binding(Guid.NewGuid(), alias, 814759445.1234567);
        doc = F.VocabularyReviewDraftPolicy.RecordAnkiBinding(doc, entry.Id, alias, first).Document;
        Assert.Equal(first.Id, doc.Collections[0].Review!.ActiveBindingId);
        var current = doc.Collections[0].Review!;
        var edited = current with { Plan = current.Plan with { Deck = "newer" }, PreservedPlans = [new ReviewPlan(Guid.NewGuid())] };
        doc = F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, sense.Id, edited, current).Document;
        var second = Binding(Guid.NewGuid(), sense.Id, 1.25);
        doc = F.VocabularyReviewDraftPolicy.RecordAnkiBinding(doc, entry.Id, alias, second).Document;
        Assert.Null(doc.Collections[0].Review!.ActiveBindingId); Assert.Equal(edited.Plan, doc.Collections[0].Review!.Plan);
        Assert.Equal(edited.PreservedPlans, doc.Collections[0].Review!.PreservedPlans);
        Assert.Equal(first.SyncedAt, doc.Collections[0].Review!.Bindings[0].SyncedAt);
        var updated = first with { Deck = "updated" };
        doc = F.VocabularyReviewDraftPolicy.RecordAnkiBinding(doc, entry.Id, alias, updated).Document;
        Assert.Equal(updated, doc.Collections[0].Review!.Bindings[0]); Assert.Equal(second, doc.Collections[0].Review!.Bindings[1]);
        Assert.Throws<VocabularyException>(() => F.VocabularyReviewDraftPolicy.RecordAnkiBinding(doc with { Collections = [] }, entry.Id, alias, first));
        Assert.Throws<VocabularyException>(() => F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, alias, review, null));
    }

    [Fact]
    public void RemoveCardAndTemplatesPreserveOptionalityAndSourceCommitIntent()
    {
        var doc = Document(); var entry = doc.Catalog[0]; var sense = entry.Senses[0]; var card = Card(Guid.NewGuid());
        doc = F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, sense.Id, card, null, card.Plan.Id).Document;
        var removed = F.VocabularyReviewDraftPolicy.RemoveReview(doc, entry.Id, sense.Id, card.Plan.Id, card);
        Assert.True(removed.NeedsCommit); Assert.NotNull(removed.Document.Collections[0].ReviewCards); Assert.Empty(removed.Document.Collections[0].ReviewCards!);
        Assert.Throws<VocabularyException>(() => F.VocabularyReviewDraftPolicy.RemoveReview(removed.Document, entry.Id, sense.Id, card.Plan.Id, card));
        var absent = F.VocabularyReviewDraftPolicy.RemoveTemplate(doc, Guid.NewGuid()); Assert.True(absent.NeedsCommit); Assert.Null(absent.Document.ReviewTemplates);
        var a = new ReviewLayoutTemplate(Guid.NewGuid(), "A", ReviewLayoutPolicy.Preset(0));
        var b = new ReviewLayoutTemplate(Guid.NewGuid(), "B", ReviewLayoutPolicy.Preset(0));
        Assert.Throws<VocabularyException>(() => F.VocabularyReviewDraftPolicy.SaveTemplate(doc, a with { Name = " " }));
        doc = F.VocabularyReviewDraftPolicy.SaveTemplate(doc, a).Document;
        doc = F.VocabularyReviewDraftPolicy.SaveTemplate(doc, b).Document;
        doc = F.VocabularyReviewDraftPolicy.SaveTemplate(doc, a with { Name = "A2" }).Document;
        Assert.Equal(new[] { "A2", "B" }, doc.ReviewTemplates!.Select(value => value.Name));
        var missing = F.VocabularyReviewDraftPolicy.RemoveTemplate(doc, Guid.NewGuid()); Assert.True(missing.NeedsCommit); Assert.Equal(doc, missing.Document);
        doc = F.VocabularyReviewDraftPolicy.RemoveTemplate(doc, a.Id).Document;
        doc = F.VocabularyReviewDraftPolicy.RemoveTemplate(doc, b.Id).Document;
        Assert.NotNull(doc.ReviewTemplates); Assert.Empty(doc.ReviewTemplates!);
    }
}
