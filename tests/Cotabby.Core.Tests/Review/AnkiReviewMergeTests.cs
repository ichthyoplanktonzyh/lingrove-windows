using Cotabby.Core.Review;

namespace Cotabby.Core.Tests.Review;

public sealed class AnkiReviewMergeTests
{
    [Fact]
    public void MergeRetainsTargetPlanAndOrderedSourceHistoryAndRequiresChoiceForMultipleNotes()
    {
        var old = ReviewPlan.Create(); var earlier = ReviewPlan.Create(); var sourcePlan = ReviewPlan.Create(); var sourceEarlier = ReviewPlan.Create();
        var a = Binding(1, Guid.NewGuid()); var b = Binding(2, Guid.NewGuid());
        var target = new VocabularyReview(old, [a], a.Id, [earlier]);
        var source = new VocabularyReview(sourcePlan, [b], b.Id, [sourceEarlier]);
        var merged = AnkiReviewPolicy.Merged(target, source)!;
        Assert.Equal(old, merged.Plan); Assert.Equal(new[] { earlier, sourcePlan, sourceEarlier }, merged.PreservedPlans);
        Assert.Equal(new[] { a, b }, merged.Bindings); Assert.Null(merged.ActiveBindingId); Assert.True(merged.NeedsBindingChoice);
        Assert.Single(target.Bindings); Assert.Single(source.PreservedPlans);
    }

    [Theory]
    [InlineData("sameNote", 1)]
    [InlineData("sameOrigin", 1)]
    [InlineData("otherProfile", 2)]
    [InlineData("otherEndpoint", 2)]
    [InlineData("nullNoteSameOrigin", 1)]
    [InlineData("nullNoteOtherOrigin", 2)]
    public void DeduplicationUsesEndpointProfileAndNoteOrOriginalSense(string mode, int count)
    {
        Guid origin = Guid.NewGuid(); var a = Binding(mode.StartsWith("null", StringComparison.Ordinal) ? null : 1, origin);
        var b = Binding(mode switch { "sameOrigin" => 2, "nullNoteSameOrigin" or "nullNoteOtherOrigin" => null, _ => 1 },
            mode is "sameOrigin" or "nullNoteSameOrigin" ? origin : Guid.NewGuid(),
            mode == "otherProfile" ? "other" : "profile", mode == "otherEndpoint" ? "other" : "loopback");
        var merged = AnkiReviewPolicy.Merged(new(ReviewPlan.Create(), [a]), new(ReviewPlan.Create(), [b]))!;
        Assert.Equal(count, merged.Bindings.Count); Assert.Equal(a, merged.Bindings[0]);
        Assert.Equal(count == 1 ? a.Id : (Guid?)null, merged.ActiveBindingId);
    }

    [Fact]
    public void AbsentReviewsAndDuplicateSourceBindingsDoNotLoseRetainedPlanValues()
    {
        var review = new VocabularyReview(ReviewPlan.Create());
        Assert.Null(AnkiReviewPolicy.Merged(null, null)); Assert.Same(review, AnkiReviewPolicy.Merged(review, null));
        Assert.Same(review, AnkiReviewPolicy.Merged(null, review));
        var binding = Binding(1, Guid.NewGuid());
        var source = new VocabularyReview(ReviewPlan.Create(), [binding, binding]);
        var merged = AnkiReviewPolicy.Merged(review, source)!;
        Assert.Single(merged.Bindings); Assert.Equal(binding.Id, merged.ActiveBindingId); Assert.Equal(source.Plan, Assert.Single(merged.PreservedPlans));
    }

    private static AnkiNoteBinding Binding(long? noteId, Guid origin, string profile = "profile", string endpoint = "loopback") =>
        new(Guid.NewGuid(), origin, endpoint, profile, noteId, "model", "deck", "Identity",
            new Dictionary<ReviewField, string>(), new Dictionary<string, string>());
}
