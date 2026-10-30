using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Tests.Insertion;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class WritingRangeSelectionAcknowledgementTests
{
    [Fact]
    public void DelayedSelectionKeepsOneSetterAndRequiresTheExactTarget()
    {
        var saved = InsertionTestFields.Create("sentence", 2);
        Assert.Equal(7, WritingRangeSelectionAcknowledgement.MaximumAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(45), WritingRangeSelectionAcknowledgement.PollInterval);
        Assert.Equal(WritingRangeSelectionDecision.Pending, WritingRangeSelectionAcknowledgement.Assess(saved, new(0, 8), true, null, null));
        Assert.Equal(WritingRangeSelectionDecision.Pending, WritingRangeSelectionAcknowledgement.Assess(saved, new(0, 8), true, "sentence", new(2, 0)));
        Assert.Equal(WritingRangeSelectionDecision.Confirmed, WritingRangeSelectionAcknowledgement.Assess(saved, new(0, 8), true, "sentence", new(0, 8)));
        Assert.Equal(WritingRangeSelectionDecision.Rejected, WritingRangeSelectionAcknowledgement.Assess(saved, new(0, 8), true, "sentence", new(1, 3)));
        Assert.Equal(WritingRangeSelectionDecision.Rejected, WritingRangeSelectionAcknowledgement.Assess(saved, new(0, 8), true, "changed!", new(0, 8)));
        Assert.Equal(WritingRangeSelectionDecision.Rejected, WritingRangeSelectionAcknowledgement.Assess(saved, new(0, 8), true, null, new(0, 8)));
        Assert.Equal(WritingRangeSelectionDecision.Rejected, WritingRangeSelectionAcknowledgement.Assess(saved, new(0, 8), false, "sentence", new(0, 8)));
    }
}
