using Cotabby.Core.Suggestions.Streaming;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class TypingCadenceTests
{
    [Fact]
    public void HoldsOnlyRecentLetterRhythmAndNeverDelaysCompletedWords()
    {
        var cadence = new TypingCadence(); cadence.Record(1, "a", 1.0);
        Assert.Equal(.08, cadence.RemainingDelay(1, "a", 1.0), 6);
        cadence.Record(1, "b", 1.1); cadence.Record(1, "c", 1.2);
        Assert.Equal(.12, cadence.RemainingDelay(1, "abc", 1.2), 6);
        Assert.Equal(0, cadence.RemainingDelay(1, "abc ", 1.2));
        Assert.Equal(0, cadence.RemainingDelay(2, "abc", 1.2));
        cadence.Record(1, " ", 1.3); Assert.Equal(0, cadence.RemainingDelay(1, "abc", 1.3));
    }
}
