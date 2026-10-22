using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Inference.Tests.Settings;

/// <summary>
/// Pins the Windows preference migration as learning moves from one source to the Mac source set.
/// These tests parse the production settings class in memory; they never create a settings store,
/// start a desktop app, or read/write the current user's preferences.
/// </summary>
public sealed class AppSettingsMigrationTests
{
    [Theory]
    [InlineData(true, LearningTranslationBackend.Endpoint)]
    [InlineData(false, LearningTranslationBackend.Local)]
    public void LegacyEndpointConsent_SelectsThePreviouslyUsableBackend(
        bool enabled, LearningTranslationBackend expected)
    {
        var legacy = JsonSerializer.Serialize(new
        {
            LearningEndpointEnabled = enabled,
            LearningEndpointModel = "existing-learning-model",
            LearningContextTokens = 4_096,
            LearningStyleRegister = "Formal",
        });

        var settings = SettingsStore.Parse(legacy);

        Assert.Equal(expected, settings.EffectiveLearningBackend);
        Assert.Equal(enabled, settings.LearningEndpointEnabled);
        Assert.Equal("existing-learning-model", settings.LearningEndpointModel);
        Assert.Equal(4_096, settings.LearningContextTokens);
        Assert.Equal(LearningTranslationRegister.Formal, settings.LearningStyleRegister);
    }

    [Theory]
    [InlineData("Local", true, LearningTranslationBackend.Local)]
    [InlineData("Endpoint", false, LearningTranslationBackend.Endpoint)]
    public void ExplicitBackendChoice_DoesNotChangeEndpointConsent(
        string backend, bool consent, LearningTranslationBackend expected)
    {
        var settings = SettingsStore.Parse(JsonSerializer.Serialize(new
        {
            LearningBackend = backend,
            LearningEndpointEnabled = consent,
            LearningEndpointModel = "retained-endpoint-model",
        }));

        Assert.Equal(expected, settings.EffectiveLearningBackend);
        Assert.Equal(consent, settings.LearningEndpointEnabled);
        Assert.Equal("retained-endpoint-model", settings.LearningEndpointModel);
    }

    [Fact]
    public void FreshLocalSettings_DoNotSelectOrWarmAnAutocompleteModel()
    {
        var settings = SettingsStore.Parse("{}");

        Assert.Equal(LearningTranslationBackend.Local, settings.EffectiveLearningBackend);
        Assert.Equal(string.Empty, settings.LearningLocalModel);
        Assert.Equal(LearningChatTemplate.Automatic, settings.LearningLocalTemplate);
        Assert.False(settings.LearningEndpointEnabled);
    }

    [Fact]
    public void LocalProfilesRoundTripIndependentlyOfEndpointPreferences()
    {
        var original = new AppSettings
        {
            LearningBackend = LearningTranslationBackend.Local,
            LearningEndpointEnabled = true,
            LearningEndpointModel = "endpoint-model",
            LearningContextTokens = 4_096,
            LearningStyleRegister = LearningTranslationRegister.Conversational,
            LearningLocalModel = "first.gguf",
            LearningLocalTemplate = LearningChatTemplate.Qwen3,
            LearningLocalContextTokensByModel = new() { ["first.gguf"] = 8_192, ["second.gguf"] = 16_384 },
            LearningLocalGenerationProfiles = new()
            {
                ["first.gguf"] = new LearningGenerationProfile
                {
                    Style = new LearningTranslationStyle(LearningTranslationRegister.Formal),
                    Sampling = new LearningModelSamplingOverrides(Temperature: 0.4,
                        SeedMode: LearningModelSeedMode.Fixed, FixedSeed: 91),
                },
                ["second.gguf"] = new LearningGenerationProfile
                {
                    Style = new LearningTranslationStyle(LearningTranslationRegister.Custom, "Use plain words."),
                },
            },
        };
        var persisted = SettingsStore.Serialize(original);
        var restored = SettingsStore.Parse(persisted);

        Assert.Contains("\"seedMode\": \"fixed\"", persisted, StringComparison.Ordinal);
        Assert.Contains("\"LearningLocalTemplate\": \"qwen3\"", persisted, StringComparison.Ordinal);

        Assert.Equal(LearningChatTemplate.Qwen3, restored.LearningLocalTemplate);
        Assert.Equal(8_192, restored.EffectiveLearningLocalContextTokens("first.gguf"));
        Assert.Equal(8_192, restored.EffectiveLearningLocalContextTokens("FIRST.GGUF"));
        Assert.Equal(16_384, restored.EffectiveLearningLocalContextTokens("second.gguf"));
        Assert.Equal(LearningTranslationRegister.Formal,
            restored.EffectiveLearningLocalGenerationProfile("first.gguf").Style.Register);
        Assert.Equal(91u, restored.EffectiveLearningLocalGenerationProfile("first.gguf").Sampling.FixedSeed);
        Assert.Equal(91u, restored.EffectiveLearningLocalGenerationProfile("FIRST.GGUF").Sampling.FixedSeed);
        Assert.Equal("Use plain words.",
            restored.EffectiveLearningLocalGenerationProfile("second.gguf").Style.CustomInstruction);
        Assert.Equal("endpoint-model", restored.LearningEndpointModel);
        Assert.True(restored.LearningEndpointEnabled);
        Assert.Equal(4_096, restored.LearningContextTokens);
        Assert.Equal(LearningTranslationRegister.Conversational, restored.LearningStyleRegister);
    }

