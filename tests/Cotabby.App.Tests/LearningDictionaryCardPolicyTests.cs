using Cotabby.App.Learning;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Tests;

/// <summary>Tests the actual captured-example slices rendered by the contextual WPF card.</summary>
public sealed class LearningDictionaryCardPolicyTests
{
    private static LearningExplanationInput Input(string text, params VocabularyTextRange[] ranges) =>
        new("target", "target", LearningLanguage.English, VocabularyEntryKind.Pattern, text, ranges, LearningExplanationContext.Sentence);

    [Fact]
    public void RepeatedWordsHighlightOnlyTheCapturedOccurrenceAndKeepLiteralWhitespace()
    {
        var input = Input("  had had enough.\n", new VocabularyTextRange(6, 3));
        var runs = LearningDictionaryCardPolicy.ContextRuns(input);
        Assert.Equal(input.Context, string.Concat(runs.Select(run => run.Text)));
        Assert.Equal("had", Assert.Single(runs, run => run.IsTarget).Text);
        Assert.Equal("  had ", runs[0].Text); Assert.False(runs[0].IsTarget);
    }

    [Fact]
    public void DiscontiguousUnorderedAndOverlappingPatternSpansHaveTheSameVisibleUnion()
    {
        var input = Input("keep me posted", new VocabularyTextRange(8, 6), new VocabularyTextRange(0, 4), new VocabularyTextRange(0, 2));
        var runs = LearningDictionaryCardPolicy.ContextRuns(input);
        Assert.Equal(input.Context, string.Concat(runs.Select(run => run.Text)));
        Assert.Equal("keepposted", string.Concat(runs.Where(run => run.IsTarget).Select(run => run.Text)));
        Assert.Equal(" me ", Assert.Single(runs, run => !run.IsTarget).Text);
    }

    [Fact]
    public void GraphemeBoundariesKeepOriginalDecomposedTextAndRejectPartialGraphemes()
    {
        const string text = "cafe\u0301!";
        var valid = LearningDictionaryCardPolicy.ContextRuns(Input(text, new VocabularyTextRange(0, 5)));
        Assert.Equal("cafe\u0301", Assert.Single(valid, run => run.IsTarget).Text);
        Assert.Empty(LearningDictionaryCardPolicy.ContextRuns(Input(text, new VocabularyTextRange(0, 4))));
    }

    [Fact]
    public void ReverseExampleTranslationRequiresTheWholeCompletedSentenceAndStaysOutOfInput()
    {
        var input = Input("keep me posted", new VocabularyTextRange(0, 14));
        var preview = new LearningPreviewState { RequestId = "doc", Status = LearningPreviewStatus.Ready,
            Source = "请让我知道最新情况。", Result = input.Context,
            Options = new() { SourceLanguage = LearningLanguage.Chinese, TargetLanguage = LearningLanguage.English } };
        var display = LearningDictionaryCardPolicy.ExampleTranslation(input, preview);
        Assert.Equal(preview.Source, display!.Text); Assert.Equal(LearningLanguage.Chinese, display.Language);
        Assert.DoesNotContain(preview.Source, input.Context); // The provider still receives only the existing input.
        Assert.Null(LearningDictionaryCardPolicy.ExampleTranslation(Input("keep", new VocabularyTextRange(0, 4)), preview));
        Assert.Null(LearningDictionaryCardPolicy.ExampleTranslation(input, preview with { Status = LearningPreviewStatus.Loading }));
        Assert.Null(LearningDictionaryCardPolicy.ExampleTranslation(input, preview with
        { Options = new() { TargetLanguage = LearningLanguage.French } }));
        Assert.Null(LearningDictionaryCardPolicy.ExampleTranslation(input, null));
    }

    [Fact]
    public void NoneOrInvalidContextCannotPretendToBeAnOriginalExample()
    {
        var none = LearningExplanationInputFactory.FromUnscopedUnit(new(0, "know", "know", LearningUnitKind.Word,
            [new(0, 4)], []), LearningLanguage.English)!;
        Assert.Equal("Meaning", LearningDictionaryCardPolicy.ContextualLabel(none));
        Assert.Empty(LearningDictionaryCardPolicy.ContextRuns(none));
        Assert.Empty(LearningDictionaryCardPolicy.ContextRuns(null));
        Assert.Empty(LearningDictionaryCardPolicy.ContextRuns(Input("short", new VocabularyTextRange(int.MaxValue, 3))));
        Assert.Equal("Contextual meaning", LearningDictionaryCardPolicy.ContextualLabel(Input("text", new VocabularyTextRange(0, 4))));
    }
}
