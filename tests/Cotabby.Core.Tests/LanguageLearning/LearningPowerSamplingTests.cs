using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningPowerSamplingTests
{
    [Fact]
    public void EndpointOverridesApplyOnlySupportedParametersAndRemainInRequestIdentity()
    {
        var options = new LearningTranslationOptions { SourceLanguage = LearningLanguage.English, TargetLanguage = LearningLanguage.French,
            ModelName = "general", EndpointSampling = new(Temperature: .8, TopP: .75, TopK: 2) };
        var request = LearningTranslationRequestFactory.Create("synthetic", "Hello world.", options);
        Assert.Equal(.8, request.Prompt.Sampling.Temperature); Assert.Equal(.75, request.Prompt.Sampling.TopP);
        Assert.Equal(LearningInstructionSampling.Standard.TopK, request.Prompt.Sampling.TopK);
        Assert.NotEqual(options, options with { EndpointSampling = new(Temperature: .3) });
        var local = LearningTranslationRequestFactory.Create("synthetic", "Hello world.", options, LearningTranslationPrompt.Family.Generic);
        Assert.Equal(LearningInstructionSampling.Standard.Temperature, local.Prompt.Sampling.Temperature);
    }
}
