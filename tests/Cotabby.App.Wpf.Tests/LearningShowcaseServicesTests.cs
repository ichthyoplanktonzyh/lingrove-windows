using Cotabby.App.UI;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class LearningShowcaseServicesTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task SampleDictionaryUsesRealSessionAndBothLocalSources() => dispatcher.RunAsync(async () =>
    {
        await using var session = new LearningDictionarySession(new LearningShowcaseDictionary(), new LearningShowcaseExplainer());
        var unit = new LearningUnit(0, "posted", "posted", LearningUnitKind.Word, [new(0, 6)], [new("posted", new(0, 6))]);
        await session.OpenAsync(unit, "sample", LearningLanguage.English);
        var identity = session.State.TargetIdentity;
        await session.SelectPageAsync(LearningDictionaryPage.Dictionary, identity);
        await session.SelectSourceAsync(DictionarySource.System, identity);
        Assert.Equal(DictionarySource.System, session.State.SelectedSource);
        Assert.IsType<LearningDictionaryResult.Entries>(session.State.Result);
        await session.SelectSourceAsync(DictionarySource.Bundled, identity);
        Assert.Equal(DictionarySource.Bundled, session.State.SelectedSource);
        session.Close(); Assert.Null(session.State.Unit);
    });
}
