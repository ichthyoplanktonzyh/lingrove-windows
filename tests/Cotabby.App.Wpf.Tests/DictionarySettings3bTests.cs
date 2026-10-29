using Cotabby.App.Dictionaries;
using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Wpf.Tests;

public sealed class DictionarySettings3bTests
{
    [Fact]
    public void ReimportPreservesIdentityAndLanguagesAndRemovingNeverDeletesFiles()
    {
        var settings = new AppSettings(); int saves = 0;
        var owner = new DictionarySettings(() => settings, () => saves++);
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "synthetic-book.mdx"));
        var book = new MDictLibraryDescriptor(Guid.NewGuid(), "Synthetic", path, [], System.IO.Path.GetDirectoryName(path)!, 2, Guid.NewGuid());
        Assert.True(owner.Add(book));
        Assert.True(owner.SetLanguages(book.Source, new(LearningLanguage.English, LearningLanguage.Chinese)));
        Assert.True(owner.Add(book with { Id = Guid.NewGuid(), Revision = Guid.NewGuid(), Name = "Updated" }));
        var saved = Assert.Single(owner.Configuration.Libraries);
        Assert.Equal(book.Id, saved.Id); Assert.Equal(LearningLanguage.English, saved.Languages?.Entry);
        Assert.True(owner.SetDefinitionLanguage(LearningLanguage.Chinese));
        var reloaded = new DictionarySettings(() => settings, () => { });
        Assert.Equal(saved.Id, Assert.Single(reloaded.Configuration.Libraries).Id);
        Assert.Equal(LearningLanguage.Chinese, reloaded.Configuration.DefinitionLanguage);
        Assert.True(owner.Remove(book.Source)); Assert.Empty(owner.Configuration.Libraries); Assert.Equal(5, saves);
    }

    [Fact]
    public void FailedPersistenceRollsBackSettingsAndDoesNotPublish()
    {
        var settings = new AppSettings(); var owner = new DictionarySettings(() => settings, () => throw new InvalidOperationException());
        int changes = 0; owner.Changed += _ => changes++;
        Assert.Throws<InvalidOperationException>(() => owner.SetDefinitionLanguage(LearningLanguage.Chinese));
        Assert.Null(settings.LearningDictionaryCatalogValue); Assert.Null(owner.Configuration.DefinitionLanguage); Assert.Equal(0, changes);
    }
}
