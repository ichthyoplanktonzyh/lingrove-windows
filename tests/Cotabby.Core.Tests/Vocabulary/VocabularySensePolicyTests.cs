using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularySensePolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static VocabularyExample Example(string text = "The bank.", int offset = 4, Guid? id = null) => new(id ?? Guid.NewGuid(), text, "",
        [new(offset, 4)], VocabularyExampleCompleteness.Sentence, "", Now, sourceApplicationId: "fixture.app");
    private static VocabularyCapture Capture(VocabularyExample? example) => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, Example: example);
    private static VocabularySense Sense(Guid? id = null) => new(id ?? Guid.NewGuid(), "", Now, Now);
    private static VocabularyEntry Entry(params VocabularySense[] senses) => new(Guid.NewGuid(), "bank", "bank", LearningLanguage.English,
        VocabularyEntryKind.Word, senses, Now, Now);
    private static LearningExplanationRecord Record(VocabularyCapture capture, string meaning = "银行", LearningLanguage language = LearningLanguage.Chinese) =>
        new(Guid.NewGuid(), LearningExplanationPolicy.Input(capture), language, new(meaning, "A literal explanation."), "fixture", Now);

    [Fact]
    public void RepeatedCaptureKeepsAllIdsAndAddsTranslationVariantsWithoutReplacingPrimary()
    {
        var example = Example() with { Translation = "银行。", TranslationLanguage = LearningLanguage.Chinese };
        var capture = Capture(example);
        var first = VocabularyPolicy.Save(capture, new(), Now);
        var original = Assert.Single(first.Document.Entries); var originalSense = Assert.Single(original.Senses);
        var second = VocabularyPolicy.Save(capture with { Example = example with { Translation = "Banco.", TranslationLanguage = LearningLanguage.Spanish } }, first.Document, Now.AddSeconds(1));
        var current = Assert.Single(second.Document.Entries); var sense = Assert.Single(current.Senses); var usage = Assert.Single(sense.Examples);
        Assert.Equal(first.EntryId, second.EntryId); Assert.Equal(originalSense.Id, sense.Id); Assert.Equal(example.Id, usage.Id);
        Assert.Equal("银行。", usage.Translation); Assert.Equal(new LearningLanguage?[] { LearningLanguage.Chinese, LearningLanguage.Spanish }, usage.TranslationVariants.Select(value => value.Language));
        Assert.Equal(Now, current.CreatedAt); Assert.Equal(Now.AddSeconds(1), current.UpdatedAt);
        Assert.Single(originalSense.Examples[0].TranslationVariants); Assert.True(VocabularyPolicy.Contains(capture, second.Document.Entries));
    }

    [Fact]
    public void EqualGeneratedMeaningsNeverInferSharedSenseForDifferentContexts()
    {
        var first = VocabularyPolicy.Save(Capture(Example() with { Meaning = "same wording" }), new(), Now);
        var second = VocabularyPolicy.Save(Capture(Example("A bank opened.", 2) with { Meaning = "same wording" }), first.Document, Now);
        var entry = Assert.Single(second.Document.Entries);
        Assert.Equal(2, entry.Senses.Count); Assert.NotEqual(entry.Senses[0].Id, entry.Senses[1].Id);
        Assert.All(entry.Senses, sense => Assert.Empty(sense.Meaning));
        Assert.All(entry.Examples, example => Assert.Equal("same wording", example.Meaning));
    }

    [Fact]
    public void StandaloneTranslationsShareOneIdentityAndResavingDoesNotOverwriteUserSummary()
    {
        var capture = Capture(null) with { Meaning = "Captured draft", MeaningLanguage = LearningLanguage.Chinese };
        var first = VocabularyPolicy.Save(capture, new(), Now);
        var entry = first.Document.Entries[0]; var sense = entry.Senses[0] with { Meaning = "User summary", MeaningOrigin = VocabularyMeaningOrigin.User };
        var published = first.Document with { Entries = [entry with { Senses = [sense] }] };
        var saved = VocabularyPolicy.Save(capture with { Meaning = "Other variant", MeaningLanguage = LearningLanguage.Spanish }, published, Now);
        var current = Assert.Single(saved.Document.Entries[0].Senses);
        Assert.Equal(sense.Id, current.Id); Assert.Equal("User summary", current.Meaning); Assert.Equal(VocabularyMeaningOrigin.User, current.MeaningOrigin);
        Assert.True(current.IsSavedWithoutContext); Assert.Equal(2, current.StandaloneMeanings.Count); Assert.Empty(current.Examples);
    }

    [Fact]
    public void ReusedUsageIdAcrossHeadwordsOrOccurrencesGetsNewIdWithoutLosingProvenance()
    {
        var example = Example("bank and bank.", 0) with { Translation = "原句", Meaning = "User", MeaningOrigin = VocabularyMeaningOrigin.User };
        var original = VocabularyPolicy.Save(Capture(example), new(), Now);
        var other = VocabularyPolicy.Save(Capture(example) with { Expression = "other headword" }, original.Document, Now);
        var copied = other.Document.Entries.Single(entry => entry.Expression == "other headword").Examples[0];
        Assert.NotEqual(example.Id, copied.Id); Assert.Equal(example.Text, copied.Text); Assert.Equal(example.Ranges, copied.Ranges);
        Assert.Equal(example.SourceApplicationId, copied.SourceApplicationId); Assert.Equal(example.MeaningOrigin, copied.MeaningOrigin); Assert.Equal(example.CreatedAt, copied.CreatedAt);
        Assert.Equal(example.Id, original.Document.Entries[0].Examples[0].Id);
        var repeated = VocabularyPolicy.Save(Capture(example with { Ranges = [new(9, 4)] }), original.Document, Now);
        Assert.Equal(2, repeated.Document.Entries[0].Senses.Count); Assert.NotEqual(example.Id, repeated.Document.Entries[0].Examples[1].Id);
        Assert.Equal(new VocabularyTextRange(9, 4), Assert.Single(repeated.Document.Entries[0].Examples[1].Ranges));
    }

    [Fact]
    public void AmbiguityAndWrongExplicitDestinationsFailWithoutPublishingPartialSense()
    {
        var example = Example(); var a = Sense() with { Examples = [example] }; var b = Sense() with { Examples = [example.CopiedWithNewId()] };
        var entry = Entry(a, b); var document = new VocabularyDocument { Entries = [entry] }; var capture = Capture(example);
        Assert.Null(VocabularySensePolicy.Destination(capture, entry)); Assert.False(VocabularyPolicy.Contains(capture, document.Entries));
        Assert.Equal(VocabularyFailure.EditConflict, Assert.Throws<VocabularyException>(() => VocabularyPolicy.Save(capture, document, Now)).Failure);
        Assert.Equal(2, entry.Senses.Count); Assert.Same(entry, document.Entries[0]);
        var owned = Entry(a, Sense()); var ownedDocument = document with { Entries = [owned] };
        Assert.Equal(VocabularyFailure.EditConflict, Assert.Throws<VocabularyException>(() => VocabularyPolicy.Save(capture with { SenseId = owned.Senses[1].Id }, ownedDocument, Now)).Failure);
        Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(() => VocabularyPolicy.Save(capture with { SenseId = Guid.NewGuid() }, ownedDocument, Now)).Failure);
        Assert.Single(owned.Senses[0].Examples); Assert.Empty(owned.Senses[1].Examples);
    }

    [Fact]
    public void MergedAliasResolvesCurrentDestinationButMergeUsesExplicitLiveSenseIds()
    {
        var example = Example(); Guid alias = Guid.NewGuid(); var owner = Sense() with { Examples = [example], MergedFrom = [alias] }; var entry = Entry(owner);
        var destination = VocabularySensePolicy.Destination(Capture(example) with { SenseId = alias }, entry)!.Value;
        Assert.Equal(owner.Id, destination.SenseId); Assert.Equal(example.Id, destination.ExampleId); Assert.Equal(0, VocabularySensePolicy.Index(alias, entry));
        Assert.Equal(owner.Id, VocabularySensePolicy.Save(Capture(example) with { SenseId = alias }, entry, Now).Senses[0].Id);
        Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(() => VocabularySensePolicy.Merge(alias, owner.Id, entry, Now)).Failure);
    }

    [Fact]
    public void MergePreservesAuthoredContentAliasesHistoryAndCanonicalScalarTagOrder()
    {
        var targetBlock = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Target content")));
        var sourceBlock = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("Source content")));
        var targetPlan = ReviewPlan.Create(); var sourcePlan = ReviewPlan.Create(); var flexible = new VocabularyReview(ReviewPlan.Create());
        Guid oldAlias = Guid.NewGuid();
        var target = Sense() with { Meaning = "Target", MeaningOrigin = VocabularyMeaningOrigin.User, Note = "Target note",
            Tags = ["😀", "cafe\u0301", "\ue000"], Content = [targetBlock], Examples = [Example()], Review = new(targetPlan), ReviewCards = [] };
        var source = Sense() with { Meaning = "Source", MeaningLanguage = LearningLanguage.Chinese, Note = "Source note",
            Tags = ["f", "café"], Content = [targetBlock, sourceBlock], Examples = [Example("A bank opened.", 2)], MergedFrom = [oldAlias],
            Review = new(sourcePlan), ReviewCards = [flexible], StandaloneMeanings = [new("standalone", null)], IsSavedWithoutContext = true };
        var entry = Entry(source, target); var merged = VocabularySensePolicy.Merge(source.Id, target.Id, entry, Now.AddSeconds(1));
        var sense = Assert.Single(merged.Senses);
        Assert.Equal(target.Id, sense.Id); Assert.Equal("Target", sense.Meaning); Assert.Equal(VocabularyMeaningOrigin.User, sense.MeaningOrigin);
        Assert.Equal("Source", Assert.Single(sense.AlternateMeanings).Text); Assert.Equal("Target note\n\nSource note", sense.Note);
        Assert.Equal(new[] { "cafe\u0301", "f", "\ue000", "😀" }, sense.Tags);
        Assert.Equal(new[] { targetBlock.Id, sourceBlock.Id }, sense.Content!.Select(block => block.Id));
        Assert.Equal(new[] { target.Examples[0].Id, source.Examples[0].Id }, sense.Examples.Select(example => example.Id));
        Assert.Equal(new[] { source.Id, oldAlias }, sense.MergedFrom); Assert.True(sense.IsSavedWithoutContext);
        Assert.Equal(targetPlan, sense.Review!.Plan); Assert.Equal(sourcePlan, Assert.Single(sense.Review.PreservedPlans)); Assert.Same(flexible, Assert.Single(sense.ReviewCards!));
        Assert.Equal(Now.AddSeconds(1), sense.UpdatedAt); Assert.Equal(Now, source.UpdatedAt); Assert.Equal(2, entry.Senses.Count);
    }

    [Fact]
    public void MergeRefusesDuplicateUsageAndMoveKeepsEmptySourceAndExactUsageIdentity()
    {
        var example = Example(); var source = Sense() with { Examples = [example], Note = "Keep note", Review = new(ReviewPlan.Create()) };
        var duplicate = Sense() with { Examples = [example.CopiedWithNewId()] }; var entry = Entry(source, duplicate);
        Assert.Equal(VocabularyFailure.EditConflict, Assert.Throws<VocabularyException>(() => VocabularySensePolicy.Merge(source.Id, duplicate.Id, entry, Now)).Failure);
        Assert.Equal(VocabularyFailure.EditConflict, Assert.Throws<VocabularyException>(() => VocabularySensePolicy.Move(example.Id, source.Id, duplicate.Id, entry, Now)).Failure);
        var moved = VocabularySensePolicy.Move(example.Id, source.Id, null, Entry(source), Now.AddSeconds(1));
        Assert.Equal(2, moved.Entry.Senses.Count); var old = moved.Entry.Senses.Single(sense => sense.Id == source.Id);
        Assert.Empty(old.Examples); Assert.Equal("Keep note", old.Note); Assert.Equal(source.Review, old.Review);
        var target = moved.Entry.Senses.Single(sense => sense.Id == moved.TargetSenseId); Assert.Equal(example, Assert.Single(target.Examples));
        Assert.NotEqual(source.Id, target.Id); Assert.Empty(target.Meaning); Assert.Single(source.Examples);
    }

    [Fact]
    public void LateExplanationFollowsMovedUsageAndCannotRecreateDeletedOrEditedContext()
    {
        var example = Example(); var source = Sense() with { Examples = [example] }; var target = Sense(); var entry = Entry(source, target);
        var record = Record(Capture(example));
        var moved = VocabularySensePolicy.Move(example.Id, source.Id, target.Id, entry, Now).Entry;
        Assert.True(VocabularyPolicy.Accepts(record, moved));
        Assert.Equal(target.Id, LearningExplanationPolicy.Capture(moved, moved.Examples[0]).SenseId);
        var attached = VocabularyPolicy.StoreExplanation(record, moved);
        Assert.Empty(attached.Senses[0].Examples); Assert.Equal("银行", attached.Senses[1].Examples[0].Meaning);
        Assert.Empty(attached.Senses[1].Meaning); Assert.Equal(example.Id, attached.Senses[1].Examples[0].Id);
        var deleted = moved with { Senses = [source with { Examples = [] }, target] };
        Assert.False(VocabularyPolicy.Accepts(record, deleted)); Assert.Equal(deleted, VocabularyPolicy.StoreExplanation(record, deleted));
        var edited = moved with { Senses = [source with { Examples = [] }, target with { Examples = [example with { Text = "The bank changed.", Ranges = [new(4, 4)] }] }] };
        Assert.False(VocabularyPolicy.Accepts(record, edited)); Assert.Equal(edited, VocabularyPolicy.StoreExplanation(record, edited));
    }

    [Fact]
    public void GeneralFirstResultFillsOnlyEmptyAutomaticSummaryAndRefreshKeepsCoreWording()
    {
        var capture = Capture(null); var saved = VocabularyPolicy.Save(capture, new(), Now).Document.Entries[0];
        var first = VocabularyPolicy.StoreExplanation(Record(capture, "first"), saved);
        var refreshed = VocabularyPolicy.StoreExplanation(Record(capture, "second"), first);
        var sense = Assert.Single(refreshed.Senses);
        Assert.Equal("first", sense.Meaning); Assert.Equal(VocabularyMeaningOrigin.Ai, sense.MeaningOrigin);
        Assert.Equal("second", Assert.Single(sense.Explanations).Result.Meaning);
        var authoredBlank = saved with { Senses = [saved.Senses[0] with { MeaningOrigin = VocabularyMeaningOrigin.User }] };
        var preserved = VocabularyPolicy.StoreExplanation(Record(capture), authoredBlank).Senses[0];
        Assert.Empty(preserved.Meaning); Assert.Equal(VocabularyMeaningOrigin.User, preserved.MeaningOrigin); Assert.Single(preserved.Explanations);
        var capturedNonempty = saved with { Senses = [saved.Senses[0] with { Meaning = "captured" }] };
        Assert.Equal("captured", VocabularyPolicy.StoreExplanation(Record(capture), capturedNonempty).Senses[0].Meaning);
    }

    [Fact]
    public void UsageUserMeaningSurvivesRefreshAndRecordsUseLanguageReplacementAndBoundedOrder()
    {
        var example = Example() with { Meaning = "My wording", MeaningOrigin = VocabularyMeaningOrigin.User };
        var capture = Capture(example); var old = Enumerable.Range(0, 24).Select(index => Record(capture, "old " + index, LearningLanguage.Spanish)).ToArray();
        var entry = Entry(Sense() with { Examples = [example with { Explanations = old }] });
        var latest = Record(capture); var attached = VocabularyPolicy.StoreExplanation(latest, entry); var usage = attached.Examples[0];
        Assert.Equal("My wording", usage.Meaning); Assert.Equal(VocabularyMeaningOrigin.User, usage.MeaningOrigin);
        Assert.Equal(24, usage.Explanations.Count); Assert.Equal(old.Skip(1).Select(record => record.Id).Append(latest.Id), usage.Explanations.Select(record => record.Id));
        var refresh = Record(capture, "refresh"); var next = VocabularyPolicy.StoreExplanation(refresh, attached).Examples[0];
        Assert.Equal(24, next.Explanations.Count); Assert.DoesNotContain(next.Explanations, record => record.Id == latest.Id); Assert.Equal(refresh.Id, next.Explanations[^1].Id);
        var invalid = refresh with { Result = new("", "") }; Assert.Equal(attached, VocabularyPolicy.StoreExplanation(invalid, attached));
        Assert.Equal(Now, attached.UpdatedAt); Assert.Equal(Now, usage.CreatedAt); Assert.Equal(24, entry.Examples[0].Explanations.Count);
    }

    [Fact]
    public void ContentValidationRetainsNarrowTypeAndCommonFixedFailureCategory()
    {
        var error = Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Validate(Capture(null) with { Expression = "" }));
        Assert.IsAssignableFrom<VocabularyException>(error); Assert.Equal(VocabularyFailure.InvalidContent, error.Failure);
        Assert.DoesNotContain("bank", error.Message);
    }

    [Fact]
    public void InvalidDotNetLanguageAndAuthorshipCannotBePublishedByCaptureOrExplanation()
    {
        var example = Example(); var capture = Capture(example); var entry = Entry(Sense() with { Examples = [example] });
        var record = Record(capture) with { Language = (LearningLanguage)999 };
        Assert.Same(entry, VocabularyPolicy.StoreExplanation(record, entry)); Assert.Empty(entry.Examples[0].Explanations);
        VocabularyCapture[] invalid = [capture with { MeaningLanguage = (LearningLanguage)999 },
            capture with { Example = example with { MeaningLanguage = (LearningLanguage)999 } },
            capture with { Example = example with { TranslationLanguage = (LearningLanguage)999 } },
            capture with { Example = example with { MeaningOrigin = (VocabularyMeaningOrigin)999 } },
            capture with { Example = example with { TranslationVariants = [new("variant", (LearningLanguage)999)] } },
            capture with { Example = example with { Explanations = [record] } },
            capture with { Example = example with { Explanations = [Record(capture) with { Input = new("bank", "bank", (LearningLanguage)999,
                VocabularyEntryKind.Word, example.Text, example.Ranges) }] } }];
        foreach (var value in invalid)
            Assert.Throws<VocabularyContentValidationException>(() => VocabularyPolicy.Save(value, new(), Now));
    }

    [Fact]
    public void MalformedTagsHaveOneConsistentOrderAcrossAllInputPermutations()
    {
        string a = "éf", b = "e\u0301z", malformed = "e" + new string((char)0xD800, 1);
        string[][] inputs = [[a, b, malformed], [a, malformed, b], [b, a, malformed], [b, malformed, a], [malformed, a, b], [malformed, b, a]];
        foreach (var input in inputs)
        {
            var source = Sense() with { Tags = input.Take(1).ToArray() }; var target = Sense() with { Tags = input.Skip(1).ToArray() };
            var result = VocabularySensePolicy.Merge(source.Id, target.Id, Entry(source, target), Now);
            Assert.Equal(new[] { a, b, malformed }, result.Senses[0].Tags);
        }
    }
}
