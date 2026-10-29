using Cotabby.Core.Suggestions.Streaming;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class TypingPredictionCandidateTests
{
    [Fact]
    public void ReusesOnlyAfterExactHostPublicationAndTracksBothDeadlines()
    {
        var origin = Fields.Text("hello"); var value = new TypingPredictionCandidate(origin);
        Assert.True(value.Receive(" world", false));
        Assert.True(value.Append(" ", origin, 1));
        Assert.Null(value.Rebased(" world", origin));
        Assert.Equal(1.4, value.Expiration(origin));
        var published = Fields.Text("hello ");
        Assert.True(value.IsPublished(published)); Assert.Null(value.Expiration(published));
        Assert.Equal("world", value.Rebased(" world", published));
        Assert.True(value.Receive(" world!", true)); Assert.False(value.Receive(" world!!", false));
    }
    [Fact]
    public void BehindModelHasBoundedGraceAndDivergenceFailsClosed()
    {
        var origin = Fields.Text("hello"); var value = new TypingPredictionCandidate(origin);
        Assert.True(value.Receive(" w", false)); Assert.True(value.Append(" w", origin, 1));
        Assert.True(value.Append("o", origin, 1.01)); Assert.Equal(1.16, value.Expiration(origin));
        Assert.False(value.Append("r", origin, 1.16)); Assert.False(value.Receive(" wrong", true));
    }
    [Fact]
    public void RejectsOtherFieldCompositionAndMalformedCharacters()
    {
        var origin = Fields.Text("hello"); var value = new TypingPredictionCandidate(origin);
        Assert.False(value.Append("a", origin with { FocusChangeSequence = 2 }, 1));
        Assert.False(value.Append("a", origin with { HasSelection = true }, 1));
        Assert.False(value.Append("\ud800", origin, 1));
        Assert.False(value.Append("\n", origin, 1));
        var buffered = new TypingPredictionCandidate(origin, requiresInitialPrediction: true);
        Assert.False(buffered.Append("a", origin, 1));
    }
}
