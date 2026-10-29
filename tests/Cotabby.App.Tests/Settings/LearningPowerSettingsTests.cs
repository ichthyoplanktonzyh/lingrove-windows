using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Tests.Settings;

public sealed class LearningPowerSettingsTests
{
    [Fact]
    public void ReopenedDestinationMapsRemainEditableAndCanonical()
    {
        var empty = SettingsStore.Parse("{\"LearningTranslationEndpointContextByDestination\":null,\"LearningTranslationEndpointGenerationByDestination\":null}");
        empty.LearningTranslationEndpointContextByDestination["owned"] = 4096;
        empty.LearningTranslationEndpointGenerationByDestination["owned"] = new();
        var settings = new AppSettings { LearningBackend = LearningTranslationBackend.Endpoint, LearningEndpointModel = "café" };
        settings.LearningTranslationEndpointContextByDestination[settings.LearningTranslationEndpointIdentity] = 6000;
        settings.LearningTranslationEndpointGenerationByDestination[settings.LearningTranslationEndpointIdentity] = new() { Style = null!, Sampling = null! };
        var reopened = SettingsStore.Parse(SettingsStore.Serialize(settings)); reopened.LearningEndpointModel = "cafe\u0301";
        Assert.True(reopened.LearningTranslationEndpointContextByDestination.ContainsKey(reopened.LearningTranslationEndpointIdentity));
        Assert.Equal(6000, reopened.LearningTranslationActiveEndpointContext);
        Assert.NotNull(reopened.LearningTranslationActiveEndpointGeneration.Style);
        Assert.NotNull(reopened.LearningTranslationActiveEndpointGeneration.Sampling);
    }
    [Fact]
    public void ExplicitPowerEndpointDoesNotBorrowOrMutateManualEndpointEnablement()
    {
        var settings = new AppSettings { LearningEndpointEnabled = false, LearningTranslationPowerSwitching = true,
            LearningTranslationBatteryProfile = LearningPowerProfile.Local("battery.gguf"),
            LearningTranslationPluggedInProfile = LearningPowerProfile.Endpoint("explicit-ac-model") };
        Assert.True(settings.LearningTranslationActiveEndpointEnabled);
        Assert.False(settings.LearningEndpointEnabled);
        settings.LearningPowerPluggedIn = false; Assert.False(settings.LearningTranslationActiveEndpointEnabled);
        settings.LearningTranslationPowerSwitching = false; Assert.False(settings.LearningTranslationActiveEndpointEnabled);
        settings.LearningBackend = LearningTranslationBackend.Endpoint; Assert.False(settings.LearningTranslationActiveEndpointEnabled);
        settings.LearningEndpointEnabled = true; Assert.True(settings.LearningTranslationActiveEndpointEnabled);
    }
    [Fact]
    public void RetainedProfilesProtectFilesAndRegisterRoleAfterRestart()
    {
        var settings = new AppSettings { LearningTranslationBatteryProfile = LearningPowerProfile.Local("battery.gguf"),
            LearningTranslationPluggedInProfile = LearningPowerProfile.Local("ac.gguf"),
            LearningExplanationBatteryProfile = LearningPowerProfile.Local("explain-battery.gguf"),
            LearningExplanationPluggedInProfile = LearningPowerProfile.Local("explain-ac.gguf") };
        var reopened = SettingsStore.Parse(SettingsStore.Serialize(settings));
        var protectedFiles = reopened.ProtectedModelFilenames();
        Assert.Contains("battery.gguf", protectedFiles); Assert.Contains("ac.gguf", protectedFiles);
        Assert.Contains("explain-battery.gguf", protectedFiles); Assert.Contains("explain-ac.gguf", protectedFiles);
        Assert.True(reopened.IsLocalModelCandidate("battery.gguf", Cotabby.Core.Models.LocalModelTask.Translation));
        Assert.True(reopened.IsLocalModelCandidate("explain-ac.gguf", Cotabby.Core.Models.LocalModelTask.Explanation));
    }

