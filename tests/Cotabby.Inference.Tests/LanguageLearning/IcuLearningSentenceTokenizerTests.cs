using Cotabby.Core.LanguageLearning;
using Cotabby.Win32.Text;

namespace Cotabby.Inference.Tests.LanguageLearning;

public sealed class IcuLearningSentenceTokenizerTests
{
    private readonly IcuLearningSentenceTokenizer _tokenizer = new();

    [Fact]
    public void EnglishSentenceBreaks_KeepAbbreviationsDecimalsAndUrlsTogether()
    {
        const string first = "Mr. Weston paid 3.14 dollars at https://example.com/page before lunch.";
        const string second = "Then follow up tomorrow.";
        var value = first + "   " + second;

        var ranges = _tokenizer.Tokenize(value, LearningLanguage.English);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(first, Slice(value, ranges[0]));
        Assert.Equal(second, Slice(value, ranges[1]));
    }

    [Fact]
    public void ChineseSourceLanguage_UsesCjkSentenceTerminators()
    {
        const string first = "我今天没有时间，可能明天再安排。";
        const string second = "请稍后再联系！";
        var value = first + second;

        var ranges = _tokenizer.Tokenize(value, LearningLanguage.Chinese);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(first, Slice(value, ranges[0]));
        Assert.Equal(second, Slice(value, ranges[1]));
    }

    [Fact]
    public void SentenceRanges_AreUtf16OffsetsAndDoNotSplitSupplementaryGraphemes()
    {
        const string emoji = "👩🏽‍💻";
        var value = $"We saw {emoji} today. Next sentence.";

        var ranges = _tokenizer.Tokenize(value, LearningLanguage.English);

        Assert.Equal(2, ranges.Count);
        Assert.Equal($"We saw {emoji} today.", Slice(value, ranges[0]));
        Assert.Equal("Next sentence.", Slice(value, ranges[1]));
        Assert.Equal(value.IndexOf("Next sentence.", StringComparison.Ordinal), ranges[1].Location);
        Assert.All(ranges, range =>
        {
            Assert.InRange(range.Location, 0, value.Length);
            Assert.InRange(range.End, range.Location + 1, value.Length);
            Assert.False(char.IsLowSurrogate(value[range.Location]));
            Assert.False(char.IsHighSurrogate(value[range.End - 1]));
        });
    }

    [Fact]
    public void LocaleMapping_UsesSelectedLanguageAndICUAbbreviationFilters()
    {
        var segmenter = new RecordingSegmenter();
        var tokenizer = new IcuLearningSentenceTokenizer(segmenter);

        tokenizer.Tokenize("A sentence.", LearningLanguage.English);
        Assert.Equal("en@ss=standard", segmenter.Locale);

        tokenizer.Tokenize("一文。", LearningLanguage.TraditionalChinese);
        Assert.Equal("zh_Hant", segmenter.Locale);
    }

    [Fact]
    public void MissingIcu_IsReportedWithAFixedManualSelectionMessageAndNoFallback()
    {
        var tokenizer = new IcuLearningSentenceTokenizer(new MissingIcuSegmenter());

        var error = Assert.Throws<LearningTranslationException>(
            () => tokenizer.Tokenize("Mr. Weston arrived.", LearningLanguage.English));

        Assert.Equal(LearningTranslationFailure.SentenceTokenizerUnavailable, error.Failure);
        Assert.Equal("Sentence boundaries are unavailable for this field. Select text manually.", error.Message);
    }

    [Fact]
    public void InvalidLanguage_IsRejectedBeforeCallingNativeSegmentation()
    {
        var segmenter = new RecordingSegmenter();
        var tokenizer = new IcuLearningSentenceTokenizer(segmenter);

        var error = Assert.Throws<LearningTranslationException>(
            () => tokenizer.Tokenize("A sentence.", (LearningLanguage)int.MaxValue));

        Assert.Equal(LearningTranslationFailure.InvalidOptions, error.Failure);
        Assert.Null(segmenter.Locale);
    }

    private static string Slice(string value, LearningTextRange range) =>
        value.Substring(range.Location, range.Length);

    private sealed class RecordingSegmenter : IIcuSentenceSegmenter
    {
        public string? Locale { get; private set; }

        public IReadOnlyList<LearningTextRange> Segment(string value, string locale)
        {
            Locale = locale;
            return Array.Empty<LearningTextRange>();
        }
    }

    private sealed class MissingIcuSegmenter : IIcuSentenceSegmenter
    {
        public IReadOnlyList<LearningTextRange> Segment(string value, string locale) =>
            throw new DllNotFoundException("Private system loader details should not escape.");
    }
}
