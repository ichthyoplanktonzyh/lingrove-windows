using Cotabby.Core.Models;

namespace Cotabby.Core.Tests.Models;

public sealed class RecommendedSetupPolicyTests
{
    [Theory]
    [InlineData(4, "tabby-2-nano")]
    [InlineData(8, "tabby-2-nano")]
    [InlineData(16, "tabby-2-base")]
    [InlineData(32, "tabby-2-base")]
    public void FrozenRecommendationsKeepTaskCompatibleCatalogChoices(int gib, string completion)
    {
        var plan = RecommendedSetupPolicy.Create((ulong)gib * 1_073_741_824);
        Assert.Equal(completion, plan.Completion.Id);
        Assert.True(LocalModelTaskPolicy.Supports(plan.Completion.FileName, LocalModelTask.Completion));
        Assert.True(LocalModelTaskPolicy.Supports(plan.Translation.Filename, LocalModelTask.Translation));
        Assert.True(LocalModelTaskPolicy.Supports(plan.Explanation.Filename, LocalModelTask.Explanation));
    }
    [Fact]
    public void ExplicitProfilesAndLaterManualChoicesAreNeverOverwritten()
    {
        Assert.True(RecommendedSetupPolicy.NeedsLearningSelection(true, " ", false));
        Assert.False(RecommendedSetupPolicy.NeedsLearningSelection(false, "", false));
        Assert.False(RecommendedSetupPolicy.NeedsLearningSelection(true, "existing.gguf", false));
        Assert.False(RecommendedSetupPolicy.NeedsLearningSelection(true, "", true));
        Assert.True(RecommendedSetupPolicy.CanFinishCompletion(null, null, "target", true, false));
        Assert.True(RecommendedSetupPolicy.CanFinishCompletion(null, "target", "target", true, false));
        Assert.False(RecommendedSetupPolicy.CanFinishCompletion(null, "manual", "target", true, false));
        Assert.False(RecommendedSetupPolicy.CanFinishCompletion(null, null, "target", false, false));
        Assert.False(RecommendedSetupPolicy.CanFinishCompletion(null, null, "target", true, true));
    }
}
