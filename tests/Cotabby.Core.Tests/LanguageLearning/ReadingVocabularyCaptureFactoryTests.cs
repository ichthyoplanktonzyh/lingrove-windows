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
    [Theory]
    [InlineData(ReadingBoundary.Sentence, ReadingBoundary.Sentence, VocabularyExampleCompleteness.Sentence)]
    [InlineData(ReadingBoundary.Crop, ReadingBoundary.Unknown, VocabularyExampleCompleteness.Fragment)]
    [InlineData(ReadingBoundary.Unknown, ReadingBoundary.Unknown, VocabularyExampleCompleteness.Unknown)]
    public void ContextualLookupPreservesExactOccurrenceAndSourceCompleteness(ReadingBoundary left, ReadingBoundary right, VocabularyExampleCompleteness completeness)
    {
        var context = new ReadingContext("bank near the bank", 14, 4, left, right, ReadingContextMethod.Characters);
        var selection = new ReadingSelection(Guid.NewGuid(), "bank", 1, "field", 1, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty, context) { SourceApplicationId = "editor" };
        var input = new ReadingLookupInput("bank", null, LearningLanguage.Chinese, true, context);
        var capture = ReadingVocabularyCaptureFactory.Lookup(input, new("河岸", "整句译文"), LearningLanguage.Chinese, selection, DateTimeOffset.UnixEpoch);
        VocabularyCapturePolicy.Validate(capture); Assert.Equal(selection.Id, capture.Example!.Id);
        Assert.Equal(completeness, capture.Example.Completeness); Assert.Equal(new VocabularyTextRange(14, 4), Assert.Single(capture.Example.Ranges));
        Assert.Equal("整句译文", capture.Example.Translation); Assert.Equal("editor", capture.Example.SourceApplicationId);
        Assert.Equal("河岸", capture.Meaning); Assert.Equal("河岸", capture.Example.Meaning);
        Assert.Equal(VocabularyMeaningOrigin.Ai, capture.Example.MeaningOrigin);
        Assert.Equal((Cotabby.Core.Vocabulary.Resources.VocabularyExampleCompleteness)(int)completeness,
            Cotabby.Core.Vocabulary.Frozen.CanonicalWritingPolicy.FromLive(capture).Example!.Completeness);
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
