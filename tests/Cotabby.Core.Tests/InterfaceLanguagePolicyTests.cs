using Cotabby.Core.Settings;

namespace Cotabby.Core.Tests;

public class InterfaceLanguagePolicyTests
{
    [Fact]
    public void SystemFallbackSelectsFirstSupportedLanguageInPreferenceOrder()
    {
        Assert.Equal("zh-Hant", InterfaceLanguagePolicy.FirstSupported(["pt-PT", "zh-HK", "ja-JP"]));
        Assert.Equal("en", InterfaceLanguagePolicy.FirstSupported(["zh", "xx-ZZ"]));
    }
    [Theory]
    [InlineData("en_US", "en")]
    [InlineData("zh_CN", "zh-Hans")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-Hant-CN", "zh-Hant")]
    [InlineData("zh-Hans-TW", "zh-Hans")]
    [InlineData("ja-JP", "ja")]
    [InlineData("fr-CA", "fr")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("pt-PT", null)]
    [InlineData("zh", null)]
    [InlineData("xx-ZZ", null)]
    public void NormalizesWithoutInventingUnsupportedLocales(string raw, string? expected) =>
        Assert.Equal(expected, InterfaceLanguagePolicy.Normalize(raw));
}
