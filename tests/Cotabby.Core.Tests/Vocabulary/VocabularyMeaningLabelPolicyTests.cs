using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Notebook;
using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyMeaningLabelPolicyTests
{
    [Fact]
    public void DisplayPreferenceNormalizesRegionsAndDistinguishesChineseScripts()
    {
        Assert.Equal(new[] { LearningLanguage.Japanese, LearningLanguage.TraditionalChinese, LearningLanguage.English, LearningLanguage.Portuguese },
            VocabularyMeaningLabelPolicy.PreferredLanguages(LearningLanguage.Japanese, ["zh_Hant_TW", "en-US", "ja-JP", "pt-BR", "id-ID"]));
        Assert.Equal(LearningLanguage.Chinese, VocabularyMeaningLabelPolicy.Language("zh-Hans-HK"));
        Assert.Equal(LearningLanguage.TraditionalChinese, VocabularyMeaningLabelPolicy.Language("zh-TW"));
        Assert.Null(VocabularyMeaningLabelPolicy.Language("zh")); Assert.Null(VocabularyMeaningLabelPolicy.Language("unsupported"));
    }

    [Fact]
    public void ExistingLabelsFollowPreferredLanguageAndPreserveIdentity()
    {
        var english = new VocabularyMeaningLabel("Postponement") { Language = LearningLanguage.English };
        var chinese = new VocabularyMeaningLabel("推迟安排") { Language = LearningLanguage.Chinese };
        var french = new VocabularyMeaningLabel("Reporter un événement") { Language = LearningLanguage.French, Preferred = false };
        var node = new VocabularyMeaningNode(Guid.NewGuid(), [english, chinese, french]);
        Assert.Same(chinese, VocabularyMeaningLabelPolicy.Label(node.Labels, [LearningLanguage.Chinese, LearningLanguage.English]));
        Assert.Equal("Reporter un événement", VocabularyMeaningLabelPolicy.Title(node, [LearningLanguage.French, LearningLanguage.English]));
        Assert.Equal("推迟安排", VocabularyMeaningLabelPolicy.Title(node, [LearningLanguage.Japanese, LearningLanguage.Chinese]));
        Assert.Equal("Postponement", VocabularyMeaningLabelPolicy.Title(node, [LearningLanguage.TraditionalChinese]));
        Assert.Equal("Postponement", node.Labels[0].Text);
    }

    [Fact]
    public void UntaggedNameAndPreferredWithinLanguageAreNotReplacedOrRelabeled()
    {
        var first = new VocabularyMeaningLabel("既有名称") { Preferred = false };
        var selected = new VocabularyMeaningLabel("名称") { Preferred = true };
        var english = new VocabularyMeaningLabel("Existing name") { Language = LearningLanguage.English };
        var labels = new[] { first, selected, english };
        Assert.Same(selected, VocabularyMeaningLabelPolicy.Label(labels, [LearningLanguage.Japanese]));
        Assert.Null(selected.Language); Assert.NotEqual(first.Id, selected.Id);
        Assert.Null(VocabularyMeaningLabelPolicy.Label([], [LearningLanguage.English]));
    }
    [Fact]
    public void MixedLanguagesIncludeUnknownAndUseBorrowedCompactNames()
    {
        var date = DateTimeOffset.UnixEpoch;
        var english = new VocabularyEntry(Guid.NewGuid(), "bank", "bank", LearningLanguage.English,
            Cotabby.Core.Vocabulary.VocabularyEntryKind.Word, [], date, date);
        var unknown = new VocabularyEntry(Guid.NewGuid(), "bank", "bank", null,
            Cotabby.Core.Vocabulary.VocabularyEntryKind.Word, [], date, date);
        Assert.True(VocabularyMeaningLabelPolicy.HasMultipleLanguages([english, unknown]));
        Assert.False(VocabularyMeaningLabelPolicy.HasMultipleLanguages([english, english]));
        Assert.Equal("bank · Unspecified", VocabularyMeaningLabelPolicy.ExpressionTitle(unknown, true, _ => "Unspecified"));
        Assert.Equal("bank", VocabularyMeaningLabelPolicy.ExpressionTitle(english, false, _ => throw new Exception("Must not request a label")));
    }
}
