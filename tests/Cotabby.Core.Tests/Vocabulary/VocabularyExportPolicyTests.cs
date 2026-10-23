using Cotabby.Core.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Tests.Parity;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyExportPolicyTests
{
    [Fact]
    public void ExactSelectionLeavesTheSourceGraphAndIdentityUntouched()
    {
        var entries = VocabularyExportProjectionParityTests.ExportEntries(); var original = entries[0];
        var selected = VocabularyExportPolicy.Selected(entries, [new(original.Id, original.Senses[1].Id)]);
        Assert.Single(selected); Assert.Single(selected[0].Senses); Assert.Same(original.Senses[1], selected[0].Senses[0]);
        Assert.Same(original, entries[0]); Assert.Equal(2, original.Senses.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularyEntry>)selected).Clear());
    }
    [Fact]
    public void ScopeSnapshotsCannotBeMutatedThroughTheirPublicView()
    {
        var a = new VocabularySelection(Guid.NewGuid(), Guid.NewGuid()); var source = new List<VocabularySelection> { a };
        var request = new VocabularyExportRequest(source, source, source, VocabularyExportScope.Selected);
        source.Clear(); Assert.Equal(a, Assert.Single(request.All)); Assert.Equal(a, Assert.Single(request.Filtered)); Assert.Equal(a, Assert.Single(request.Selected));
        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularySelection>)request.Selected).Clear());
    }
    [Fact]
    public void DocumentCollectionsDetachAtEveryExportOwnershipBoundary()
    {
        var tags = new List<string> { "first" }; var content = new List<VocabularyExportDocument.Block>();
        var senses = new List<VocabularyExportDocument.Sense> { new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "", null, [], "", tags, [], [], content) };
        var entries = new List<VocabularyExportDocument.Entry> { new(Guid.NewGuid(), "word", "word", LearningLanguage.English, VocabularyEntryKind.Word, senses) };
        var document = new VocabularyExportDocument(entries); entries.Clear(); senses.Clear(); tags.Clear(); content.Add(new(Guid.NewGuid(), new("late"), null, null, null));
        var sense = Assert.Single(Assert.Single(document.Entries).Senses);
        Assert.Equal("first", Assert.Single(sense.Tags!)); Assert.Empty(sense.Content!); Assert.Empty(sense.OtherMeanings!); Assert.Empty(sense.Examples!); Assert.Empty(sense.Explanations!);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)sense.Tags!).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularyExportDocument.Entry>)document.Entries).Clear());
    }
    [Fact]
    public void UsageCollectionsAreAlsoFrozen()
    {
        var ranges = new List<VocabularyTextRange> { new(1, 2) }; var translations = new List<VocabularyMeaning> { new("literal", null) };
        var explanations = new List<VocabularyExportDocument.Explanation> { new(LearningLanguage.Chinese, "meaning", "detail") };
        var usage = new VocabularyExportDocument.Usage(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "text", VocabularyExampleCompleteness.Fragment,
            ranges, "translation", null, translations, null, null, explanations);
        ranges.Clear(); translations.Clear(); explanations.Clear();
        Assert.Equal(new(1, 2), Assert.Single(usage.Ranges)); Assert.Equal("literal", Assert.Single(usage.OtherTranslations).Text); Assert.Equal("detail", Assert.Single(usage.Explanations!).Detail);
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularyMeaning>)usage.OtherTranslations).Clear());
    }
    [Fact]
    public void ExportTypesExposeOnlyTheMacPortableLearningMaterialFields()
    {
        // The semantic corpus adapter intentionally chooses keys. Independently assert the entire
        // production public shape so a new host/review/model field cannot hide behind that adapter.
        Fields<VocabularyExportDocument>("Format Version Entries");
        Fields<VocabularyExportDocument.Entry>("Id Expression DictionaryForm Language Kind Senses");
        Fields<VocabularyExportDocument.Sense>("Id CreatedAt Meaning MeaningLanguage OtherMeanings Note Tags Examples Explanations Content");
        Fields<VocabularyExportDocument.Usage>("Id CreatedAt Text Completeness Ranges Translation TranslationLanguage OtherTranslations Meaning MeaningLanguage Explanations");
        Fields<VocabularyExportDocument.Explanation>("Language Meaning Detail");
        Fields<VocabularyExportDocument.Block>("Id Text Image Filename Caption");
    }
    private static void Fields<T>(string names)
    {
        Assert.Equal(names.Split(' ').Order(StringComparer.Ordinal), typeof(T).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(typeof(T).GetProperties(), property => Assert.Null(property.SetMethod));
    }
}
