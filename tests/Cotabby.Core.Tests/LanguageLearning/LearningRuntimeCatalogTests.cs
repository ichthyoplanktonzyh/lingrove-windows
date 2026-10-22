using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningRuntimeCatalogTests
{
    [Fact]
    public void RoleCatalogsKeepPinnedModelIdentityAndDownloadValidation()
    {
        var translation = LearningRuntimeCatalog.TranslationModels;
        Assert.Equal(
            new[] { "Hy-MT2-1.8B-Q4_K_M.gguf", "Hy-MT2-7B-Q4_K_M.gguf", "translategemma-4b-it-Q4_K_M.gguf" },
            translation.Select(model => model.Filename));
        Assert.Equal(1_133_080_448L, translation[0].ExpectedSizeBytes);
        Assert.Equal("dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699", translation[0].Sha256);
        Assert.Equal("https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/a0c709d9fac510f2c807aa3af52872340dc37a4a/Hy-MT2-1.8B-Q4_K_M.gguf?download=true",
            translation[0].DownloadUrl.AbsoluteUri);

        var explanation = LearningRuntimeCatalog.ExplanationModels;
        Assert.Equal(new[] { "Gemma-4-E4B-Q4_K_M.gguf", "Qwen3.5-4B-Q4_K_M.gguf" },
            explanation.Select(model => model.Filename));
        Assert.All(translation.Concat(explanation), model => Assert.False(string.IsNullOrEmpty(model.Id)));
    }

    [Theory]
    [InlineData("Hy-MT2-1.8B-Q4_K_M.gguf", 8_192)]
    [InlineData("HY-MT2-1.8B-Q4_K_M.GGUF", 8_192)]
    [InlineData("translategemma-4b-it-Q4_K_M.gguf", 4_096)]
    [InlineData("Hy-MT2-7B-Q4_K_M.gguf", null)]
    [InlineData("custom-translategemma-4b-it-Q4_K_M.gguf", null)]
    [InlineData("TranslateGemma 4B", null)]
    public void ContextRecommendationsRequireAnExactCatalogFilename(string filename, int? expected)
    {
        Assert.Equal(expected, LearningRuntimeCatalog.DefaultTranslationContextWindowTokens(filename));
    }

    [Theory]
    [InlineData("Hy-MT2-1.8B-Q4_K_M.gguf", false)]
    [InlineData("Hy-MT2-7B-Q4_K_M.gguf", false)]
    [InlineData("translategemma-4b-it-Q4_K_M.gguf", false)]
    [InlineData("Qwen3.5-4B-Q4_K_M.gguf", false)]
    [InlineData("Qwen3.5-2B-Base-Q4_K_M.gguf", true)]
    [InlineData("my-custom-model.gguf", true)]
    public void LearningSpecialistsAreNotCompletionBaseCandidates(string filename, bool expected)
    {
        Assert.Equal(expected, LearningRuntimeCatalog.IsCompletionCandidate(filename));
    }
}
