using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class DictionaryLinkPolicyTests
{
    [Theory]
    [InlineData("https://example.com/entry#sense", true)]
    [InlineData("HTTP://example.com/", true)]
    [InlineData("file:///tmp/private", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("entry://word", false)]
    [InlineData("https://user:password@example.com/", false)]
    [InlineData("https://@example.com/", false)]
    [InlineData("https:///entry", false)]
    public void OnlyExplicitHttpLinksWithoutCredentialsCanLeaveTheCard(string value, bool allowed) =>
        Assert.Equal(allowed, DictionaryLinkPolicy.ExternalUrl(value) is not null);

    [Fact]
    public void UrlBudgetUsesUtf8Bytes()
    {
        const string prefix = "https://example.com/";
        Assert.NotNull(DictionaryLinkPolicy.ExternalUrl(prefix + new string('x', 4096 - prefix.Length)));
        Assert.Null(DictionaryLinkPolicy.ExternalUrl(prefix + new string('é', 2040)));
    }
}
