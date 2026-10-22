using System.Text.Json;
using System.Text.Json.Serialization;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningLocalModelConfigurationTests
{
    [Fact]
    public void ConfigurationFreezesModelPathTemplateContextAndCpuDefault()
    {
        var configuration = new LearningLocalModelConfiguration(@"C:\Models\model.gguf");

        Assert.Equal(@"C:\Models\model.gguf", configuration.ModelPath);
        Assert.Equal(LearningChatTemplate.Automatic, configuration.RequestedTemplate);
        Assert.Equal(8_192, configuration.ContextWindowTokens);
        Assert.Null(configuration.SamplingOverrides);
        Assert.Equal(0, configuration.GpuLayerCount);

        Assert.Equal(ModelContextBudget.MaximumLocalTokens,
            new LearningLocalModelConfiguration("max.gguf", contextWindowTokens: ModelContextBudget.MaximumLocalTokens)
                .ContextWindowTokens);
    }

    [Theory]
    [InlineData(1_023)]
    [InlineData(32_769)]
    public void LocalContextOutsideMacSettingsRangeIsRejected(int contextTokens)
    {
        var error = Assert.Throws<LearningTranslationException>(() =>
            new LearningLocalModelConfiguration("model.gguf", contextWindowTokens: contextTokens));

        Assert.Equal(LearningTranslationFailure.InvalidContextBudget, error.Failure);
    }

    [Fact]
    public void InvalidGpuLayerHintAndTemplateAreRejected()
    {
        var gpu = Assert.Throws<LearningTranslationException>(() =>
            new LearningLocalModelConfiguration("model.gguf", gpuLayerCount: -1));
        Assert.Equal(LearningTranslationFailure.InvalidOptions, gpu.Failure);

        var template = Assert.Throws<LearningTranslationException>(() =>
            new LearningLocalModelConfiguration("model.gguf", (LearningChatTemplate)int.MaxValue));
        Assert.Equal(LearningTranslationFailure.InvalidOptions, template.Failure);
    }

    [Fact]
    public void SamplingOverridesKeepFamilyDefaultsAndBoundPersistedValues()
    {
        var defaults = LearningModelSamplingValues.FromInstruction(LearningInstructionSampling.HyMt)
            .Applying(new LearningModelSamplingOverrides());
        Assert.Equal(0.7, defaults.Temperature);
        Assert.Equal(0.6, defaults.TopP);
        Assert.Equal(20, defaults.TopK);
        Assert.Equal(1.05, defaults.RepetitionPenalty);
        Assert.Equal(uint.MaxValue, defaults.Seed);

        var overrides = new LearningModelSamplingOverrides(
            Temperature: double.PositiveInfinity,
            TopP: -1,
            TopK: int.MaxValue,
            RepetitionPenalty: double.NaN,
            SeedMode: LearningModelSeedMode.Fixed,
            FixedSeed: uint.MaxValue);
        var resolved = defaults.Applying(overrides);

        Assert.True(Math.Abs(0.7 - resolved.Temperature) < 1e-12,
            "A non-finite value keeps the adapter recommendation.");
        Assert.Equal(0.01, resolved.TopP);
        Assert.Equal(256, resolved.TopK);
        Assert.True(Math.Abs(1.05 - resolved.RepetitionPenalty) < 1e-12,
            "A non-finite value keeps the adapter recommendation.");
        Assert.Equal(uint.MaxValue - 1, resolved.Seed);
        Assert.Equal(uint.MaxValue,
            new LearningModelSamplingOverrides(SeedMode: LearningModelSeedMode.Random).ResolveSeed(123));
    }

    [Fact]
    public void EndpointOnlySamplingDropsNativeOnlyFields()
    {
        var endpoint = new LearningModelSamplingOverrides(
            Temperature: 0.2,
            TopP: 0.8,
            TopK: 5,
            RepetitionPenalty: 1.2,
            SeedMode: LearningModelSeedMode.Fixed,
            FixedSeed: 9).EndpointOnly;

        Assert.Equal(new LearningModelSamplingOverrides(Temperature: 0.2, TopP: 0.8), endpoint);
    }

    [Theory]
    [InlineData(LearningChatTemplate.Automatic, "automatic")]
    [InlineData(LearningChatTemplate.HyMt, "hyMT")]
    [InlineData(LearningChatTemplate.HyMt7, "hyMT7")]
    [InlineData(LearningChatTemplate.ChatMl, "chatML")]
    [InlineData(LearningChatTemplate.Gemma4Large, "gemma4Large")]
    [InlineData(LearningChatTemplate.TranslateGemma, "translateGemma")]
    public void TemplatePreferencesUseMacStableJsonIds(LearningChatTemplate template, string rawValue)
    {
        var json = JsonSerializer.Serialize(template);

        Assert.Equal($"\"{rawValue}\"", json);
        Assert.Equal(template, JsonSerializer.Deserialize<LearningChatTemplate>(json));
    }

    [Fact]
    public void SamplingProfileUsesMacStablePropertyAndSeedIdentifiers()
    {
        var json = JsonSerializer.Serialize(new LearningModelSamplingOverrides(
            Temperature: 0.2,
            TopP: 0.8,
            SeedMode: LearningModelSeedMode.Fixed,
            FixedSeed: 7));
        using var document = JsonDocument.Parse(json);

        Assert.Equal(0.2, document.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(0.8, document.RootElement.GetProperty("topP").GetDouble());
        Assert.Equal("fixed", document.RootElement.GetProperty("seedMode").GetString());
        Assert.Equal(7u, document.RootElement.GetProperty("fixedSeed").GetUInt32());
    }

    [Fact]
    public void GenerationProfileRoundTripsWithSettingsJsonNamesAndGlobalEnumConverter()
    {
        // SettingsStore installs JsonStringEnumConverter globally. It takes precedence over the
        // enum's converter attribute, so this test preserves the existing PascalCase enum values
        // while the newly added property attributes keep the Mac lowerCamelCase field names.
        var settingsJson = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        var expected = new LearningGenerationProfile
        {
            Style = new LearningTranslationStyle(LearningTranslationRegister.Formal, "document tone"),
            Sampling = new LearningModelSamplingOverrides(
                Temperature: 0.25,
                TopP: 0.85,
                TopK: 24,
                RepetitionPenalty: 1.1,
                SeedMode: LearningModelSeedMode.Fixed,
                FixedSeed: 17),
            MaximumOutputTokens = 1_024,
        };

        var json = JsonSerializer.Serialize(expected, settingsJson);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Formal", root.GetProperty("style").GetProperty("register").GetString());
        Assert.Equal("document tone", root.GetProperty("style").GetProperty("customInstruction").GetString());
        Assert.Equal("Fixed", root.GetProperty("sampling").GetProperty("seedMode").GetString());
        Assert.Equal(1_024, root.GetProperty("maximumOutputTokens").GetInt32());

        var restored = JsonSerializer.Deserialize<LearningGenerationProfile>(json, settingsJson);
        Assert.Equal(expected, restored);
    }

    [Fact]
    public void SpecialistTemplateAvailabilityIsRoleSpecific()
    {
        Assert.Contains(LearningChatTemplate.TranslateGemma,
            LearningChatTemplatePolicy.AvailableFor(LearningModelRole.Translation));
        Assert.DoesNotContain(LearningChatTemplate.TranslateGemma,
            LearningChatTemplatePolicy.AvailableFor(LearningModelRole.Explanation));
        Assert.DoesNotContain(LearningChatTemplate.HyMt,
            LearningChatTemplatePolicy.AvailableFor(LearningModelRole.Explanation));
        Assert.Contains(LearningChatTemplate.Automatic,
            LearningChatTemplatePolicy.AvailableFor(LearningModelRole.Explanation));
    }
}
