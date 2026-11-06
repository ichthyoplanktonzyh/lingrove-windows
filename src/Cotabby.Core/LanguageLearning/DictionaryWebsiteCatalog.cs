namespace Cotabby.Core.LanguageLearning;

/// <summary>The eight stable recommended website descriptors from Mac 3b. Adding one does not enable it.</summary>
public static class DictionaryWebsiteCatalog
{
    public static IReadOnlyList<DictionaryWebsite> Websites { get; } = Array.AsReadOnly(new[]
    {
        Site(1, "Cambridge · English–中文", "https://dictionary.cambridge.org/dictionary/english-chinese-simplified/{query}", LearningLanguage.English, LearningLanguage.Chinese),
        Site(2, "Cambridge · English–繁體中文", "https://dictionary.cambridge.org/dictionary/english-chinese-traditional/{query}", LearningLanguage.English, LearningLanguage.TraditionalChinese),
        Site(3, "Cambridge · English", "https://dictionary.cambridge.org/dictionary/english/{query}", LearningLanguage.English, LearningLanguage.English),
        Site(4, "Oxford Learner’s Dictionaries", "https://www.oxfordlearnersdictionaries.com/definition/english/{query}", LearningLanguage.English, LearningLanguage.English),
        Site(5, "Wiktionary · English", "https://en.wiktionary.org/wiki/{query}", null, LearningLanguage.English),
        Site(6, "維基詞典 · 中文", "https://zh.wiktionary.org/wiki/{query}", null, LearningLanguage.Chinese),
        Site(7, "汉典", "https://www.zdic.net/hans/{query}", LearningLanguage.Chinese, LearningLanguage.Chinese),
        Site(8, "Diccionario de la lengua española", "https://dle.rae.es/{query}", LearningLanguage.Spanish, LearningLanguage.Spanish),
    });
    private static DictionaryWebsite Site(int number, string name, string template, LearningLanguage? entry, LearningLanguage definition) =>
        new(Guid.Parse($"81B75A7A-BC16-4BE4-9307-0000000000{number:00}"), name, template, new(entry, definition, entry is null));
}
