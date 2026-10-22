using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Tests.Settings;

public sealed class LearningExplanationSettingsTests
{
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
