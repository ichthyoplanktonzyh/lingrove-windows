using System.Globalization;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningSourceLanguageResolverTests
{
    [Theory]
    [InlineData("你好", LearningLanguage.Chinese, LearningLanguage.English)]
    [InlineData("Hi", LearningLanguage.English, LearningLanguage.Spanish)]
    [InlineData("Hola", LearningLanguage.Spanish, LearningLanguage.English)]
    [InlineData("Привет", LearningLanguage.Russian, LearningLanguage.English)]
    [InlineData("ありがとう", LearningLanguage.Japanese, LearningLanguage.English)]
    public void OneExplicitSourceWinsEvenForShortTextWithoutCallingDetector(
        string text, LearningLanguage source, LearningLanguage target)
    {
        var detector = new FakeLanguageDetector(throwOnCall: new InvalidOperationException("should not run"));

        var resolved = LearningSourceLanguageResolver.Resolve(text, [source], target, detector);

        Assert.Equal(source, resolved);
        Assert.Equal(0, detector.CallCount);
    }

    [Fact]
    public void TrimmingAndDuplicateSingleChoicePreserveTheExplicitDirection()
    {
        var detector = new FakeLanguageDetector(throwOnCall: new InvalidOperationException("should not run"));

        var resolved = LearningSourceLanguageResolver.Resolve(
            "  你好  ", [LearningLanguage.Chinese, LearningLanguage.Chinese], LearningLanguage.English, detector);

        Assert.Equal(LearningLanguage.Chinese, resolved);
        Assert.Equal(0, detector.CallCount);
    }

    [Fact]
    public void FiltersTargetAndDuplicatesWhilePreservingEnabledSourceOrder()
    {
        var detector = new FakeLanguageDetector(new Dictionary<LearningLanguage, double>
        {
            [LearningLanguage.English] = 0.20,
            [LearningLanguage.Chinese] = 0.80,
            [LearningLanguage.French] = 0.99,
        });

        var resolved = LearningSourceLanguageResolver.Resolve(
            "你好", [LearningLanguage.English, LearningLanguage.French, LearningLanguage.Chinese,
                LearningLanguage.English, (LearningLanguage)999],
            LearningLanguage.French, detector);

        Assert.Equal(LearningLanguage.Chinese, resolved);
        Assert.Equal(new[] { LearningLanguage.English, LearningLanguage.Chinese }, detector.CapturedCandidates!);
    }

    [Fact]
    public void MultiSourceDetectionUsesTheLastSixHundredGraphemeClusters()
    {
        const string family = "👩🏽‍💻";
        var input = " \n" + new string('p', 20) + new string('x', 599) + family + "z \t ";
        var detector = new FakeLanguageDetector(new Dictionary<LearningLanguage, double>
        {
            [LearningLanguage.English] = 0.81,
            [LearningLanguage.Chinese] = 0.12,
        });

        var resolved = LearningSourceLanguageResolver.Resolve(
            input, [LearningLanguage.Chinese, LearningLanguage.English], LearningLanguage.French, detector);

        Assert.Equal(LearningLanguage.English, resolved);
        Assert.Equal(new string('x', 598) + family + "z", detector.CapturedSample);
        Assert.Equal(600, StringInfo.ParseCombiningCharacters(detector.CapturedSample!).Length);
        Assert.Equal(new[] { LearningLanguage.Chinese, LearningLanguage.English }, detector.CapturedCandidates!);
    }

    [Fact]
    public void MissingCandidateScoresBecomeZeroConfidenceAlternatives()
    {
        var detector = new FakeLanguageDetector(new Dictionary<LearningLanguage, double>
        {
            [LearningLanguage.Chinese] = 0.70,
        });

        var resolved = LearningSourceLanguageResolver.Resolve(
            "你好", [LearningLanguage.Chinese, LearningLanguage.English], LearningLanguage.French, detector);

        Assert.Equal(LearningLanguage.Chinese, resolved);
    }

    [Fact]
    public void BlankTextLettersOnlyAndNoEligibleSourcesFailClosedBeforeRecognition()
    {
        var detector = new FakeLanguageDetector(new Dictionary<LearningLanguage, double>());

        Assert.Null(LearningSourceLanguageResolver.Resolve("  \n\t  ", [LearningLanguage.Chinese], LearningLanguage.English, detector));
        Assert.Null(LearningSourceLanguageResolver.Resolve("123 !!!", [LearningLanguage.Chinese], LearningLanguage.English, detector));
        Assert.Null(LearningSourceLanguageResolver.Resolve("hello\uD800", [LearningLanguage.English, LearningLanguage.Chinese], LearningLanguage.French, detector));
        Assert.Null(LearningSourceLanguageResolver.Resolve("hello", [], LearningLanguage.English, detector));
        Assert.Null(LearningSourceLanguageResolver.Resolve("hello", [LearningLanguage.English], LearningLanguage.English, detector));
        Assert.Equal(0, detector.CallCount);
    }

    [Fact]
    public void ConfidenceFloorMarginTiesAndNonfiniteValuesMatchMacRanking()
    {
        Assert.Equal(LearningLanguage.Chinese,
            LearningSourceLanguageResolver.ConfidentLanguage(new Dictionary<LearningLanguage, double>
            {
                [LearningLanguage.Chinese] = 0.75,
                [LearningLanguage.English] = 0.60,
            }));
        Assert.Null(LearningSourceLanguageResolver.ConfidentLanguage(new Dictionary<LearningLanguage, double>
        {
            [LearningLanguage.Chinese] = 0.549,
            [LearningLanguage.English] = 0.10,
        }));
        Assert.Null(LearningSourceLanguageResolver.ConfidentLanguage(new Dictionary<LearningLanguage, double>
        {
            [LearningLanguage.Chinese] = 0.70,
            [LearningLanguage.English] = 0.56,
        }));
        Assert.Null(LearningSourceLanguageResolver.ConfidentLanguage(new Dictionary<LearningLanguage, double>
        {
            [LearningLanguage.Chinese] = 0.70,
            [LearningLanguage.English] = 0.70,
        }));
        Assert.Equal(LearningLanguage.Chinese,
            LearningSourceLanguageResolver.ConfidentLanguage(new Dictionary<LearningLanguage, double>
            {
                [LearningLanguage.Chinese] = 0.8,
                [LearningLanguage.English] = double.NaN,
            }));
        Assert.Null(LearningSourceLanguageResolver.ConfidentLanguage(new Dictionary<LearningLanguage, double>()));
    }

    [Fact]
    public void DetectorFailureReturnsUnknownAndCancellationStillPropagates()
    {
        var failure = new FakeLanguageDetector(throwOnCall: new InvalidOperationException("host details"));
        var cancelled = new FakeLanguageDetector(throwOnCall: new OperationCanceledException());

        Assert.Null(LearningSourceLanguageResolver.Resolve(
            "hello", [LearningLanguage.English, LearningLanguage.Chinese], LearningLanguage.French, failure));
        Assert.Throws<OperationCanceledException>(() => LearningSourceLanguageResolver.Resolve(
            "hello", [LearningLanguage.English, LearningLanguage.Chinese], LearningLanguage.French, cancelled));
        Assert.Equal(1, failure.CallCount);
        Assert.Equal(1, cancelled.CallCount);
    }

    private sealed class FakeLanguageDetector(
        IReadOnlyDictionary<LearningLanguage, double>? scores = null,
        Exception? throwOnCall = null) : ILearningLanguageDetector
    {
        public string? CapturedSample { get; private set; }

        public IReadOnlyList<LearningLanguage>? CapturedCandidates { get; private set; }

        public int CallCount { get; private set; }

        public IReadOnlyDictionary<LearningLanguage, double> Scores(
            string sample,
            IReadOnlyList<LearningLanguage> candidates)
        {
            CallCount++;
            CapturedSample = sample;
            CapturedCandidates = candidates.ToArray();
            if (throwOnCall is not null) throw throwOnCall;
            return scores ?? new Dictionary<LearningLanguage, double>();
        }
    }
}
