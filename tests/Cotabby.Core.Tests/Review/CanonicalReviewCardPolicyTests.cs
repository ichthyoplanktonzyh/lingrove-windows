using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Core.Tests.Review;

public sealed class CanonicalReviewCardPolicyTests
{
    [Fact]
    public void CanonicalMaterialRetainsUnknownLanguageContextAndAuthoredMeaning()
    {
        var time = new F.SourceDate(814759445.1234567);
        var doc = F.VocabularyEntryImportPolicy.Plan([new VocabularyEntryInput(Expression: "bank", Language: null, Meaning: "core", Example: "the bank opens")], new(), time).Document;
        var entry = doc.CollectedEntries[0]; var sense = entry.Senses[0]; var usage = sense.Examples[0] with { Meaning = "context" };
        sense = sense with { Examples = [usage], MeaningOrigin = VocabularyMeaningOrigin.Captured }; var plan = CanonicalReviewCardPolicy.InitialPlan(sense);
        Assert.Null(entry.Language); Assert.Equal("context", CanonicalReviewCardPolicy.Content(entry, sense, plan).Answer);
        sense = sense with { MeaningOrigin = VocabularyMeaningOrigin.User };
        var content = CanonicalReviewCardPolicy.Content(entry, sense, plan);
        Assert.Equal("core", content.Answer); Assert.Equal("the bank opens", content.Sentence); Assert.Equal(time, usage.CreatedAt);
    }
    [Fact]
    public void FlexibleLayoutRetainsStableBlockAndRejectsMissingSource()
    {
        var time = new F.SourceDate(42); var doc = F.VocabularyEntryImportPolicy.Plan([new VocabularyEntryInput(Expression: "word", Meaning: "meaning")], new(), time).Document;
        var entry = doc.CollectedEntries[0]; var block = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("explanation")));
        var sense = entry.Senses[0] with { Content = [block] };
        var layout = new ReviewLayout([new ReviewCardPart(Guid.NewGuid(), ReviewContentSource.SenseText, block.Id)], [ReviewCardPart.Create(ReviewContentSource.Meaning)]);
        var content = CanonicalReviewCardPolicy.Content(entry, sense, new ReviewPlan(Guid.NewGuid()) { Layout = layout });
        Assert.Equal(block.Id, Assert.Single(content.SenseContent).Id);
        Assert.Throws<AnkiReviewException>(() => CanonicalReviewCardPolicy.Content(entry, sense with { Content = [] }, new ReviewPlan(Guid.NewGuid()) { Layout = layout }));
    }
}
