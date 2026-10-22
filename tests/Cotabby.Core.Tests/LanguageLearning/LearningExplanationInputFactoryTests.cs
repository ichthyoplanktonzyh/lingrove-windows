using System.Collections;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningExplanationInputFactoryTests
{
    private static LearningPreviewState Ready => new()
    {
        RequestId = "capture", Status = LearningPreviewStatus.Ready, Source = "原句。",
        Result = "Please keep me posted.",
        Options = new() { SourceLanguage = LearningLanguage.Chinese, TargetLanguage = LearningLanguage.English },
    };

    [Theory]
    [InlineData("null")]
    [InlineData("throw")]
    [InlineData("enumerator")]
    [InlineData("negative")]
    [InlineData("empty")]
    [InlineData("outside")]
    [InlineData("overflow")]
    [InlineData("overlap-after-match")]
    [InlineData("unordered")]
    public void UnusableSentenceFactsReturnOnlyTheFixedFailure(string failure)
    {
        var facts = new Facts
        {
            Sentences = failure switch
            {
                "null" => () => null!,
                "throw" => () => throw new InvalidOperationException("private captured text"),
                "enumerator" => () => new ThrowingRanges(),
                "negative" => () => [new(-1, 1)],
                "empty" => () => [new(0, 0)],
                "outside" => () => [new(0, 23)],
                "overflow" => () => [new(1, int.MaxValue)],
                "overlap-after-match" => () => [new(0, 22), new(1, 1)],
                "unordered" => () => [new(15, 7), new(0, 15)],
                _ => throw new InvalidOperationException(),
            },
        };
        var error = Assert.Throws<LearningTranslationException>(() => new LearningExplanationInputFactory(facts, facts).FromWritingSentencePreview(Ready));
        Assert.Equal(LearningTranslationFailure.SentenceTokenizerUnavailable, error.Failure);
        Assert.DoesNotContain("private", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void SentenceFactMayNotSplitAnOriginalGrapheme()
    {
        var facts = new Facts { Sentences = () => [new(0, 1), new(1, 4)] };
        var error = Assert.Throws<LearningTranslationException>(() =>
            new LearningExplanationInputFactory(facts, facts).FromWritingSentencePreview(Ready with { Result = "e\u0301 me" }));
        Assert.Equal(LearningTranslationFailure.SentenceTokenizerUnavailable, error.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WordProviderFailureCannotChangeTheTargetKindOrLeakCapturedText(bool throws)
    {
        var facts = new Facts { WordFacts = () => throws ? throw new InvalidOperationException("private captured text") : null! };
        var error = Assert.Throws<LearningLexicalException>(() => new LearningExplanationInputFactory(facts, facts).FromWritingSentencePreview(Ready));
        Assert.Equal(LearningLexicalFailure.TextProviderUnavailable, error.Failure);
        Assert.DoesNotContain("private", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void EmptySentenceFactsRetainTheWholeContextAsAFragment()
    {
        var facts = new Facts { Sentences = () => [] };
        var capture = new LearningExplanationInputFactory(facts, facts).FromWritingSentencePreview(Ready);
        Assert.NotNull(capture);
        Assert.Equal(LearningExplanationContext.Fragment, capture.ContextKind);
        Assert.Equal(Ready.Result, capture.Context);
        Assert.NotNull(capture.OccurrenceIdentity);
    }

    private sealed class Facts : ILearningLexicalText, ILearningEvidenceSentenceTokenizer
    {
        public Func<IReadOnlyList<LearningTextRange>> Sentences { get; init; } = () => [new(0, 22)];
        public Func<IReadOnlyList<LearningDictionaryWord>> WordFacts { get; init; } = () =>
            [new("Please", new(0, 6)), new("keep", new(7, 4)), new("me", new(12, 2)), new("posted", new(15, 6))];
        public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language) => Sentences();
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language) => WordFacts();
        public string Key(string value) => throw new NotSupportedException();
        public bool IsPatternText(string value) => throw new NotSupportedException();
        public bool FirstIsUppercase(string value) => throw new NotSupportedException();
    }

    private sealed class ThrowingRanges : IReadOnlyList<LearningTextRange>
    {
        public int Count => 2;
        public LearningTextRange this[int index] => throw new InvalidOperationException("private captured text");
        public IEnumerator<LearningTextRange> GetEnumerator()
        {
            yield return new(0, 22);
            throw new InvalidOperationException("private captured text");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
