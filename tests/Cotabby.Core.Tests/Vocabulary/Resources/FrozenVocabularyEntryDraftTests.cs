using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenVocabularyEntryDraftTests
{
    private static readonly F.SourceDate Time = new(814759445.1234567);
    private static F.VocabularyDocument Seed() => F.VocabularyEntryImportPolicy.Plan(
        [new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello there")], new(), Time).Document;
    private static F.VocabularyEntry ChangeSense(F.VocabularyEntry entry, Func<F.VocabularySense, F.VocabularySense> edit) => entry with { Senses = [edit(entry.Senses[0])] };
    [Fact]
    public void ConcurrentCapturesSurviveFieldMergeAndConflictingMeaningFails()
    {
        var baselineDocument = Seed(); var baseline = baselineDocument.Entries[0];
        var live = F.VocabularyEntryImportPolicy.Plan([new VocabularyEntryInput(Expression: "hello", Meaning: "greeting", Example: "hello again")], baselineDocument, Time).Document;
        var edited = ChangeSense(baseline, sense => sense with { Meaning = "welcome", Note = "authored" });
        var result = F.VocabularyEntryDraftPolicy.Update(edited, baseline, live, new F.SourceDate(55));
        Assert.Equal(2, result.CollectedEntries[0].Senses[0].Examples.Count);
        Assert.Equal("welcome", result.Entries[0].Senses[0].Meaning);
        Assert.Equal(VocabularyMeaningOrigin.User, result.Entries[0].Senses[0].MeaningOrigin);
        Assert.Equal(new F.SourceDate(55), result.Catalog[0].UpdatedAt); F.VocabularyArchiveValidation.Validate(result);
        var other = ChangeSense(baseline, sense => sense with { Meaning = "other" });
        Assert.Throws<VocabularyException>(() => F.VocabularyEntryDraftPolicy.Update(other, baseline, result, Time));
        Assert.Throws<VocabularyException>(() => F.VocabularyEntryDraftPolicy.Update(edited, baseline, live with { Collections = [] }, Time));
        Assert.Throws<VocabularyException>(() => F.VocabularyEntryDraftPolicy.Update(baseline with { Senses = [] }, baseline, live, Time));
    }
    [Fact]
    public void CorrectedTextForksEvidenceAndDeletionOnlyUncollects()
    {
        var doc = Seed(); var baseline = doc.Entries[0]; var old = baseline.Senses[0].Examples[0];
        var corrected = old with { Text = "hello world", Meaning = "new contextual meaning" };
        var edited = ChangeSense(baseline, sense => sense with { Examples = [corrected] });
        var updated = F.VocabularyEntryDraftPolicy.Update(edited, baseline, doc, Time);
        var example = updated.Entries[0].Senses[0].Examples[0];
        Assert.Equal(old.Id, example.Id); Assert.NotEqual(old.SourceId, example.SourceId); Assert.NotEqual(old.OccurrenceId, example.OccurrenceId);
        Assert.Equal(VocabularyMeaningOrigin.User, example.MeaningOrigin); Assert.Contains(updated.Sources, source => source.Id == old.SourceId);
        Assert.Equal(old.CreatedAt, example.CreatedAt); F.VocabularyArchiveValidation.Validate(updated);
        var newBaseline = updated.Entries[0];
        var removed = ChangeSense(newBaseline, sense => sense with { Examples = [] });
        var after = F.VocabularyEntryDraftPolicy.Update(removed, newBaseline, updated, Time);
        Assert.Empty(after.CollectedEntries[0].Senses[0].Examples);
        Assert.Contains(after.Interpretations, value => value.Id == old.Id); Assert.Equal(updated.Sources, after.Sources);
        F.VocabularyArchiveValidation.Validate(after);
    }
    [Fact]
    public void NewUsageKeepsCallerIdentityAndBecomesCollected()
    {
        var doc = Seed(); var baseline = doc.Entries[0]; var existing = baseline.Senses[0].Examples[0];
        var added = new F.VocabularyExample(Guid.NewGuid(), "hello again", "", [],
            Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness.Fragment, "", Time);
        var edited = ChangeSense(baseline, sense => sense with { Examples = [existing, added] });
        var result = F.VocabularyEntryDraftPolicy.Update(edited, baseline, doc, Time);
        Assert.Contains(result.Interpretations, value => value.Id == added.Id);
        Assert.Contains(added.Id, result.Collections[0].InterpretationIds);
        F.VocabularyArchiveValidation.Validate(result);
    }
}
