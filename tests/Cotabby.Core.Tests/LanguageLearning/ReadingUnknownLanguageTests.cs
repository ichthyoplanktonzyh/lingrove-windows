using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class ReadingUnknownLanguageTests
{
    [Theory]
    [InlineData(LearningTranslationPrompt.Family.Generic)]
    [InlineData(LearningTranslationPrompt.Family.HyMt)]
    [InlineData(LearningTranslationPrompt.Family.TranslateGemma)]
    public void UnknownLanguageStaysUnknownAcrossPlanningAndUsesFrozenFamilyContract(LearningTranslationPrompt.Family family)
    {
        var options = new LearningTranslationOptions { SourceLanguage = null, TargetLanguage = LearningLanguage.Chinese, ContextTokens = 8192 };
        var request = LearningTranslationRequestFactory.Create("reading-unknown", "Opaque text.", options, family);
        Assert.Null(request.Options.SourceLanguage);
        Assert.Contains("Opaque text.", request.Prompt.Prompt);
        if (family == LearningTranslationPrompt.Family.Generic) Assert.Contains("following text into natural Simplified Chinese", request.Prompt.Prompt);
        if (family == LearningTranslationPrompt.Family.TranslateGemma)
        {
            Assert.Contains("Infer the source language from the text.", request.Prompt.Prompt);
            Assert.True(request.Prompt.UserOnly);
            Assert.Equal(LearningInstructionSampling.TranslateGemma, request.Prompt.Sampling);
        }
    }

    [Fact]
    public void KnownDirectionKeepsItsStableSerializedCodeAndNullRemainsNull()
    {
        var options = new LearningTranslationOptions { SourceLanguage = LearningLanguage.Chinese };
        var known = System.Text.Json.JsonSerializer.Serialize(options);
        Assert.Contains("\"SourceLanguage\":\"zh-Hans\"", known);
        Assert.Contains("\"SourceLanguage\":null", System.Text.Json.JsonSerializer.Serialize(options with { SourceLanguage = null }));
    }

    [Fact]
    public void KnownSameLanguageStillFailsButUnknownDoesNotInventDirection()
    {
        var options = new LearningTranslationOptions { SourceLanguage = LearningLanguage.English, TargetLanguage = LearningLanguage.English };
        Assert.Throws<LearningTranslationException>(() => LearningTranslationRequestFactory.Create("known", "Some text.", options));
        Assert.Null(LearningTranslationRequestFactory.Create("unknown", "Some text.", options with { SourceLanguage = null }).Options.SourceLanguage);
    }
}
