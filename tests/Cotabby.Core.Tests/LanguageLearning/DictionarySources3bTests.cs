using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class DictionarySources3bTests
{
    [Fact]
    public void CorruptOrderAndEmptyEnabledNormalizeLikeSource()
    {
        var value = new DictionaryConfiguration([DictionarySource.System, new("unknown"), DictionarySource.System], []);
        Assert.Equal([DictionarySource.System, DictionarySource.Bundled, DictionarySource.Online], value.Order);
        Assert.Equal([DictionarySource.Bundled], value.Sources(null));
    }
    [Fact]
    public void UnknownBooksStayManualAndGlossPreferenceRanksWithoutChangingSourceOrder()
    {
        var unknown = Book(null); var english = Book(new(LearningLanguage.English, LearningLanguage.Chinese));
        var chinese = Book(new(LearningLanguage.Chinese, LearningLanguage.English));
        var configuration = new DictionaryConfiguration(enabled: [DictionarySource.Bundled, DictionarySource.Online,
            unknown.Source, english.Source, chinese.Source], libraries: [unknown, english, chinese]);
        Assert.Contains(unknown.Source, configuration.Sources(null));
        Assert.DoesNotContain(unknown.Source, configuration.AutomaticSources(null, LearningLanguage.Chinese));
        Assert.DoesNotContain(DictionarySource.Online, configuration.AutomaticSources(null, LearningLanguage.Chinese));
        Assert.DoesNotContain(chinese.Source, configuration.Sources(LearningLanguage.English));
        Assert.Equal(english.Source, configuration.AutomaticSources(LearningLanguage.English, LearningLanguage.Chinese)[0]);
        Assert.True(new DictionaryLanguagePair(LearningLanguage.Chinese).Supports(LearningLanguage.TraditionalChinese));
    }
    [Fact]
    public void SourceIdentityNormalizesUuidAndRejectsUnknownKinds()
    {
        var id = Guid.Parse("81b75a7a-bc16-4be4-9307-000000000001");
        Assert.Equal(DictionarySource.MDict(id), DictionarySource.Parse("mdict:" + id));
        Assert.Null(DictionarySource.Parse("mdict:not-a-guid")); Assert.Null(DictionarySource.Parse("file:" + id));
    }
    private static MDictLibraryDescriptor Book(DictionaryLanguagePair? languages) =>
        new(Guid.NewGuid(), "Fixture", "fixture.mdx", [], ".", 3, Guid.NewGuid(), languages);
}
