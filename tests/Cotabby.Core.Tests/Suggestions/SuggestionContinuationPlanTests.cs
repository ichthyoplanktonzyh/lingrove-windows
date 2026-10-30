using Cotabby.Core.Suggestions.Streaming;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class SuggestionContinuationPlanTests
{
    [Fact]
    public void VirtualBoundaryNeverChangesTheExpectedCommittedText()
    {
        var field = Fields.Text("I bu"); var plan = SuggestionContinuationPlan.Completing("ild", field)!;
        Assert.Equal("I build", plan.Target.Text); Assert.Equal("I build ", plan.Request.Text);
        Assert.True(plan.MatchesSource(field)); Assert.True(plan.MatchesTarget(plan.Target));
        Assert.True(plan.MatchesTargetWithJoiningSeparator(plan.Request));
        Assert.Equal(" a house", plan.Continuation("a house"));
        Assert.Equal("\n  a house", plan.Continuation("\n  a house"));
        Assert.False(plan.MatchesTarget(plan.Target with { FocusChangeSequence = 2 }));
        Assert.False(plan.MatchesTarget(plan.Target with { Text = "I built" }));
    }
    [Fact]
    public void ReplacementRejectsUnsafeOrSplitBoundaries()
    {
        var field = Fields.Text("teh ");
        Assert.NotNull(SuggestionContinuationPlan.Correcting(4, "the ", field));
        Assert.Null(SuggestionContinuationPlan.Correcting(8, "the", field));
        var emoji = Fields.Text("🐈");
        Assert.Null(SuggestionContinuationPlan.Correcting(1, "cat", emoji));
        Assert.Null(SuggestionContinuationPlan.Completing("x", field with { IsSecure = true }));
    }
}
