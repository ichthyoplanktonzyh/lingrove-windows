using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningLanguageTests
{
    [Fact]
    public void StableMacLanguageCodesStayUniqueAndRoundTrip()
    {
        var expected = new[]
        {
            "en", "es", "fr", "de", "it", "pt", "nl", "ru", "zh-Hans", "zh-Hant", "ja", "ko", "hi", "ar",
        };

        Assert.Equal(14, LearningLanguages.All.Count);
        Assert.Equal(expected, LearningLanguages.All.Select(language => language.Code()));
        Assert.Equal(14, LearningLanguages.All.Select(language => language.Code()).Distinct(StringComparer.Ordinal).Count());
        foreach (var language in LearningLanguages.All)
            Assert.Equal(language, LearningLanguages.FromCode(language.Code()));
        Assert.Null(LearningLanguages.FromCode("unsupported"));
        Assert.Equal("\"zh-Hans\"", JsonSerializer.Serialize(LearningLanguage.Chinese));
        Assert.Equal(LearningLanguage.TraditionalChinese,
            JsonSerializer.Deserialize<LearningLanguage>("\"zh-Hant\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LearningLanguage>("\"unsupported\""));
    }

    [Fact]
    public void DefaultTranslationOptionsMatchMacFallbackDirectionAndBudget()
    {
        var options = new LearningTranslationOptions();

        Assert.Equal(LearningLanguage.Chinese, options.SourceLanguage);
        Assert.Equal(LearningLanguage.English, options.TargetLanguage);
        Assert.Equal(LearningTranslationRegister.Natural, options.Style.Register);
        Assert.Equal(8_192, options.ContextTokens);
    }
}
