using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class ReadingVocabularyCaptureFactoryTests
{
    [Fact]
    public void ReadingTranslationPreservesOriginalUnknownSourceAndWholeTranslatedExample()
    {
        var selected = new ReadingSelection(Guid.NewGuid(), "Hello world!", 1, "field", 1, 0, 12, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        var capture = ReadingVocabularyCaptureFactory.Translation(selected, null, LearningLanguage.Chinese, "你好世界！", 2,
            [new(0, 12)], DateTimeOffset.UnixEpoch);
        Assert.Null(capture.Language); Assert.Equal(VocabularyEntryKind.Sentence, capture.Kind);
        Assert.Equal("Hello world!", capture.Expression); Assert.Equal("你好世界！", capture.Meaning);
        Assert.Equal(LearningLanguage.Chinese, capture.MeaningLanguage);
        Assert.Equal("你好世界！", capture.Example!.Translation); Assert.Equal(VocabularyExampleCompleteness.Sentence, capture.Example.Completeness);
        var fragment = ReadingVocabularyCaptureFactory.Translation(selected, null, LearningLanguage.Chinese, "你好世界！", 2,
            [new(0, 6), new(6, 6)], DateTimeOffset.UnixEpoch);
        Assert.Equal(VocabularyExampleCompleteness.Fragment, fragment.Example!.Completeness);
        Assert.Equal(VocabularyCapturePolicy.EntryKey(capture), VocabularyCapturePolicy.EntryKey(fragment));
    }
    [Fact]
    public void LookupUsesAcceptedMeaningAndUnknownLanguageWithoutInventingAnExample()
    {
        var input = new ReadingLookupInput("bank", null, LearningLanguage.Chinese, true, null);
        var capture = ReadingVocabularyCaptureFactory.Lookup(input, new("河岸", ""), LearningLanguage.Chinese);
        Assert.Null(capture.Language); Assert.Null(capture.Example); Assert.Equal("河岸", capture.Meaning);
        VocabularyCapturePolicy.Validate(capture);
        Assert.NotEqual(VocabularyCapturePolicy.EntryKey(capture), VocabularyCapturePolicy.EntryKey(capture with { Language = LearningLanguage.English }));
    }
}
