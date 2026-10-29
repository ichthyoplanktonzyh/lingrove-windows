using Cotabby.App.Learning;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

public sealed class LearningModelDestinationTextTests
{
    [Fact]
    public void SelectedEndpointIsDisclosedWithTheActualNormalizedDestination()
    {
        var profile = new LearningModelSnapshot { Backend = LearningModelBackend.Endpoint, BaseUrl = "https://owned.invalid/", EndpointModel = "instruction-model" };
        string text = LearningModelDestinationText.Explanation(profile);
        Assert.Contains("https://owned.invalid/v1", text); Assert.Contains("instruction-model", text); Assert.Contains("context", text);
        Assert.DoesNotContain("Local AI", text);
    }
    [Theory]
    [InlineData("https://user:owned-secret@owned.invalid/v1")]
    [InlineData("https://owned.invalid/v1?key=owned-secret")]
    [InlineData("https://owned.invalid/v1#owned-secret")]
    public void InvalidDestinationNeverPublishesCredentials(string url)
    {
        var profile = new LearningModelSnapshot { Backend = LearningModelBackend.Endpoint, BaseUrl = url, EndpointModel = "model" };
        Assert.Null(LearningModelDestinationText.Endpoint(profile));
        Assert.DoesNotContain("owned-secret", LearningModelDestinationText.Explanation(profile));
    }
    [Fact]
    public void LocalSelectionNeverDisclosesItsDormantEndpoint()
    {
        var profile = new LearningModelSnapshot { LocalModel = "local.gguf", BaseUrl = "https://dormant.invalid/v1", EndpointModel = "dormant-model" };
        string text = LearningModelDestinationText.Explanation(profile);
        Assert.Contains("local.gguf", text); Assert.DoesNotContain("dormant", text);
    }
}