    [Fact]
    public void ProfilesSeedOncePersistIndependentlyAndRestoreManual()
    {
        var settings = new AppSettings { LearningLocalModel = "manual.gguf", LearningLocalTemplate = LearningChatTemplate.HyMt,
            LearningExplanationLocalModel = "explainer.gguf" };
        settings.SetLearningPowerSwitching(LearningModelRole.Translation, true);
        Assert.Equal("manual.gguf", settings.LearningTranslationBatteryProfile!.LocalModel);
        Assert.Equal(LearningChatTemplate.HyMt, settings.LearningTranslationPluggedInProfile!.Template);
        settings.LearningTranslationBatteryProfile = LearningPowerProfile.Local("battery.gguf");
        settings.LearningTranslationPluggedInProfile = LearningPowerProfile.Endpoint("ac");
        Assert.Equal("ac", settings.LearningTranslationActiveSelection.EndpointModel);
        Assert.Equal("explainer.gguf", settings.LearningExplanationSnapshot().LocalModel);
        settings.LearningPowerPluggedIn = false;
        Assert.Equal("battery.gguf", settings.LearningTranslationActiveSelection.LocalModel);
        var reopened = SettingsStore.Parse(SettingsStore.Serialize(settings));
        Assert.Equal("ac", reopened.LearningTranslationActiveSelection.EndpointModel);
        reopened.LearningPowerPluggedIn = false;
        Assert.Equal("battery.gguf", reopened.LearningTranslationActiveSelection.LocalModel);
        reopened.SetLearningPowerSwitching(LearningModelRole.Translation, false);
        Assert.Equal("manual.gguf", reopened.LearningTranslationActiveSelection.LocalModel);
        reopened.SetLearningPowerSwitching(LearningModelRole.Translation, true);
        Assert.Equal("battery.gguf", reopened.LearningTranslationActiveSelection.LocalModel);
    }

    [Fact]
    public void ExplanationSnapshotsKeepRoleAddressCredentialAndDestinationBudget()
    {
        var settings = new AppSettings { LearningExplanationLocalModel = "manual.gguf", LearningExplanationEndpointBaseUrl = "https://example.invalid/v1",
            LearningExplanationCredentialRevision = 7 };
        settings.SetLearningPowerSwitching(LearningModelRole.Explanation, true);
        settings.LearningExplanationPluggedInProfile = LearningPowerProfile.Endpoint("ac-model");
        settings.LearningExplanationBatteryProfile = LearningPowerProfile.Local("battery.gguf", LearningChatTemplate.Qwen3);
        settings.LearningExplanationEndpointContextTokensByDestination[settings.LearningExplanationEndpointIdentity] = 16384;
        var ac = settings.LearningExplanationSnapshot();
        Assert.Equal("ac-model", ac.EndpointModel); Assert.Equal(7, ac.CredentialRevision); Assert.Equal(16384, ac.ContextWindowTokens);
        settings.LearningPowerPluggedIn = false;
        var battery = settings.LearningExplanationSnapshot();
        Assert.Equal("battery.gguf", battery.LocalModel); Assert.Equal(LearningChatTemplate.Qwen3, battery.Template);
        Assert.Empty(battery.BaseUrl); Assert.Equal(0, battery.CredentialRevision);
        settings.LearningExplanationPluggedInProfile = LearningPowerProfile.Endpoint("changed");
        Assert.Equal(battery, settings.LearningExplanationSnapshot());
        settings.LearningPowerPluggedIn = true;
        Assert.Equal(8192, settings.LearningExplanationSnapshot().ContextWindowTokens);
        Assert.NotEqual(ac, settings.LearningExplanationSnapshot());
    }

    [Fact]
    public void EndpointBudgetsFollowSelectedDestinationAfterRoundTrip()
    {
        var settings = new AppSettings { LearningBackend = LearningTranslationBackend.Endpoint, LearningEndpointModel = "manual", LearningContextTokens = 4096 };
        settings.SetLearningPowerSwitching(LearningModelRole.Translation, true);
        settings.LearningTranslationBatteryProfile = LearningPowerProfile.Endpoint("battery");
        settings.LearningPowerPluggedIn = false;
        Assert.Equal(8192, settings.LearningTranslationActiveEndpointContext);
        settings.LearningTranslationEndpointContextByDestination[settings.LearningTranslationEndpointIdentity] = 16384;
        var reopened = SettingsStore.Parse(SettingsStore.Serialize(settings));
        reopened.LearningPowerPluggedIn = false;
        Assert.Equal(16384, reopened.LearningTranslationActiveEndpointContext);
        reopened.LearningPowerPluggedIn = true;
        Assert.Equal(4096, reopened.LearningTranslationActiveEndpointContext);
    }
}
