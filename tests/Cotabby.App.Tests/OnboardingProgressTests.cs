using Cotabby.App.Settings;

namespace Cotabby.App.Tests;

public sealed class OnboardingProgressTests
{
    [Fact]
    public void GeneralResetPreservesFirstRunResumeState()
    {
        var settings = new AppSettings { ShowFirstRunWelcome = true };
        settings.OnboardingProgress = (5, true);
        var reset = settings.ResetGeneralPreferences(42);
        Assert.True(reset.ShowFirstRunWelcome);
        Assert.Equal((5, true), reset.OnboardingProgress);
        Assert.Equal(42, reset.WordsAccepted);
    }

    [Fact]
    public void ProgressRoundTripsAndInvalidInputDoesNotDiscardOtherSettings()
    {
        var settings = new AppSettings { UserName = "fixture" };
        settings.OnboardingProgress = (4, true);
        Assert.False(settings.RecordOnboardingStep(2, true));
        Assert.True(settings.RecordOnboardingStep(2, false));
        Assert.Equal((4, false), settings.OnboardingProgress);
        Assert.True(settings.RecordOnboardingStep(4, true));
        var reopened = SettingsStore.Parse(SettingsStore.Serialize(settings));
        Assert.Equal((4, true), reopened.OnboardingProgress);
        Assert.Equal("fixture", reopened.UserName);
        var invalid = SettingsStore.Parse("{\"UserName\":\"fixture\",\"lingroveOnboardingProgressWindows1\":{\"step\":\"bad\",\"manual\":true}}");
        Assert.Equal((0, false), invalid.OnboardingProgress);
        Assert.Equal("fixture", invalid.UserName);
    }
}
