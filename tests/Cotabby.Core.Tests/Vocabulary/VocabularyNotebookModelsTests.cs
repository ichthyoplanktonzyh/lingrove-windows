using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyNotebookModelsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static VocabularySense Sense(Guid? id = null) => new(id ?? Guid.NewGuid(), "meaning", Now, Now);
    private static VocabularyEntry Entry(IReadOnlyList<VocabularySense> senses) => new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, senses, Now, Now);
    private static LearningExplanationRecord Explanation(Guid id, string meaning) => new(id,
        new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "bank", [new(0, 4)]),
        LearningLanguage.Chinese, new(meaning, "Explanation"), "fixture", Now);
    private static VocabularyExample Usage(Guid id, IReadOnlyList<LearningExplanationRecord> explanations) =>
        new(id, "The bank.", "", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "", Now, explanations: explanations);

    [Fact]
    public void EntryProjectionsKeepUsageOrderAndFirstExplanationUuidWithoutMutatingOwners()
    {
        Guid duplicate = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid();
        var first = Explanation(duplicate, "first wins"); var changed = first with { Result = new("later duplicate", "other") };
        var a = Sense() with { Explanations = [first], Examples = [Usage(Guid.NewGuid(), [changed, Explanation(second, "second")])] };
        var b = Sense() with { Explanations = [Explanation(third, "third")], Examples = [Usage(Guid.NewGuid(), [first])] };
        var entry = Entry([a, b]);
        Assert.Equal(new[] { a.Examples[0].Id, b.Examples[0].Id }, entry.Examples.Select(value => value.Id));
        Assert.Equal(new[] { duplicate, second, third }, entry.Explanations!.Select(value => value.Id));
        Assert.Equal("first wins", entry.Explanations![0].Result.Meaning);
        Assert.Equal(2, a.Examples[0].Explanations.Count); Assert.Single(b.Examples[0].Explanations);
        Assert.Null(Entry([Sense()]).Explanations);
        Assert.Equal(2, Entry([a, a]).Examples.Count); // Headword projection never deduplicates usages.
    }

    [Fact]
    public void LegacyAndFlexibleReviewSlotsAndRetainedLayoutsStayIndependentAndOrdered()
    {
        ReviewLayout Layout(ReviewContentSource source) => new([ReviewCardPart.Create(source)]);
        var legacy = new VocabularyReview(ReviewPlan.Create() with { Layout = Layout(ReviewContentSource.Expression) },
            preservedPlans: [ReviewPlan.Create() with { Layout = Layout(ReviewContentSource.Notes) }]);
        var card = new VocabularyReview(ReviewPlan.Create() with { Layout = Layout(ReviewContentSource.Meaning) },
            preservedPlans: [ReviewPlan.Create() with { Layout = Layout(ReviewContentSource.Translation) }]);
        var initial = Sense() with { Review = legacy };
        Assert.Same(legacy, initial.ReviewValue(null)); Assert.Null(initial.ReviewValue(card.Plan.Id)); Assert.Null(initial.ReviewCards);
        var appended = initial.WithReview(card, card.Plan.Id);
        Assert.Null(initial.ReviewCards); Assert.Same(legacy, appended.Review); Assert.Same(card, appended.ReviewValue(card.Plan.Id));
        Assert.Equal(new[] { ReviewContentSource.Expression, ReviewContentSource.Notes, ReviewContentSource.Meaning, ReviewContentSource.Translation },
            appended.ReviewLayouts.Select(layout => layout.Front[0].Source));
        var changed = card with { Plan = card.Plan with { Answer = "Review only" } };
        var replaced = appended.WithReview(changed, card.Plan.Id);
        Assert.Single(replaced.ReviewCards!); Assert.Same(changed, replaced.ReviewValue(card.Plan.Id)); Assert.Same(card, appended.ReviewValue(card.Plan.Id));
        Assert.Same(card, replaced.WithReview(card, null).Review); Assert.Equal(changed, replaced.ReviewCards![0]);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("reviewCards")]
    [InlineData("review")]
    public void AbsentSenseExtensionsRemainDifferentFromExplicitEmptyValues(string extension)
    {
        var original = Sense();
        var changed = extension switch
        {
            "content" => original with { Content = [] },
            "reviewCards" => original with { ReviewCards = [] },
            _ => original with { Review = new(new(Guid.NewGuid())) },
        };
        Assert.NotEqual(original, changed); Assert.Null(original.Content); Assert.Null(original.ReviewCards); Assert.Null(original.Review);
    }

    [Fact]
    public void GraphSnapshotsDetachDraftCollectionsAndRetainNullTemplatesAndUnsignedRevision()
    {
        var tags = new List<string> { "work" }; var aliases = new List<Guid> { Guid.NewGuid() };
        var senses = new List<VocabularySense> { Sense() with { Tags = tags, MergedFrom = aliases } };
        var entry = Entry(senses); var entries = new List<VocabularyEntry> { entry };
        var document = new VocabularyDocument { Entries = entries };
        var clone = document with { Entries = entries, ReviewTemplates = [] };
        tags.Clear(); aliases.Clear(); senses.Clear(); entries.Clear();
        Assert.Single(document.Entries); Assert.Single(entry.Senses); Assert.Equal("work", Assert.Single(entry.Senses[0].Tags));
        Assert.Single(entry.Senses[0].MergedFrom); Assert.Null(document.ReviewTemplates); Assert.Empty(clone.ReviewTemplates!);
        Assert.NotEqual(document, clone); Assert.Equal(3, document.SchemaVersion);
        Assert.True(Assert.IsAssignableFrom<ICollection<VocabularyEntry>>(document.Entries).IsReadOnly);
        var snapshot = new VocabularySnapshot(ulong.MaxValue, document);
        Assert.Equal(ulong.MaxValue, snapshot.Revision); Assert.Same(document, snapshot.Document);
    }

    [Fact]
    public void StructuralEqualityIncludesAuthorshipOrderAliasesAndAllOptionalGraphFields()
    {
        Guid id = Guid.NewGuid(); var original = Sense(id) with { Tags = ["cafe\u0301"], MergedFrom = [Guid.NewGuid()] };
        var same = Sense(id) with { Tags = ["café"], MergedFrom = original.MergedFrom.ToArray() };
        Assert.Equal(original, same); Assert.Equal(original.GetHashCode(), same.GetHashCode());
        Assert.Equal(Entry([original]), Entry([same])); Assert.Equal(Entry([original]).GetHashCode(), Entry([same]).GetHashCode());
        VocabularySense[] changed = [original with { Meaning = "other" }, original with { MeaningLanguage = LearningLanguage.Chinese },
            original with { MeaningOrigin = VocabularyMeaningOrigin.User }, original with { Note = "note" }, original with { Tags = [] },
            original with { IsSavedWithoutContext = true }, original with { StandaloneMeanings = [new("draft", null)] },
            original with { AlternateMeanings = [new("other", LearningLanguage.Spanish)] }, original with { MergedFrom = [] },
            original with { UpdatedAt = Now.AddSeconds(1) }];
        Assert.All(changed, value => Assert.NotEqual(original, value));
        Assert.NotEqual(Entry([original, same]), Entry([same with { Note = "changed" }, original]));
        Assert.Equal("cafe\u0301", original.Tags[0]);
    }
}
