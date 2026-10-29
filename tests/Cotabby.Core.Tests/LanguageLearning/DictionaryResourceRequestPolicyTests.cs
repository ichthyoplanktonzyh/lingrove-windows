using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class DictionaryResourceRequestPolicyTests
{
    [Fact]
    public void OnlyCurrentDictionaryRelativeResourcesAreAccepted()
    {
        var id = Guid.NewGuid(); string prefix = DictionaryResourceRequestPolicy.ArticleUri(id).Replace("__article__", "");
        Assert.Equal("images/a b.png", DictionaryResourceRequestPolicy.ResourcePath(prefix + "images/a%20b.png", id));
        Assert.Null(DictionaryResourceRequestPolicy.ResourcePath(prefix + "%2e%2e/secret.css", id));
        Assert.Null(DictionaryResourceRequestPolicy.ResourcePath(prefix + "image.png?x=1", id));
        Assert.Null(DictionaryResourceRequestPolicy.ResourcePath("https://example.test/a.png", id));
        Assert.Null(DictionaryResourceRequestPolicy.ResourcePath(DictionaryResourceRequestPolicy.ArticleUri(Guid.NewGuid()), id));
    }
}
