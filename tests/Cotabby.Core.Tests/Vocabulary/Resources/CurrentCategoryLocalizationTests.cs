using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Resources;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class CurrentCategoryLocalizationTests
{
    private sealed class Search : IVocabularyTextSearching { public bool Contains(string field, string term) => field.Contains(term, StringComparison.OrdinalIgnoreCase); }
    [Fact]
    public void MultiParentPathsAndLocalizedSearchKeepAncestorsAndIdentity()
    {
        var a = Category(); var b = new VocabularyCategory(Guid.NewGuid(), VocabularyCategoryAxis.Content, [new("B")]); var child = new VocabularyCategory(Guid.NewGuid(), VocabularyCategoryAxis.Content, [new("Child")]) { Localizations = [new("en", Name: "Target")] };
        var index = new VocabularyClassificationIndex { Categories = [a, b, child], Parents = [new(Guid.NewGuid(), child.Id, a.Id), new(Guid.NewGuid(), child.Id, b.Id)] };
        var rows = VocabularyClassificationProjection.CurrentRows(index, [], new HashSet<string>(), new Search(), ["en"], "Target");
        Assert.Equal(4, rows.Count); Assert.Equal(2, rows.Count(x => x.Category.Id == child.Id)); Assert.Equal(2, rows.Where(x => x.Category.Id == child.Id).Select(x => x.Id).Distinct().Count());
        var paths = VocabularyClassificationProjection.ExpansionPaths(new HashSet<Guid> { child.Id }, index); Assert.Single(paths); Assert.Contains(a.Id.ToString("D").ToUpperInvariant(), paths.Single());
    }
    [Fact]
    public void CategoryPayloadDefaultsMissingAndNullGuidance()
    {
        var id = Guid.NewGuid(); var label = Guid.NewGuid(); var json = $$"""{"id":"{{id}}","axis":"content","labels":[{"id":"{{label}}","text":"Name","preferred":false}],"definition":null,"examples":null,"scopeNote":null,"references":null,"localizations":null,"isRetired":null,"provenance":null}""";
        var category = (VocabularyCategory)VocabularyDatabasePayloadCodec.Decode(Cotabby.Core.Vocabulary.Frozen.VocabularyDatabaseTable.Categories, System.Text.Encoding.UTF8.GetBytes(json)); Assert.Empty(category.Examples); Assert.Equal("", category.Definition); Assert.False(category.IsRetired);
    }
    [Fact]
    public void CurrentDocumentKeepsGuidanceAndHistoricalEncoderRefusesLoss()
    {
        var category = Category(); var document = new Cotabby.Core.Vocabulary.Frozen.VocabularyDocument { Classification = new() { Categories = [category] } };
        var encoded = VocabularyDocumentCodec.Encode(document); var roundtrip = VocabularyDocumentCodec.Decode(encoded); Assert.Equal(category, Assert.Single(roundtrip.Classification.Categories));
        Assert.Throws<VocabularyException>(() => VocabularyDocumentCodec.EncodeAt35b(document));
    }
    private static VocabularyCategory Category() => new(Guid.NewGuid(), VocabularyCategoryAxis.Content, [new VocabularyMeaningLabel("原名") { Language = LearningLanguage.Chinese }]) { Definition = "原定义", ScopeNote = "原范围", Examples = [new("not", "en")], Localizations = [new("en", "Negation", "Denies a proposition.", "Different from refusal."), new("fr", "Négation"), new("zh-Hant", "否定", "否定某個命題。"), new("id", "Penyangkalan"), new("tr", "Olumsuzlama"), new("pt-BR", "Negação")] };
    [Fact]
    public void InterfaceFallbackAndOverridesKeepSourceExamples()
    {
        var category = Category(); var french = VocabularyCategoryLocalizationPolicy.Resolve(category, ["fr-CA", "zh-Hant"]); Assert.Equal("Négation", french.Name); Assert.Equal("Denies a proposition.", french.Definition);
        Assert.Equal("否定", VocabularyCategoryLocalizationPolicy.Resolve(category, ["zh_TW"]).Name); Assert.Equal("Negação", VocabularyCategoryLocalizationPolicy.Resolve(category, ["pt_BR"]).Name); Assert.Equal("Olumsuzlama", VocabularyCategoryLocalizationPolicy.Resolve(category, ["tr-TR"]).Name);
        var overlay = new VocabularyCategoryOverride(category.Id) { Definition = "legacy", Localizations = [new("fr", Definition: "local")] }; var effective = VocabularyClassificationPolicy.Effective(category, new() { Overrides = [overlay] });
        Assert.Equal("local", VocabularyCategoryLocalizationPolicy.Resolve(effective, ["fr"]).Definition); Assert.Equal("legacy", VocabularyCategoryLocalizationPolicy.Resolve(effective, ["en"]).Definition); Assert.Equal(category.Examples, effective.Examples);
    }
    [Fact]
    public void DuplicateAndMalformedTranslationsAreRejected()
    {
        Assert.Throws<VocabularyException>(() => VocabularyCategoryLocalizationPolicy.Validate([new("en", Name: "one"), new("EN", Name: "two")]));
        Assert.Throws<VocabularyException>(() => VocabularyCategoryLocalizationPolicy.Validate([new("../en", Name: "bad")]));
        Assert.Throws<VocabularyException>(() => VocabularyCategoryLocalizationPolicy.Validate([new("fr")]));
    }
    [Fact]
    public void LocalEditDoesNotManufactureFallbackAndRestoreRetainsSourceGuidance()
    {
        var category = Category(); var index = new VocabularyClassificationIndex { Categories = [category] };
        var unchanged = VocabularyCategoryLocalizationPolicy.Edit(category, index, "fr", "Négation", "Denies a proposition.", false); Assert.Null(unchanged.Overlay);
        var edited = VocabularyCategoryLocalizationPolicy.Edit(category, index, "fr", "Nom local", "Définition locale.", false); Assert.Equal(category, edited.Category); index = index with { Overrides = [edited.Overlay!] };
        var effective = VocabularyClassificationPolicy.Effective(category, index); Assert.Equal("Nom local", VocabularyCategoryLocalizationPolicy.Resolve(effective, ["fr"]).Name); Assert.Equal("Negation", VocabularyCategoryLocalizationPolicy.Resolve(effective, ["en"]).Name); Assert.Equal(category.Examples, effective.Examples);
        Assert.Null(VocabularyCategoryLocalizationPolicy.Edit(category, index, "fr", "Négation", "Denies a proposition.", false, true).Overlay);
        Assert.Equal("zh-Hans", VocabularyCategoryLocalizationPolicy.EditingLanguage("zh-Hans-TW")); Assert.Equal("pt-BR", VocabularyCategoryLocalizationPolicy.EditingLanguage("pt_BR"));
    }
}
