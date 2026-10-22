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
    public async Task CompletedPreviewAnnotationsAndDictionarySessionUseTheSameActualResource()
    {
        var service = Create();
        await using var preview = new LearningPreviewCoordinator(unitRecognizer: service);
        var annotated = new TaskCompletionSource<LearningPreviewState>(TaskCreationOptions.RunContinuationsAsynchronously);
        preview.StateChanged += state =>
        {
            if (state.Status == LearningPreviewStatus.Ready && state.UnitStage == LearningUnitStage.Ready)
                annotated.TrySetResult(state);
        };
        await preview.PreviewAsync("fixture source", new LearningTranslationOptions { ModelName = "fixture" },
            new FixtureEngine()).WaitAsync(TimeSpan.FromSeconds(10));
        var state = await annotated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var expression = Assert.Single(state.Units, unit => unit.Kind == LearningUnitKind.Expression);
        Assert.Equal("look forward to", expression.Surface);
        Assert.Equal(state.Result, string.Concat(LearningUnitDisplayPolicy.Runs(state.Result!, state.Units).Select(run => run.Text)));
        await using var dictionary = new LearningDictionarySession(service);
        await dictionary.OpenAsync(expression, state.RequestId, LearningLanguage.English, state.Result).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(Assert.IsType<LearningDictionaryResult.Entries>(dictionary.State.Result).Values,
            entry => entry.Id == "1b618b011fddd4a5");
        Assert.Equal(state.RequestId, dictionary.State.DocumentId);
        dictionary.Close(); preview.Cancel();
        Assert.Null(dictionary.State.Unit); Assert.Empty(preview.State.Units);
    }

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

    private sealed class FixtureEngine : ILearningTranslationEngine
    {
        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult("I look forward to seeing you.");
    }
}
