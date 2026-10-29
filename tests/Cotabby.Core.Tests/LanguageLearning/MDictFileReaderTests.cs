using Cotabby.Core.LanguageLearning.MDict;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class MDictFileReaderTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SourceSyntheticV1AndV2Lookup(bool v2)
    {
        using var stream = new MemoryStream(v2 ? MDictFixture3b.v2 : MDictFixture3b.v1, false);
        var file = new MDictFileReader(stream, value => value.ToLowerInvariant());
        Assert.Equal("Lingrove Test", file.Title); Assert.Equal(3, file.EntryCount);
        Assert.Contains("Hello", Assert.Single(file.Definitions("hello")));
        Assert.Equal("@@@LINK=hello", Assert.Single(file.Definitions("alias")));
        Assert.Empty(file.Definitions("absent"));
    }
    [Fact]
    public void SourceSyntheticMddPreservesBinaryResource()
    {
        using var stream = new MemoryStream(MDictFixture3b.mdd, false);
        var file = new MDictFileReader(stream, value => value.ToLowerInvariant());
        var bytes = Assert.Single(file.Records("\\test.png")); Assert.Equal(new byte[] { 137, 80, 78, 71 }, bytes[..4]);
    }
    [Fact]
    public void SourceAdversarialBlocksAndTruncatedInputAreRejected()
    {
        foreach (var bytes in new[] { MDictFixture3b.oversize, MDictFixture3b.reversed, MDictFixture3b.v2[..20] })
        {
            using var stream = new MemoryStream(bytes, false);
            Assert.ThrowsAny<Exception>(() => new MDictFileReader(stream, value => value.ToLowerInvariant()));
        }
    }
    [Fact]
    public void SourceOverlappingKeyBlocksExceedLookupBudget()
    {
        using var stream = new MemoryStream(MDictFixture3b.overlap, false);
        var file = new MDictFileReader(stream, value => value.ToLowerInvariant());
        Assert.Throws<InvalidDataException>(() => file.Definitions("hello"));
    }
}