    [Fact]
    public void LocalContextUsesExactCatalogDefaultOrBoundedPerModelOverride()
    {
        var settings = new AppSettings
        {
            LearningLocalContextTokensByModel = new() { ["custom.gguf"] = 100_000 },
        };

        Assert.Equal(4_096, settings.EffectiveLearningLocalContextTokens("translategemma-4b-it-Q4_K_M.gguf"));
        Assert.Equal(8_192, settings.EffectiveLearningLocalContextTokens("renamed.gguf"));
        Assert.Equal(ModelContextBudget.MaximumLocalTokens, settings.EffectiveLearningLocalContextTokens("custom.gguf"));
    }

    [Fact]
    public void MissingSourceSet_PreservesThePreviouslyConfiguredSingleDirection()
    {
        var settings = SettingsStore.Parse("""{"LearningSourceLanguage":"French"}""");

        Assert.Equal([LearningLanguage.French], settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void FreshSettings_KeepTheEstablishedChineseDefault()
    {
        var settings = SettingsStore.Parse("{}");

        Assert.Equal([LearningLanguage.Chinese], settings.EffectiveLearningSourceLanguages);
        Assert.Equal(8_192, settings.LearningContextTokens);
    }

    [Fact]
    public void StoredContextBudget_IsPreservedWhenTheFreshDefaultIsCorrected()
    {
        var settings = SettingsStore.Parse("""{"LearningContextTokens":4096}""");

        Assert.Equal(4_096, settings.LearningContextTokens);
    }

    [Fact]
    public void ExplicitEmptySourceSet_DoesNotRestoreALegacyOrDefaultSource()
    {
        var settings = SettingsStore.Parse("""
            {"LearningSourceLanguage":"French","LearningSourceLanguages":[]}
            """);

        Assert.Empty(settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void SourceSet_TakesPrecedenceAndKeepsDistinctChoicesInUserOrder()
    {
        var settings = SettingsStore.Parse("""
            {"LearningSourceLanguage":"French","LearningSourceLanguages":["Japanese","English","Japanese"]}
            """);

        Assert.Equal([LearningLanguage.Japanese, LearningLanguage.English], settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void EffectiveSources_AreDetachedFromMutablePersistedPreferences()
    {
        var settings = new AppSettings
        {
            LearningSourceLanguages = [LearningLanguage.Japanese, (LearningLanguage)int.MaxValue, LearningLanguage.Japanese],
        };
        var snapshot = settings.EffectiveLearningSourceLanguages;

        settings.LearningSourceLanguages.Clear();

        Assert.Equal([LearningLanguage.Japanese], snapshot);
        Assert.Empty(settings.EffectiveLearningSourceLanguages);
    }

    [Fact]
    public void SaveFormatRoundTrip_KeepsTheFullSourceSetIncludingExplicitEmpty()
    {
        foreach (var sources in new List<LearningLanguage>[] { [], [LearningLanguage.TraditionalChinese, LearningLanguage.English] })
        {
            var original = new AppSettings { LearningSourceLanguages = sources };
            // Match SettingsStore's enum format: the old Windows settings used enum names, and the
            // new list must preserve that format rather than silently changing existing keys.
            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var saved = JsonSerializer.Serialize(original, options);
            var restored = SettingsStore.Parse(saved);

            Assert.NotNull(restored.LearningSourceLanguages);
            Assert.Equal(sources, restored.EffectiveLearningSourceLanguages);
        }
    }
}
