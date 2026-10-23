using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

/// <summary>Regression scenarios adapted directly from pinned Mac archive/sense/explanation tests.</summary>
public sealed class VocabularyArchiveDraftPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch, Later = Now.AddSeconds(1);
    private static VocabularyExample Usage(string text = "The bank.", int offset = 4) => new(Guid.NewGuid(), text, "", [new(offset, 4)], VocabularyExampleCompleteness.Sentence, "draft", Now);
    private static VocabularyCapture Capture(VocabularyExample? usage = null) => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, Example: usage);
    private static VocabularyDocument Saved() => VocabularyPolicy.Save(Capture(Usage()), new(), Now).Document;
    private static VocabularyEntry WithSense(VocabularyEntry entry, VocabularySense sense) => entry with { Senses = [sense] };
    private static void Conflict(Action action) => Assert.Equal(VocabularyFailure.EditConflict, Assert.Throws<VocabularyException>(action).Failure);
    private static LearningExplanationRecord Record(VocabularyEntry entry, VocabularySense sense, VocabularyExample? usage) => new(Guid.NewGuid(),
        LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(entry, usage, sense)), LearningLanguage.Chinese, new("meaning", "explanation"), "fixture", Now);

    [Fact]
    public void StaleEditorPreservesContextsSavedAfterItOpened()
    {
        var first = Saved(); var baseline = first.Entries[0];
        var live = VocabularyPolicy.Save(Capture(Usage("A bank opened.", 2)), first, Later).Document;
        var edited = WithSense(baseline, baseline.Senses[0] with { Note = "Written during capture" });
        var draft = VocabularyArchiveDraftPolicy.Update(edited, baseline, live, Later);
        Assert.Equal(2, draft.Entries[0].Senses.Count); Assert.Equal(2, draft.Entries[0].Examples.Count);
        Assert.Equal(edited.Senses[0].Note, draft.Entries[0].Senses[0].Note); Assert.Empty(baseline.Senses[0].Note);
        Assert.Empty(live.Entries[0].Senses[0].Note); Assert.Equal(baseline.CreatedAt, draft.Entries[0].CreatedAt);
    }

    [Fact]
    public void DifferentConcurrentFieldsMergeWhileDifferentWordingOnSameFieldConflicts()
    {
        var document = Saved(); var baseline = document.Entries[0]; var original = baseline.Senses[0];
        var live = document with { Entries = [WithSense(baseline, original with { Note = "Live note" })] };
        var edited = WithSense(baseline, original with { Tags = ["Work"] });
        var merged = VocabularyArchiveDraftPolicy.Update(edited, baseline, live, Later).Entries[0].Senses[0];
        Assert.Equal("Live note", merged.Note); Assert.Equal(new[] { "Work" }, merged.Tags);
        Conflict(() => VocabularyArchiveDraftPolicy.Update(WithSense(baseline, original with { Note = "Different" }), baseline, live, Later));
        Assert.Equal("Live note", live.Entries[0].Senses[0].Note);
        Assert.Equal("Live note", VocabularyArchiveDraftPolicy.Update(WithSense(baseline, original with { Note = "Live note" }), baseline, live, Later).Entries[0].Senses[0].Note);
    }

    [Fact]
    public void CanonicalEquivalentTextAndListsFollowSwiftValueMergeWithoutNormalizingEditedSpelling()
    {
        var document = Saved(); var baseline = document.Entries[0]; var original = baseline.Senses[0] with { Note = "cafe\u0301", Tags = ["cafe\u0301"] };
        baseline = WithSense(baseline, original); var live = baseline with { Senses = [original with { Note = "Concurrent", Tags = ["Live"] }] };
        var edited = WithSense(baseline, original with { Note = "café", Tags = ["café"] });
        var result = VocabularyArchiveDraftPolicy.Update(edited, baseline, Document(live), Later).Entries[0].Senses[0];
        Assert.Equal("Concurrent", result.Note); Assert.Equal(new[] { "Live" }, result.Tags);
    }

    [Fact]
    public void MeaningEditsAndExplicitAdoptionTransferAuthorshipWhileUnrelatedEditsDoNot()
    {
        var doc = Saved(); var baseline = doc.Entries[0]; var sense = baseline.Senses[0] with { IsSavedWithoutContext = true, Meaning = "Draft" };
        baseline = WithSense(baseline, sense); doc = Document(baseline);
        var changed = VocabularyArchiveDraftPolicy.Update(WithSense(baseline, sense with { Meaning = "My meaning", MeaningLanguage = LearningLanguage.Chinese }), baseline, doc, Later).Entries[0].Senses[0];
        Assert.Equal(VocabularyMeaningOrigin.User, changed.MeaningOrigin); Assert.Equal("My meaning", Assert.Single(changed.StandaloneMeanings).Text);
        var adopted = VocabularyArchiveDraftPolicy.Update(WithSense(baseline, sense with { MeaningOrigin = VocabularyMeaningOrigin.User }), baseline, doc, Later).Entries[0].Senses[0];
        Assert.Equal(VocabularyMeaningOrigin.User, adopted.MeaningOrigin); Assert.Empty(adopted.StandaloneMeanings);
        var note = VocabularyArchiveDraftPolicy.Update(WithSense(baseline, sense with { Note = "note" }), baseline, doc, Later).Entries[0].Senses[0];
        Assert.Equal(VocabularyMeaningOrigin.Captured, note.MeaningOrigin);
    }

    [Fact]
    public void SourceEditAutomaticClearCanReceiveNewMeaningButExplicitUsageMeaningIsOwned()
    {
        var doc = Saved(); var baseline = doc.Entries[0]; var sense = baseline.Senses[0]; var usage = sense.Examples[0] with { MeaningOrigin = VocabularyMeaningOrigin.User };
        baseline = WithSense(baseline, sense with { Examples = [usage] }); doc = Document(baseline);
        var cleared = WithSense(baseline, baseline.Senses[0] with { Examples = [usage with { Text = "A bank opened.", Ranges = [new(2, 4)], Meaning = "" }] });
        var draft = VocabularyArchiveDraftPolicy.Update(cleared, baseline, doc, Later).Entries[0];
        Assert.Equal(VocabularyMeaningOrigin.Captured, draft.Examples[0].MeaningOrigin); Assert.Equal(usage.Id, draft.Examples[0].Id);
        var edited = WithSense(baseline, baseline.Senses[0] with { Examples = [usage with { Meaning = "My contextual meaning" }] });
        Assert.Equal(VocabularyMeaningOrigin.User, VocabularyArchiveDraftPolicy.Update(edited, baseline, doc, Later).Entries[0].Examples[0].MeaningOrigin);
    }

    [Fact]
    public void UsageEditsCompareTheWholeValueAndNeverResurrectConcurrentDeletion()
    {
        var doc = Saved(); var baseline = doc.Entries[0]; var sense = baseline.Senses[0]; var usage = sense.Examples[0];
        var live = Document(WithSense(baseline, sense with { Examples = [usage with { Translation = "live translation" }] }));
        var edited = WithSense(baseline, sense with { Examples = [usage with { Meaning = "My meaning" }] });
        Conflict(() => VocabularyArchiveDraftPolicy.Update(edited, baseline, live, Later));
        var deleted = Document(WithSense(baseline, sense with { Examples = [] }));
        Conflict(() => VocabularyArchiveDraftPolicy.Update(edited, baseline, deleted, Later));
        var removed = WithSense(baseline, sense with { Examples = [] });
        Conflict(() => VocabularyArchiveDraftPolicy.Update(removed, baseline, live, Later));
    }

    [Fact]
    public void NewUsageDeduplicatesByExactContentAndConcurrentCaptureKeepsItsId()
    {
        var doc = Saved(); var baseline = doc.Entries[0]; var sense = baseline.Senses[0]; var added = Usage("A bank opened.", 2);
        var live = Document(WithSense(baseline, sense with { Examples = [sense.Examples[0], added] }));
        var edited = WithSense(baseline, sense with { Examples = [sense.Examples[0], added.CopiedWithNewId()] });
        var draft = VocabularyArchiveDraftPolicy.Update(edited, baseline, live, Later).Entries[0];
        Assert.Equal(2, draft.Examples.Count); Assert.Equal(added.Id, draft.Examples[1].Id);
    }

    [Fact]
    public void StaleSenseDeletionRejectsConcurrentAnnotationAndAliasIdentityIsNotReused()
    {
        var doc = Saved(); var baseline = doc.Entries[0]; var sense = baseline.Senses[0];
        var live = Document(WithSense(baseline, sense with { Note = "Live" }));
        Conflict(() => VocabularyArchiveDraftPolicy.Update(baseline with { Senses = [] }, baseline, live, Later));
        Guid alias = Guid.NewGuid(); live = Document(WithSense(baseline, sense with { MergedFrom = [alias] }));
        var added = baseline with { Senses = [sense, new(alias, "", Now, Now)] };
        Conflict(() => VocabularyArchiveDraftPolicy.Update(added, baseline, live, Later));
    }

    [Fact]
    public void StaleEditDoesNotRecreateRemovedStandaloneMeaningOrClobberLiveReviewMetadata()
    {
        var doc = Saved(); var baseline = doc.Entries[0]; var sense = baseline.Senses[0] with { IsSavedWithoutContext = true, StandaloneMeanings = [new("general", null)] };
        baseline = WithSense(baseline, sense); var review = new VocabularyReview(ReviewPlan.Create());
        var live = Document(WithSense(baseline, sense with { IsSavedWithoutContext = false, StandaloneMeanings = [], Review = review }));
        var edited = WithSense(baseline, sense with { Examples = [], Review = null });
        var result = VocabularyArchiveDraftPolicy.Update(edited, baseline, live, Later).Entries[0].Senses[0];
        Assert.False(result.IsSavedWithoutContext); Assert.Empty(result.StandaloneMeanings); Assert.Empty(result.Examples); Assert.Same(review, result.Review);
    }

    [Fact]
    public void RemovingFavoriteRetainsAnnotatedIdentityAndStandaloneRemovalKeepsContext()
    {
        var doc = Saved(); var entry = doc.Entries[0]; var sense = entry.Senses[0]; var capture = Capture(sense.Examples[0]);
        var annotated = Document(WithSense(entry, sense with { Note = "Keep" }));
        var removed = VocabularyArchiveDraftPolicy.Remove(capture, annotated);
        Assert.Empty(removed.Entries[0].Examples); Assert.Equal("Keep", removed.Entries[0].Senses[0].Note); Assert.Equal(sense.Id, removed.Entries[0].Senses[0].Id);
        Assert.Empty(VocabularyArchiveDraftPolicy.Remove(capture, doc).Entries);
        var standalone = VocabularyPolicy.Save(Capture(), doc, Later).Document;
        var without = VocabularyArchiveDraftPolicy.Remove(Capture(), standalone);
        Assert.Single(without.Entries[0].Examples); Assert.DoesNotContain(without.Entries[0].Senses, s => s.IsSavedWithoutContext);
        Assert.Single(doc.Entries[0].Examples);
    }

    [Fact]
    public void ExplicitSenseCreateRemoveUsesLiveIdsAndPreservesSourceTimestampRules()
    {
        var doc = Saved(); var entry = doc.Entries[0]; Guid newId = Guid.NewGuid();
        var added = VocabularyArchiveDraftPolicy.CreateSense(entry.Id, newId, doc, Later);
        Assert.Equal(newId, added.Entries[0].Senses[1].Id); Assert.Equal(Later, added.Entries[0].UpdatedAt);
        Assert.Equal(Now, added.Entries[0].Senses[0].UpdatedAt);
        var removed = VocabularyArchiveDraftPolicy.RemoveSense(entry.Id, newId, added);
        Assert.Single(removed.Entries[0].Senses); Assert.Equal(Later, removed.Entries[0].UpdatedAt);
        Assert.Empty(VocabularyArchiveDraftPolicy.RemoveSense(entry.Id, entry.Senses[0].Id, doc).Entries);
        Assert.Empty(VocabularyArchiveDraftPolicy.RemoveEntry(entry.Id, doc).Entries); Assert.Single(doc.Entries);
    }

    [Fact]
    public void CommitPruningRetainsMovedRecordsAndDropsEditedGeneralOrUsageInputs()
    {
        var doc = Saved(); var entry = doc.Entries[0]; var sense = entry.Senses[0]; var usage = sense.Examples[0];
        var record = Record(entry, sense, usage); sense = sense with { Examples = [usage with { Explanations = [record] }], Explanations = [Record(entry, sense, null)] };
        doc = Document(WithSense(entry, sense));
        var newSense = new VocabularySense(Guid.NewGuid(), "", Now, Now);
        var moved = VocabularySensePolicy.Move(usage.Id, sense.Id, newSense.Id, entry with { Senses = [sense, newSense] }, Later).Entry;
        var pruned = VocabularyArchiveDraftPolicy.PruneExplanations(Document(moved));
        Assert.Empty(pruned.Entries[0].Senses[0].Explanations); Assert.Same(record, pruned.Entries[0].Examples[0].Explanations[0]);
        var edited = WithSense(entry, sense with { Examples = [sense.Examples[0] with { Text = "The bank changed." }] });
        Assert.Empty(VocabularyArchiveDraftPolicy.PruneExplanations(Document(edited)).Entries[0].Examples[0].Explanations);
        Assert.Single(doc.Entries[0].Examples[0].Explanations);
        var general = WithSense(entry, sense with { IsSavedWithoutContext = true });
        Assert.Single(VocabularyArchiveDraftPolicy.PruneExplanations(Document(general)).Entries[0].Senses[0].Explanations);
        Assert.Empty(VocabularyArchiveDraftPolicy.PruneExplanations(Document(general with { DictionaryForm = "other" })).Entries[0].Senses[0].Explanations);
    }
    [Fact]
    public void PruningRejectsDotNetOnlyMalformedGraphsBeforeDroppingAnyRecord()
    {
        var doc = Saved(); var entry = doc.Entries[0]; var sense = entry.Senses[0]; var usage = sense.Examples[0];
        var record = Record(entry, sense, usage);
        VocabularyDocument[] invalid = [new() { Entries = [null!] }, Document(entry with { Senses = [null!] }),
            Document(WithSense(entry, sense with { Examples = [null!] })),
            Document(WithSense(entry, sense with { Explanations = [null!] })),
            Document(WithSense(entry, sense with { Examples = [usage with { Explanations = [null!] }] })),
            Document(WithSense(entry, sense with { Examples = [usage with { Explanations = [record with { Input = null! }] }] })),
            Document(WithSense(entry, sense with { Examples = [usage with { Explanations = [record with { Language = (LearningLanguage)999 }] }] }))];
        foreach (var value in invalid)
            Assert.Equal(VocabularyFailure.InvalidArchive, Assert.Throws<VocabularyException>(() => VocabularyArchiveDraftPolicy.PruneExplanations(value)).Failure);
        Assert.Single(doc.Entries[0].Examples); Assert.Empty(doc.Entries[0].Examples[0].Explanations);
        Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(() => VocabularyArchiveDraftPolicy.Update(
            WithSense(entry, sense with { Note = null! }), entry, doc, Later)).Failure);
    }
    private static VocabularyDocument Document(VocabularyEntry entry) => new() { Entries = [entry] };
}
