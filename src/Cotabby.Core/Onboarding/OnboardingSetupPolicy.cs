namespace Cotabby.Core.Onboarding;

/// <summary>Configuration, not model warmth; source OnboardingSetupPolicy at 3b53402.</summary>
public enum OnboardingModelStatus { NeedsConfiguration, Downloading, Configured, Unavailable }
public static class OnboardingSetupPolicy
{
    public static OnboardingModelStatus ModelStatus(bool selected, bool available, bool downloading = false) =>
        downloading ? OnboardingModelStatus.Downloading : !selected ? OnboardingModelStatus.NeedsConfiguration : available ? OnboardingModelStatus.Configured : OnboardingModelStatus.Unavailable;
}
