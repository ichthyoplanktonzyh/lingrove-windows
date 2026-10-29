using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularySenseActionTests
{
    private static readonly F.SourceDate Time = new(814759445.1234567);
    private static F.VocabularyDocument Seed() => F.VocabularyEntryImportPolicy.Plan([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")], new(), Time).Document;
    private static F.VocabularyDocument Commit(F.VocabularyDocument before, F.VocabularySenseDraft draft) => F.CanonicalWritingPolicy.PrepareCommit(before, draft.Document);
    [Fact]
    public void CreateMoveMergeAndDeletePreserveRawAndStableIdentities()
    {
        var doc = Seed(); var entry = doc.Entries[0]; var source = entry.Senses[0].Id; var usage = entry.Examples[0]; var target = Guid.NewGuid();
        doc = Commit(doc, F.VocabularySenseActionPolicy.Create(doc, entry.Id, target, Time));
        var moved = F.VocabularySenseActionPolicy.Move(doc, entry.Id, usage.Id, source, target, Time); doc = Commit(doc, moved);
        Assert.Equal(target, moved.DestinationId); Assert.Empty(doc.Entries[0].Senses.Single(value => value.Id == source).Examples);
        Assert.Contains(usage.Id, doc.Collections.Single(value => value.SenseId == target).InterpretationIds);
        Assert.Equal(usage.SourceId, doc.Entries[0].Senses.Single(value => value.Id == target).Examples[0].SourceId);
        var merged = F.VocabularySenseActionPolicy.Merge(doc, entry.Id, target, source, Time); doc = Commit(doc, merged);
        Assert.Contains(target, doc.Entries[0].Senses[0].MergedFrom); Assert.Contains(usage.Id, doc.Collections[0].InterpretationIds);
        Assert.Throws<VocabularyException>(() => F.VocabularySenseActionPolicy.Merge(doc, entry.Id, target, source, Time));
        var raw = doc.Interpretations.ToArray(); doc = Commit(doc, F.VocabularySenseActionPolicy.Remove(doc, entry.Id, source));
        Assert.Empty(doc.Collections); Assert.Equal(raw, doc.Interpretations); Assert.Single(doc.Catalog[0].Senses); F.VocabularyArchiveValidation.Validate(doc);
        Assert.Throws<VocabularyException>(() => F.VocabularySenseActionPolicy.Remove(doc, entry.Id, target));
    }
    [Fact]
    public void MergeRetainsCardsAndRemoteBindingsWithoutChoosingAmbiguousAssociation()
    {
        var doc = Seed(); var entry = doc.Entries[0]; var source = entry.Senses[0].Id; var target = Guid.NewGuid();
        doc = Commit(doc, F.VocabularySenseActionPolicy.Create(doc, entry.Id, target, Time));
        F.AnkiNoteBinding Binding(Guid sense) => new(Guid.NewGuid(), sense, "http://localhost:8765", "profile", null, "model", "deck", "ID",
            new Dictionary<ReviewField, string> { [ReviewField.Identity] = "ID" }, new Dictionary<string, string> { ["ID"] = sense.ToString("D") }, Time);
        var a = Binding(source); var b = Binding(target);
        var first = new F.VocabularyReview(new ReviewPlan(Guid.NewGuid()), [a], a.Id);
        var second = new F.VocabularyReview(new ReviewPlan(Guid.NewGuid()), [b], b.Id);
        doc = F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, source, first, null).Document;
        doc = F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, target, second, null).Document;
        var card = new F.VocabularyReview(new ReviewPlan(Guid.NewGuid()) { Layout = ReviewLayoutPolicy.Preset(0) });
        doc = F.VocabularyReviewDraftPolicy.SaveReview(doc, entry.Id, source, card, null, card.Plan.Id).Document;
        var result = Commit(doc, F.VocabularySenseActionPolicy.Merge(doc, entry.Id, source, target, Time));
        var merged = result.Collections.Single().Review!;
        Assert.Equal(new[] { b.Id, a.Id }, merged.Bindings.Select(value => value.Id)); Assert.Null(merged.ActiveBindingId);
        Assert.Contains(first.Plan, merged.PreservedPlans); Assert.Equal(card, result.Collections.Single().ReviewCards![0]);
        var updated = a with { Deck = "after merge" };
        result = F.VocabularyReviewDraftPolicy.RecordAnkiBinding(result, entry.Id, source, updated).Document;
        Assert.Equal(updated, result.Collections.Single().Review!.Bindings[1]);
        F.VocabularyArchiveValidation.Validate(result);
    }
    [Fact]
    public void MovingUncollectedUsageDoesNotCollectIt()
    {
        var doc = Seed(); var entry = doc.Entries[0]; var source = entry.Senses[0].Id; var usage = entry.Examples[0].Id; var target = Guid.NewGuid();
        doc = Commit(doc, F.VocabularySenseActionPolicy.Create(doc, entry.Id, target, Time));
        doc = doc with { Collections = doc.Collections.Select(value => value.SenseId == source ? value with { InterpretationIds = [] } : value).ToArray() };
        var moved = Commit(doc, F.VocabularySenseActionPolicy.Move(doc, entry.Id, usage, source, target, Time));
        Assert.Contains(moved.Interpretations, value => value.Id == usage && value.SenseId == target);
        Assert.DoesNotContain(usage, moved.Collections.Single(value => value.SenseId == target).InterpretationIds);
        F.VocabularyArchiveValidation.Validate(moved);
    }
    [Fact]
    public void MergeRebindsGeneralExplanationIdentityWithoutChangingItsDateOrResult()
    {
        var doc = Seed(); var entry = doc.Entries[0]; var source = entry.Senses[0].Id; var target = Guid.NewGuid();
        doc = Commit(doc, F.VocabularySenseActionPolicy.Create(doc, entry.Id, target, Time));
        var catalog = doc.Catalog.ToArray(); var senses = catalog[0].Senses.ToArray();
        senses[0] = senses[0] with { CreatedWithoutContext = true };
        var input = new Cotabby.Core.Vocabulary.Resources.LearningExplanationInput(entry.Expression, entry.DictionaryForm, entry.Language, entry.Kind,
            entry.Expression, [new VocabularyTextRange(0, entry.Expression.Length)], Cotabby.Core.Vocabulary.Resources.LearningExplanationContext.None,
            "sense:" + source.ToString("D").ToUpperInvariant());
        var record = new F.LearningExplanationRecord(Guid.NewGuid(), input, Cotabby.Core.LanguageLearning.LearningLanguage.English,
            new Cotabby.Core.LanguageLearning.LearningExplanationResult("meaning", "explanation"), "model", Time);
        senses[0] = senses[0] with { Explanations = [record] }; catalog[0] = catalog[0] with { Senses = senses }; doc = doc with { Catalog = catalog };
        F.VocabularyArchiveValidation.Validate(doc);
        var result = Commit(doc, F.VocabularySenseActionPolicy.Merge(doc, entry.Id, source, target, Time));
        var rebound = Assert.Single(result.Entries[0].Senses[0].Explanations);
        Assert.Equal(record.Id, rebound.Id); Assert.Equal(record.CreatedAt, rebound.CreatedAt); Assert.Equal(record.Result, rebound.Result);
        Assert.Equal("sense:" + target.ToString("D").ToUpperInvariant(), rebound.Input.OccurrenceIdentity);
        F.VocabularyArchiveValidation.Validate(result);
    }
}
