using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Tests.Settings;

public sealed class LearningExplanationSettingsTests
{
    [Fact]
    public void EndpointKeepsItsOwnDestinationBudgetSamplerAndRuntimeKeyRevision()
    {
        var settings = new AppSettings { LearningExplanationBackend = LearningModelBackend.Endpoint,
            LearningExplanationEndpointBaseUrl = "https://FIRST.invalid:443/", LearningExplanationEndpointModel = " tutor ",
            LearningEndpointModel = "translation", LearningExplanationLocalModel = "local.gguf", LearningExplanationCredentialRevision = 3 };
        string first = settings.LearningExplanationEndpointIdentity;
        settings.LearningExplanationEndpointContextTokensByDestination[first] = 65536;
        settings.LearningExplanationEndpointGenerationProfiles[first] = new() { Sampling = new(Temperature: .7, TopK: 17, SeedMode: LearningModelSeedMode.Fixed), MaximumOutputTokens = 768 };
        var frozen = settings.LearningExplanationSnapshot();
        Assert.Equal(LearningModelBackend.Endpoint, frozen.Backend); Assert.Empty(frozen.LocalModel);
        Assert.Equal(65536, frozen.ContextWindowTokens); Assert.Equal(768, frozen.MaximumOutputTokens);
        Assert.Equal(.7, frozen.SamplingOverrides.Temperature); Assert.Null(frozen.SamplingOverrides.TopK);
        Assert.Equal(LearningModelSeedMode.Recommended, frozen.SamplingOverrides.SeedMode); Assert.Equal(3, frozen.CredentialRevision);
        settings.LearningExplanationEndpointBaseUrl = "https://first.invalid/v1/";
        Assert.Equal(first, settings.LearningExplanationEndpointIdentity);
        Assert.Equal(65536, settings.LearningExplanationSnapshot().ContextWindowTokens);
        settings.LearningExplanationEndpointBaseUrl = "https://second.invalid/v1";
        Assert.Equal(8192, settings.LearningExplanationSnapshot().ContextWindowTokens);
        Assert.Null(settings.LearningExplanationSnapshot().SamplingOverrides.Temperature);
        settings.LearningExplanationEndpointBaseUrl = "https://first.invalid/v1";
        var reopened = SettingsStore.Parse(SettingsStore.Serialize(settings));
        Assert.Equal(65536, reopened.LearningExplanationSnapshot().ContextWindowTokens);
        Assert.Equal(.7, reopened.LearningExplanationSnapshot().SamplingOverrides.Temperature);
        Assert.Equal(0, reopened.LearningExplanationSnapshot().CredentialRevision);
        settings.LearningExplanationEndpointContextTokensByDestination[first] = 1024;
        Assert.Equal(65536, frozen.ContextWindowTokens);
    }

    [Fact]
    public void DormantProviderFieldsAndUnknownBackendCannotRedirectAnActiveTask()
    {
        var settings = new AppSettings { LearningExplanationLocalModel = "local.gguf" };
        var local = settings.LearningExplanationSnapshot();
        settings.LearningExplanationEndpointBaseUrl = "https://other.invalid/v1";
        settings.LearningExplanationEndpointModel = "other"; settings.LearningExplanationCredentialRevision = 5;
        Assert.Equal(local, settings.LearningExplanationSnapshot());
        settings.LearningExplanationBackend = LearningModelBackend.Endpoint;
        var endpoint = settings.LearningExplanationSnapshot();
        settings.LearningExplanationLocalModel = "different.gguf";
        Assert.Equal(endpoint, settings.LearningExplanationSnapshot());
        var unknown = SettingsStore.Parse("{\"learning.explanation.backend\":\"future\",\"LearningExplanationEndpointModel\":\"remote\"}");
        Assert.Equal(LearningModelBackend.Local, unknown.LearningExplanationSnapshot().Backend);
        Assert.Contains("future", SettingsStore.Serialize(unknown));
    }

    [Theory]
    [InlineData("zh-Hans", LearningLanguage.Chinese)]
    [InlineData("zh-Hant", LearningLanguage.TraditionalChinese)]
    [InlineData("fr", LearningLanguage.French)]
    [InlineData("ar", LearningLanguage.Arabic)]
    [InlineData("pt-BR", LearningLanguage.English)]
    [InlineData("id", LearningLanguage.English)]
    [InlineData("tr", LearningLanguage.English)]
    public void AppLanguageUsesActualLaunchLocalizationUntilRestart(string locale, LearningLanguage expected)
    {
        var settings = new AppSettings { InterfaceLocaleAtLaunch = locale, InterfaceLanguage = locale };
        Assert.Equal(expected, settings.ResolvedLearningExplanationLanguage);
        settings.InterfaceLanguage = "ja"; // Preference is pending; the displayed interface has not restarted.
        Assert.Equal(expected, settings.ResolvedLearningExplanationLanguage);
        Assert.Equal(expected, settings.ResetGeneralPreferences(0).ResolvedLearningExplanationLanguage);
        string json = SettingsStore.Serialize(settings);
        Assert.DoesNotContain("InterfaceLocaleAtLaunch", json);
        Assert.Null(settings.LearningExplanationLanguage);
        settings.LearningExplanationLanguage = LearningLanguage.German;
        Assert.Equal(LearningLanguage.German, settings.ResolvedLearningExplanationLanguage);
    }

