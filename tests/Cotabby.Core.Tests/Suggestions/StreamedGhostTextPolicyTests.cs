using Cotabby.Core.Suggestions.Streaming;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class StreamedGhostTextPolicyTests
{
    [Theory]
    [InlineData("world ag", "world ")]
    [InlineData("world again tom", "world again ")]
    [InlineData("world again\n  tom", "world again\n  ")]
    [InlineData("world ag\t", "world ag\t")]
    [InlineData("world 'again'", "world 'again'")]
    [InlineData("world ‘again’", "world ‘again’")]
    [InlineData("world don't", "world ")]
    [InlineData("world item_2", "world ")]
    public void IncompleteHiddenTokensAreNotAccepted(string text, string expected) =>
        Assert.Equal(expected, StreamedGhostTextPolicy.CompletedBufferedPrediction(text, 5));
    [Fact]
    public void PreservesAlreadyVisibleGraphemesAndRejectsNonMonotonicPartials()
    {
        Assert.Equal("world a", StreamedGhostTextPolicy.CompletedBufferedPrediction("world ag", 7));
        Assert.Equal("你好", StreamedGhostTextPolicy.CompletedBufferedPrediction("你好世界", 2));
        Assert.Equal("🐈 café ", StreamedGhostTextPolicy.CompletedBufferedPrediction("🐈 café cafe\u0301", 6));
        Assert.True(StreamedGhostTextPolicy.IsRenderableExtension(" world", " wor"));
        Assert.False(StreamedGhostTextPolicy.IsRenderableExtension(" wor", " world"));
        Assert.False(StreamedGhostTextPolicy.IsRenderableExtension(" cafe\u0301", " cafe"));
        Assert.True(StreamedGhostTextPolicy.IsRenderableExtension("café!", "cafe\u0301"));
    }
}
