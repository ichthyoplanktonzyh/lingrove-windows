using Cotabby.Core.LanguageLearning;
using System.Text;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningTranslationBudgetTests
{
    [Fact]
    public void ContextAllocationAndTranslationOutputReserveMatchMacRules()
    {
        Assert.Equal(8_192 - 512 - 819, ModelContextBudget.InputAllowance(8_192, 512, estimated: true));
        Assert.Equal(8_192 - 512 - 32, ModelContextBudget.InputAllowance(8_192, 512, estimated: false));
        Assert.Equal(0, ModelContextBudget.InputAllowance(100, 500, estimated: true));

        const string source = "A short complete sentence.";
        var expected = Math.Max(
            Math.Min(2_048, Math.Max(256, 8_192 / 3)),
            ModelContextBudget.EstimatedTokens(source) * 2 + 64);
        Assert.Equal(expected, ModelContextBudget.TranslationOutputTokens(source, 8_192));
        Assert.Equal(262_144, ModelContextBudget.MaximumTextBytes);
        Assert.Equal(131_072, ModelContextBudget.MaximumEndpointTokens);
        Assert.Equal(32_768, ModelContextBudget.MaximumTranslationOutputTokens);
    }

    [Fact]
    public void CompletePromptIsRejectedInsteadOfClippingTheTargetToFit()
    {
        var source = new string('a', 5_000);
        var input = new LearningTranslationInput(source, new LearningTranslationStyle());

        var error = Assert.Throws<LearningTranslationException>(() => LearningTranslationBudget.Prompt(
            input, LearningLanguage.English, LearningLanguage.Chinese, LearningTranslationPrompt.Family.Generic,
            total: 2_048, output: 512, estimated: false, count: prompt => prompt.Length));

        Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);
    }

    [Fact]
    public void TranslateGemmaInputLimitAppliesEvenWhenEndpointHasMoreRoom()
    {
        var source = new string('a', 2_000);

        var error = Assert.Throws<LearningTranslationException>(() => LearningTranslationBudget.Prompt(
            new LearningTranslationInput(source, new LearningTranslationStyle()),
            LearningLanguage.English, LearningLanguage.Chinese, LearningTranslationPrompt.Family.TranslateGemma,
            total: 32_768, output: 4_096, estimated: true, count: prompt => prompt.Length));

        Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);
    }

    [Theory]
    [InlineData(LearningTranslationPrompt.Family.Generic)]
    [InlineData(LearningTranslationPrompt.Family.HyMt)]
    public void CustomStyleBytesAreIncludedInTheInputAllowance(LearningTranslationPrompt.Family family)
    {
        var source = "今天没有发送2个文件。";
        var style = new LearningTranslationStyle(LearningTranslationRegister.Custom, "Use concise everyday wording. \"quoted\"");
        var input = new LearningTranslationInput(source, style);
        var bare = LearningTranslationPrompt.Render(source, LearningLanguage.Chinese, LearningLanguage.English, family, style);
        var exactBudget = bare.Prompt.Length + 100 + 32;

        var planned = LearningTranslationBudget.Prompt(
            input, LearningLanguage.Chinese, LearningLanguage.English, family,
            exactBudget, output: 100, estimated: false, count: prompt => prompt.Length);

        Assert.Equal(bare, planned);
        var tooSmall = Assert.Throws<LearningTranslationException>(() => LearningTranslationBudget.Prompt(
            input, LearningLanguage.Chinese, LearningLanguage.English, family,
            exactBudget - 1, output: 100, estimated: false, count: prompt => prompt.Length));
        Assert.Equal(LearningTranslationFailure.OversizedTarget, tooSmall.Failure);
    }

    [Fact]
    public void RaisingContextCanReserveMoreOutputWithoutShorteningSource()
    {
        var source = string.Concat(Enumerable.Repeat("A complete draft sentence. ", 400));
        var smallBudgetError = Assert.Throws<LearningTranslationException>(() => ModelContextBudget.TranslationOutputTokens(source, 8_192));
        Assert.Equal(LearningTranslationFailure.OversizedTarget, smallBudgetError.Failure);

        var reserve = ModelContextBudget.TranslationOutputTokens(source, 32_768);
        var planned = LearningTranslationBudget.Prompt(
            new LearningTranslationInput(source, new LearningTranslationStyle()),
            LearningLanguage.English, LearningLanguage.Chinese, LearningTranslationPrompt.Family.Generic,
            total: 32_768, output: reserve, estimated: true);

        Assert.True(reserve > 8_192 && reserve < 32_768);
        Assert.Contains(source, planned.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceAtByteCapIsAcceptedByThePromptBoundaryAndOneByteOverIsRejected()
    {
        var atLimit = new string('a', ModelContextBudget.MaximumTextBytes);
        var overLimit = new string('a', ModelContextBudget.MaximumTextBytes + 1);

        var accepted = LearningTranslationPrompt.Render(
            atLimit, LearningLanguage.English, LearningLanguage.Chinese, LearningTranslationPrompt.Family.Generic);
        var error = Assert.Throws<LearningTranslationException>(() => LearningTranslationPrompt.Render(
            overLimit, LearningLanguage.English, LearningLanguage.Chinese, LearningTranslationPrompt.Family.Generic));

        Assert.EndsWith("\"" + atLimit + "\"", accepted.Prompt, StringComparison.Ordinal);
        Assert.Equal(1_000, Encoding.UTF8.GetByteCount(new LearningTranslationStyle(
            LearningTranslationRegister.Custom, new string('a', 4_000)).Normalized.CustomInstruction));
        Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);
    }
}