    [Fact]
    public void OldPreferencesNeverInheritTranslationOrAutocompleteDestinations()
    {
        var settings = SettingsStore.Parse("""{"ActiveModelId":"completion","LearningEndpointEnabled":true,"LearningEndpointModel":"remote","LearningLocalModel":"translator.gguf","LearningTargetLanguage":"French"}""");
        var snapshot = settings.LearningExplanationSnapshot();
        Assert.Equal(LearningModelBackend.Local, snapshot.Backend); Assert.Empty(snapshot.LocalModel);
        Assert.Empty(snapshot.BaseUrl); Assert.Empty(snapshot.EndpointModel); Assert.Equal(0, snapshot.CredentialRevision);
        Assert.Equal(8192, snapshot.ContextWindowTokens); Assert.Null(snapshot.MaximumOutputTokens);
        Assert.Equal(LearningLanguage.English, settings.ResolvedLearningExplanationLanguage);
    }

    [Fact]
    public void PerModelProfilesRoundtripWithoutChangingTranslationPreferences()
    {
        var settings = new AppSettings
        {
            LearningLocalModel = "translator.gguf", LearningLocalTemplate = LearningChatTemplate.HyMt,
            LearningExplanationLocalModel = "tutor.gguf", LearningExplanationLocalTemplate = LearningChatTemplate.Qwen3,
            LearningExplanationLanguage = LearningLanguage.Chinese,
            LearningExplanationLocalContextTokensByModel = new() { ["tutor.gguf"] = 16384, ["other.gguf"] = 4096 },
            LearningExplanationLocalGenerationProfiles = new()
            {
                ["tutor.gguf"] = new() { MaximumOutputTokens = 768, Sampling = new(Temperature: 0.4, SeedMode: LearningModelSeedMode.Fixed, FixedSeed: 9) },
                ["other.gguf"] = new() { MaximumOutputTokens = 2048 },
            },
        };
        var restored = SettingsStore.Parse(SettingsStore.Serialize(settings));
        Assert.Equal(settings.LearningExplanationSnapshot(), restored.LearningExplanationSnapshot());
        Assert.Equal(16384, restored.EffectiveLearningExplanationContextTokens("TUTOR.GGUF"));
        Assert.Equal(4096, restored.EffectiveLearningExplanationContextTokens("OTHER.GGUF"));
        Assert.Equal(768, restored.EffectiveLearningExplanationGenerationProfile("TUTOR.GGUF").MaximumOutputTokens);
        Assert.Equal(2048, restored.EffectiveLearningExplanationGenerationProfile("OTHER.GGUF").MaximumOutputTokens);
        Assert.Equal(9u, restored.LearningExplanationSnapshot().SamplingOverrides.FixedSeed);
        Assert.Equal(LearningLanguage.Chinese, restored.ResolvedLearningExplanationLanguage);
        Assert.Equal("translator.gguf", restored.LearningLocalModel); Assert.Equal(LearningChatTemplate.HyMt, restored.LearningLocalTemplate);
    }

    [Theory]
    [InlineData(-1, 1024, 256)]
    [InlineData(999999, ModelContextBudget.MaximumLocalTokens, 4096)]
    public void EffectiveSnapshotBoundsPersistedNumbersAndIgnoresTranslationStyle(int input, int context, int output)
    {
        var settings = new AppSettings
        {
            LearningExplanationLocalModel = "tutor.gguf",
            LearningExplanationLocalContextTokensByModel = new() { ["tutor.gguf"] = input },
            LearningExplanationLocalGenerationProfiles = new() { ["tutor.gguf"] = new()
            {
                MaximumOutputTokens = input, Sampling = new(Temperature: 100, TopK: -1),
                Style = new(LearningTranslationRegister.Custom, "not an explanation instruction"),
            } },
        };
        var snapshot = settings.LearningExplanationSnapshot();
        Assert.Equal(context, snapshot.ContextWindowTokens); Assert.Equal(output, snapshot.MaximumOutputTokens);
        Assert.Equal(2, snapshot.SamplingOverrides.Temperature); Assert.Equal(1, snapshot.SamplingOverrides.TopK);
        Assert.Equal(new LearningTranslationStyle(), snapshot.TranslationStyle);
    }

    [Fact]
    public void CorruptOptionalMapsAreRecoveredAndAnExistingSnapshotIsDetached()
    {
        var restored = SettingsStore.Parse("""{"LearningExplanationLocalModel":null,"LearningExplanationLocalContextTokensByModel":null,"LearningExplanationLocalGenerationProfiles":null}""");
        Assert.Empty(restored.LearningExplanationLocalModel); Assert.Equal(8192, restored.LearningExplanationSnapshot().ContextWindowTokens);
        var settings = new AppSettings { LearningExplanationLocalModel = "tutor.gguf" };
        var captured = settings.LearningExplanationSnapshot();
        settings.LearningExplanationLocalModel = "second.gguf";
        settings.LearningExplanationLocalContextTokensByModel["tutor.gguf"] = 16384;
        settings.LearningExplanationLocalGenerationProfiles["tutor.gguf"] = new() { MaximumOutputTokens = 4096 };
        Assert.Equal("tutor.gguf", captured.LocalModel); Assert.Equal(8192, captured.ContextWindowTokens); Assert.Null(captured.MaximumOutputTokens);
    }

    [Fact]
    public void ExplanationNeverUsesTranslationSpecialistContextRecommendations()
    {
        var settings = new AppSettings { LearningExplanationLocalModel = "translategemma-4b-it-Q4_K_M.gguf" };
        Assert.Equal(8192, settings.LearningExplanationSnapshot().ContextWindowTokens);
        settings.LearningExplanationLocalTemplate = (LearningChatTemplate)999;
        settings.LearningExplanationLanguage = (LearningLanguage)999;
        Assert.Equal(LearningChatTemplate.Automatic, settings.LearningExplanationSnapshot().Template);
        Assert.Equal(LearningLanguage.English, settings.ResolvedLearningExplanationLanguage);
    }
}
