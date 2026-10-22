using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningTranslationRequestFactoryTests
{
    [Fact]
    public void FactoryFreezesDefaultsSourcePromptAndOutputReserve()
    {
        var request = LearningTranslationRequestFactory.Create("req-1", "你好。", new LearningTranslationOptions());

        Assert.Equal("req-1", request.RequestId);
        Assert.Equal("你好。", request.Source);
        Assert.Equal(LearningLanguage.Chinese, request.Options.SourceLanguage);
        Assert.Equal(LearningLanguage.English, request.Options.TargetLanguage);
        Assert.Equal(8_192, request.Options.ContextTokens);
        Assert.Equal(2_048, request.MaxOutputTokens);
        Assert.False(request.Prompt.UserOnly);
        Assert.Contains("你好。", request.Prompt.Prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hy-mt2:7b", LearningTranslationPrompt.Family.HyMt)]
    [InlineData("provider/TranslateGemma-4B", LearningTranslationPrompt.Family.TranslateGemma)]
    [InlineData("my-translator", LearningTranslationPrompt.Family.Generic)]
    public void ModelAliasSelectsTheMatchingFrozenPromptFamily(string modelName, LearningTranslationPrompt.Family expected)
    {
        var request = LearningTranslationRequestFactory.Create("req-1", "A complete sentence.", new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            ModelName = modelName,
        });

        Assert.Equal(expected == LearningTranslationPrompt.Family.Generic, !request.Prompt.UserOnly);
        Assert.Equal(expected switch
        {
            LearningTranslationPrompt.Family.HyMt => LearningInstructionSampling.HyMt,
            LearningTranslationPrompt.Family.TranslateGemma => LearningInstructionSampling.TranslateGemma,
            _ => LearningInstructionSampling.Standard,
        }, request.Prompt.Sampling);
    }

    [Fact]
    public void FactoryKeepsOnlyTheEffectiveStyleSnapshot()
    {
        var request = LearningTranslationRequestFactory.Create("req-1", "Hello.", new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            Style = new LearningTranslationStyle(LearningTranslationRegister.Formal, "dormant custom text"),
        });

        Assert.Equal(new LearningTranslationStyle(LearningTranslationRegister.Formal), request.Options.Style);
        Assert.Contains("formal written register", request.Prompt.Prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ", LearningTranslationFailure.InvalidRequestId)]
    [InlineData("req-1", LearningTranslationFailure.NoConvertibleText)]
    public void FactoryRejectsInvalidRequestOrNonTextSourceWithFixedFailures(string requestId, LearningTranslationFailure expected)
    {
        var source = expected == LearningTranslationFailure.NoConvertibleText ? "   " : "Hello.";

        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create(requestId, source, new LearningTranslationOptions()));

        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain(source, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextBudgetCannotExceedEndpointCeiling()
    {
        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create("req-1", "Hello.", new LearningTranslationOptions
            {
                ContextTokens = ModelContextBudget.MaximumEndpointTokens + 1,
            }));

        Assert.Equal(LearningTranslationFailure.InvalidContextBudget, error.Failure);
    }

    [Fact]
    public void MalformedUtf16SourceIsRejectedWithoutReplacingTheInvalidSurrogate()
    {
        // Swift String cannot represent this value; a Windows UIA string can contain a lone surrogate.
        var source = "This text contains an invalid surrogate: \uD800";

        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create("req-1", source, new LearningTranslationOptions()));

        Assert.Equal(LearningTranslationFailure.InvalidSourceRange, error.Failure);
        Assert.DoesNotContain("replacement", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TranslationGemmaRejectsStyleBeforeEngineDispatch()
    {
        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create("req-1", "Hello.", new LearningTranslationOptions
            {
                SourceLanguage = LearningLanguage.English,
                ModelName = "translategemma-4b",
                Style = new LearningTranslationStyle(LearningTranslationRegister.Conversational),
            }));

        Assert.Equal(LearningTranslationFailure.UnsupportedStyle, error.Failure);
    }

    [Fact]
    public void SameSourceAndTargetLanguageIsRejectedBeforePromptConstruction()
    {
        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create("req-1", "Hello.", new LearningTranslationOptions
            {
                SourceLanguage = LearningLanguage.English,
                TargetLanguage = LearningLanguage.English,
            }));

        Assert.Equal(LearningTranslationFailure.SameLanguage, error.Failure);
        Assert.Equal(
            "The source language is not enabled or matches the target. Check the translation settings.",
            error.Message);
    }
}
