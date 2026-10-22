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
            TargetLanguage = LearningLanguage.Chinese,
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
    public void ExplicitLocalFamilyWinsOverFilenameLikeModelNameAndPreservesOptions()
    {
        var options = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            TargetLanguage = LearningLanguage.Chinese,
            ModelName = "translategemma-renamed.gguf",
            Style = new LearningTranslationStyle(LearningTranslationRegister.Formal, "dormant custom text"),
        };

        var request = LearningTranslationRequestFactory.Create(
            "req-local", "A complete sentence.", options, LearningTranslationPrompt.Family.Generic);

        Assert.False(request.Prompt.UserOnly);
        Assert.Equal(LearningInstructionSampling.Standard, request.Prompt.Sampling);
        Assert.Equal(options, request.Options);
        Assert.Contains("formal written register", request.Prompt.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitLocalTranslateGemmaFamilyRetainsItsStyleRestriction()
    {
        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create(
                "req-local", "Hello.", new LearningTranslationOptions
                {
                    SourceLanguage = LearningLanguage.English,
                    TargetLanguage = LearningLanguage.Chinese,
                    ModelName = "renamed.gguf",
                    Style = new LearningTranslationStyle(LearningTranslationRegister.Conversational),
                },
                LearningTranslationPrompt.Family.TranslateGemma));

        Assert.Equal(LearningTranslationFailure.UnsupportedStyle, error.Failure);
    }

    [Fact]
    public void ExactLocalTokenCountUsesNativeBudgetMarginRatherThanEndpointEstimate()
    {
        var options = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            TargetLanguage = LearningLanguage.Chinese,
            ContextTokens = 8_192,
        };

        var request = LearningTranslationRequestFactory.Create(
            "req-local", "Hello.", options, LearningTranslationPrompt.Family.Generic,
            exactTokenCount: _ => 8_192 - requestOutputReserve(options) - 32);

        Assert.Equal("Hello.", request.Source);
        Assert.Equal(8_192 - request.MaxOutputTokens - 32,
            ModelContextBudget.InputAllowance(options.ContextTokens, request.MaxOutputTokens, estimated: false));

        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create(
                "req-local", "Hello.", options, LearningTranslationPrompt.Family.Generic,
                exactTokenCount: _ => 8_192 - request.MaxOutputTokens - 31));
        Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);

        static int requestOutputReserve(LearningTranslationOptions value) =>
            ModelContextBudget.TranslationOutputTokens("Hello.", value.ContextTokens);
    }

    [Fact]
    public void ExactLocalCountEnforcesTranslateGemmaTaskLimit()
    {
        var options = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            TargetLanguage = LearningLanguage.Chinese,
        };

        var request = LearningTranslationRequestFactory.Create(
            "req-local", "Hello.", options, LearningTranslationPrompt.Family.TranslateGemma,
            exactTokenCount: _ => 2_048);
        Assert.True(request.Prompt.UserOnly);

        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create(
                "req-local", "Hello.", options, LearningTranslationPrompt.Family.TranslateGemma,
                exactTokenCount: _ => 2_049));
        Assert.Equal(LearningTranslationFailure.TranslateGemmaInputTooLarge, error.Failure);
    }

    [Fact]
    public void ExactLocalPlanUsesSmallerNativeContextButKeepsConfiguredOutputReserve()
    {
        var options = new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            TargetLanguage = LearningLanguage.Chinese,
            ContextTokens = 8_192,
        };
        var configuredOutput = ModelContextBudget.TranslationOutputTokens("Hello.", options.ContextTokens);
        var actualAllowance = ModelContextBudget.InputAllowance(
            total: 4_096, output: configuredOutput, estimated: false);

        var request = LearningTranslationRequestFactory.Create(
            "req-local", "Hello.", options, LearningTranslationPrompt.Family.Generic,
            actualContextWindowTokens: 4_096,
            exactTokenCount: _ => actualAllowance);

        Assert.Equal(configuredOutput, request.MaxOutputTokens);
        Assert.Equal(options, request.Options);
        var error = Assert.Throws<LearningTranslationException>((Action)(() =>
        {
            _ = LearningTranslationRequestFactory.Create(
                "req-local", "Hello.", options, LearningTranslationPrompt.Family.Generic,
                actualContextWindowTokens: 4_096,
                exactTokenCount: _ => actualAllowance + 1);
        }));
        Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32_769)]
    public void ExactLocalPlanRejectsInvalidNativeContext(int actualContext)
    {
        var error = Assert.Throws<LearningTranslationException>(() =>
            LearningTranslationRequestFactory.Create(
                "req-local", "Hello.", new LearningTranslationOptions
                {
                    SourceLanguage = LearningLanguage.English,
                    TargetLanguage = LearningLanguage.Chinese,
                },
                LearningTranslationPrompt.Family.Generic,
                actualContext,
                exactTokenCount: _ => 20));

        Assert.Equal(LearningTranslationFailure.InvalidContextBudget, error.Failure);
    }

    [Fact]
    public void FactoryKeepsOnlyTheEffectiveStyleSnapshot()
    {
        var request = LearningTranslationRequestFactory.Create("req-1", "Hello.", new LearningTranslationOptions
        {
            SourceLanguage = LearningLanguage.English,
            TargetLanguage = LearningLanguage.Chinese,
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
                TargetLanguage = LearningLanguage.Chinese,
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
