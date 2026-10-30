using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class WritingPreviousSentencePolicyTests
{
    [Fact]
    public void OriginalTextAndOnlyPunctuationGapKeepThePreviousTranslation()
    {
        var previous = new WritingPreviousSentence("first", new(0, 5), "premier");
        Assert.Same(previous, WritingPreviousSentencePolicy.Select(previous, null, "first。 \nsecond", new(8, 6)));
        Assert.Null(WritingPreviousSentencePolicy.Select(previous, null, "other. second", new(7, 6)));
        Assert.Null(WritingPreviousSentencePolicy.Select(previous, null, "first intervening second", new(18, 6)));
        Assert.Null(WritingPreviousSentencePolicy.Select(previous, null, "first", new(0, 5)));
    }
    [Fact]
    public void AnIncompleteCurrentSentenceCanRetainEarlierVerifiedEvidence()
    {
        var earlier = new WritingPreviousSentence("first", new(0, 5), "premier");
        var current = new WritingPreviousSentence("second", new(7, 6), "deuxième");
        Assert.Same(earlier, WritingPreviousSentencePolicy.Select(current, earlier, "first. second", new(7, 6)));
        Assert.Null(WritingPreviousSentencePolicy.Select(current with { Range = new(int.MaxValue, 6) }, null, "short", new(1, 2)));
    }
}
