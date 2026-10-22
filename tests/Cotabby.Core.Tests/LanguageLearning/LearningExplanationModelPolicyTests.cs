using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningExplanationModelPolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void UnconfiguredPathsRetainTheExistingLocalProfileFailure(string path)
    {
        Assert.Equal(LearningTranslationFailure.LocalModelNotConfigured,
            Assert.Throws<LearningTranslationException>(() => LearningExplanationModelPolicy.ResolveLocalProfile(
                new(path, LearningChatTemplate.Qwen3), new("qwen3"))).Failure);
    }

    [Theory]
    [InlineData("hunyuan-dense", null, null, LearningChatTemplate.Qwen3)]
    [InlineData("hunyuan-dense", null, "<|extra_0|>", LearningChatTemplate.ChatMl)]
    [InlineData("gemma3", "TranslateGemma", null, LearningChatTemplate.Gemma3)]
    [InlineData("unknown-architecture", null, null, LearningChatTemplate.Qwen3)]
    public void RenamedMetadataCannotBypassEligibilityWithAManualTemplate(string architecture, string? name,
        string? chatTemplate, LearningChatTemplate selected)
    {
        var failure = Assert.Throws<LearningExplanationException>(() => LearningExplanationModelPolicy.ResolveLocalProfile(
            new("renamed.gguf", selected), new(architecture, name, chatTemplate)));
        Assert.Equal(LearningExplanationFailure.InstructionModelRequired, failure.Failure);
        Assert.Equal("Choose a general instruction model for AI explanation.", failure.Message);
    }

    [Fact]
    public void MissingMetadataStaysUnavailableEvenWithManualFraming()
    {
        Assert.Null(LearningExplanationModelPolicy.GeneralInstructionsAvailable(true, "renamed.gguf", LearningChatTemplate.Qwen3));
        Assert.Throws<LearningExplanationException>(() => LearningExplanationModelPolicy.ResolveLocalProfile(new("renamed.gguf", LearningChatTemplate.Qwen3), null));
    }

    [Theory]
    [InlineData(LearningChatTemplate.Automatic, LearningChatTemplate.Qwen3)]
    [InlineData(LearningChatTemplate.ChatMl, LearningChatTemplate.ChatMl)]
    public void GeneralMetadataConfirmsCapabilityWhileRetainingTheSelectedFraming(LearningChatTemplate selected, LearningChatTemplate expected)
    {
        var result = LearningExplanationModelPolicy.ResolveLocalProfile(new("renamed.gguf", selected), new("qwen3"));
        Assert.Equal(expected, result.Template); Assert.Equal(LearningTranslationPrompt.Family.Generic, result.Family);
    }

    [Fact]
    public void KnownCompletionWeightsStayRejectedEvenWhenMetadataSuggestsAnInstructionFormat()
    {
        Assert.Throws<LearningExplanationException>(() => LearningExplanationModelPolicy.ResolveLocalProfile(
            new("gemma-4-E2B.i1-Q6_K.gguf", LearningChatTemplate.Gemma4), new("gemma4")));
    }
}
