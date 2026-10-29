using Cotabby.Core.Suggestions.Streaming;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class SuggestionStreamingStateTests
{
    [Fact]
    public void ScheduledDrainCoalescesAcrossReplacementAndFinalization()
    {
        var state = new SuggestionStreamingState(); state.BeginGeneration();
        Assert.True(state.Enqueue(" old", 1)); Assert.False(state.Enqueue(" older", 1));
        state.RecordRendered(" old"); state.BeginGeneration();
        Assert.True(state.IsDrainScheduled); Assert.False(state.Enqueue(" new", 2));
        Assert.Equal(new SuggestionStreamingState.Pending(" new", 2), state.Drain());
        Assert.True(state.CanRender(" new")); state.RecordRendered(" new");
        Assert.False(state.CanRender(" ne")); Assert.True(state.CanRender(" newer"));
        Assert.True(state.Enqueue(" newer", 2)); state.FinishGeneration();
        Assert.Null(state.Drain()); Assert.False(state.Enqueue("late", 2));
        state.BeginGeneration(); Assert.True(state.Enqueue("fresh", 3));
    }
}
