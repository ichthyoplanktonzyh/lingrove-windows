using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.LanguageLearning;
using Cotabby.Win32.Text;

namespace Cotabby.Inference.Tests.LanguageLearning;

/// <summary>Actual pinned data + actual Windows ICU, adapted from Mac LearningLexiconServiceTests.</summary>
public sealed class BundledLearningLexiconTests
{
    private static LearningLexiconService Create() => new(
        BundledLearningLexiconResourceSource.FromBundleDirectory(AppContext.BaseDirectory), new IcuLearningLexicalText());

    [Fact]
    public async Task ActualEnglishExpressionsRecordedFormsAndConstituentWordsRetainTheirIdentity()
    {
        var service = Create();
        var units = await service.RecognizeAsync("I look forward to seeing you.", LearningLanguage.English);
        Assert.Equal(["I", "look forward to", "seeing", "you"], units.Select(unit => unit.Surface));
        var result = Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupAsync("looking forward to", LearningLanguage.English, true));
        Assert.Contains(result.Values, entry => entry.Headword == "look forward to" && entry.Senses.Count > 0);
        Assert.All(result.Values, entry => Assert.StartsWith("https://en.wiktionary.org/", entry.SourceUrl));
        Assert.Equal(LearningDictionaryResult.NotFound, await service.LookupAsync("blorblytastic snorf", LearningLanguage.English, true));
        foreach (var word in new[] { "go", "figures" })
            Assert.Equal(LearningDictionaryResult.NotFound, await service.LookupAsync(word, LearningLanguage.English, false));
    }

    [Theory]
    [InlineData("a pesar de", LearningLanguage.Spanish)]
    [InlineData("auf jeden Fall", LearningLanguage.German)]
    [InlineData("afin de", LearningLanguage.French)]
    [InlineData("안녕하세요", LearningLanguage.Korean)]
    [InlineData("一举两得", LearningLanguage.Chinese)]
    [InlineData("一舉兩得", LearningLanguage.TraditionalChinese)]
    public async Task ActualRecordedMultilingualExpressionsRemainWhole(string expression, LearningLanguage language)
    {
        var service = Create();
        Assert.Contains(await service.RecognizeAsync(expression, language),
            unit => unit.Surface == expression && unit.Kind == LearningUnitKind.Expression);
        var result = Assert.IsType<LearningDictionaryResult.Entries>(await service.LookupAsync(expression, language, true));
        string code = language is LearningLanguage.Chinese or LearningLanguage.TraditionalChinese ? "zh" : language.Code();
        Assert.All(result.Values, entry => Assert.Equal(code, entry.Language));
    }
}
