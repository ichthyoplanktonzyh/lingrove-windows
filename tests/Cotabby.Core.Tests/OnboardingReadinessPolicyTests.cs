using Cotabby.Core.Onboarding;
namespace Cotabby.Core.Tests;
public sealed class OnboardingReadinessPolicyTests
{
    [Theory]
    [InlineData(false, false, false, OnboardingModelStatus.NeedsConfiguration)]
    [InlineData(true, false, false, OnboardingModelStatus.Unavailable)]
    [InlineData(true, true, false, OnboardingModelStatus.Configured)]
    [InlineData(false, false, true, OnboardingModelStatus.Downloading)]
    [InlineData(true, true, true, OnboardingModelStatus.Downloading)]
    public void StatusIsConfigurationAndDownloadWins(bool selected, bool available, bool downloading, OnboardingModelStatus expected) =>
        Assert.Equal(expected, OnboardingSetupPolicy.ModelStatus(selected, available, downloading));
}
