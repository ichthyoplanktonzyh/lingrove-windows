using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningTextScopePolicyTests
{
    [Fact]
    public void ExplicitSelectionWinsAndPreservesItsExactUnicodeText()
    {
        const string value = "First sentence. 请只转换这里！Another sentence.";
        const string selected = "请只转换这里";
        var range = RangeOf(value, selected);

        var resolved = LearningTextScopePolicy.ConversionRange(
            value, range, new FixtureSentenceTokenizer([]), LearningLanguage.Chinese);

        Assert.Equal(range, resolved);
        Assert.Equal(selected, LearningTextScopePolicy.Text(value, resolved));
    }

    [Fact]
    public void CaretSelectsOnlyItsSentenceAndCaretAfterTrailingSpaceUsesPreviousSentence()
    {
        const string value = "First sentence. The cursor is here! Third sentence.";
        var word = RangeOf(value, "cursor");
        var middleCaret = new LearningTextRange(word.Location + 2, 0);
        var final = "The final sentence is complete.   ";

        var tokenizer = new FixtureSentenceTokenizer(
        [
            RangeOf(value, "First sentence."),
            RangeOf(value, "The cursor is here!"),
            RangeOf(value, "Third sentence."),
        ]);
        var middle = LearningTextScopePolicy.Text(value,
            LearningTextScopePolicy.ConversionRange(value, middleCaret, tokenizer, LearningLanguage.English));
        var trailing = LearningTextScopePolicy.Text(final,
            LearningTextScopePolicy.ConversionRange(final, new LearningTextRange(final.Length, 0),
                new FixtureSentenceTokenizer([RangeOf(final, "The final sentence is complete.")]), LearningLanguage.English));

        Assert.Contains("The cursor is here!", middle, StringComparison.Ordinal);
        Assert.DoesNotContain("First sentence.", middle, StringComparison.Ordinal);
        Assert.DoesNotContain("Third sentence.", middle, StringComparison.Ordinal);
        Assert.Equal("The final sentence is complete.", trailing);
    }

    [Fact]
    public void CaretAtNextSentenceStartSelectsThatSentenceAndChineseStaysLocal()
    {
        const string value = "The previous sentence ends here.   This sentence starts here! Last sentence.";
        var next = RangeOf(value, "This sentence");
        var nextSentence = LearningTextScopePolicy.Text(value,
            LearningTextScopePolicy.ConversionRange(value, new LearningTextRange(next.Location, 0),
                new FixtureSentenceTokenizer(
                [
                    RangeOf(value, "The previous sentence ends here."),
                    RangeOf(value, "This sentence starts here!"),
                    RangeOf(value, "Last sentence."),
                ]), LearningLanguage.English));
        const string olderSentence = "Earlier English context remains in the document.";
        var before = string.Concat(Enumerable.Repeat(olderSentence + " ", 250));
        const string chinese = "我今天没有时间，可能明天再安排。";
        const string newerSentence = "Later English context remains in the document.";
        var after = string.Concat(Enumerable.Repeat(newerSentence + " ", 250));
        var mixed = before + chinese + after;
        var chineseWord = RangeOf(mixed, "可能明天");
        var local = LearningTextScopePolicy.Text(mixed, LearningTextScopePolicy.ConversionRange(
            mixed, new LearningTextRange(chineseWord.Location + 2, 0),
            new FixtureSentenceTokenizer(FindAllRanges(mixed, olderSentence)
                .Append(RangeOf(mixed, chinese))
                .Concat(FindAllRanges(mixed, newerSentence))
                .OrderBy(range => range.Location).ToArray()), LearningLanguage.Chinese));

        Assert.Contains("This sentence starts here!", nextSentence, StringComparison.Ordinal);
        Assert.DoesNotContain("The previous sentence", nextSentence, StringComparison.Ordinal);
        Assert.Contains(chinese, local, StringComparison.Ordinal);
        Assert.DoesNotContain("Earlier English", local, StringComparison.Ordinal);
        Assert.DoesNotContain("Later English", local, StringComparison.Ordinal);
    }

    [Fact]
    public void SentenceInLongFieldIsReturnedWholeWithoutArtificialWindowClipping()
    {
        const string olderSentence = "Earlier paragraph stays outside this sentence.";
        var before = string.Concat(Enumerable.Repeat(olderSentence + " ", 300));
        const string target = "The complete sentence around the caret must survive the document window.";
        const string newerSentence = "Later paragraph stays outside this sentence.";
        var after = string.Concat(Enumerable.Repeat(newerSentence + " ", 300));
        var value = before + target + " " + after;
        var word = RangeOf(value, "caret");

        var sentence = LearningTextScopePolicy.Text(value,
            LearningTextScopePolicy.ConversionRange(value, new LearningTextRange(word.Location + 2, 0),
                new FixtureSentenceTokenizer(FindAllRanges(value, olderSentence)
                    .Append(RangeOf(value, target))
                    .Concat(FindAllRanges(value, newerSentence))
                    .OrderBy(range => range.Location).ToArray()), LearningLanguage.English));

        Assert.Contains(target, sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("Earlier paragraph", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("Later paragraph", sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("👩🏽‍💻")]
    [InlineData("e\u0301")]
    public void PartialGraphemesCannotBeReadOrUsedAsCaretPositions(string grapheme)
    {
        var value = "A" + grapheme + "中文";
        var range = RangeOf(value, grapheme);
        var partial = new LearningTextRange(range.Location + 1, value.Length - range.Location - 1);

        Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.Text(value, partial));
        Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.ConversionRange(
            value, new LearningTextRange(range.Location + 1, 0), tokenizer: null!, language: LearningLanguage.English));
    }

    [Fact]
    public void TargetByteCapAcceptsBoundaryAndRejectsOneByteOverWithoutClipping()
    {
        var atLimit = new string('a', ModelContextBudget.MaximumTextBytes);
        var overLimit = new string('a', ModelContextBudget.MaximumTextBytes + 1);

        Assert.Equal(atLimit, LearningTextScopePolicy.Text(atLimit, new LearningTextRange(0, atLimit.Length)));
        var error = Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.Text(
            overLimit, new LearningTextRange(0, overLimit.Length)));
        Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);
    }

    [Fact]
    public void InvalidCaretRangesAndTextWithoutLettersFailClosed()
    {
        Assert.Equal(LearningTranslationFailure.InvalidCaretRange,
            Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.ConversionRange(
                "A sentence.", new LearningTextRange(int.MaxValue, 1), tokenizer: null!, language: LearningLanguage.English)).Failure);
        Assert.Equal(LearningTranslationFailure.NoSentenceAtCaret,
            Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.ConversionRange(
                "   ", new LearningTextRange(1, 0), new FixtureSentenceTokenizer([]), LearningLanguage.English)).Failure);
        Assert.Equal(LearningTranslationFailure.NoConvertibleText,
            Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.Text(
                "😀!?", new LearningTextRange(0, 4))).Failure);
        Assert.Equal(LearningTranslationFailure.InvalidSourceRange,
            Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.Text(
                "A\uD800 sentence.", new LearningTextRange(0, 2))).Failure);
    }

    [Fact]
    public void TokenizerOwnsSentenceBoundariesInsteadOfSplittingPeriodsInNamesOrNumbers()
    {
        const string value = "Dr. Jones paid $3.14. Now move on.";
        var first = "Dr. Jones paid $3.14.";
        var later = "Now move on.";
        var number = RangeOf(value, "3.14");
        var tokenizer = new FixtureSentenceTokenizer([RangeOf(value, first), RangeOf(value, later)]);

        var range = LearningTextScopePolicy.ConversionRange(value,
            new LearningTextRange(number.Location + 1, 0), tokenizer, LearningLanguage.English);

        Assert.Equal(RangeOf(value, first), range);
        Assert.Equal(first, LearningTextScopePolicy.Text(value, range));
    }

    [Fact]
    public void UnavailableTokenizerFailsWithAFixedMessageAndNeverFallsBackToPunctuation()
    {
        var value = "A complete sentence.";
        var word = RangeOf(value, "complete");
        var tokenizer = new ThrowingSentenceTokenizer();

        var error = Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.ConversionRange(
            value, new LearningTextRange(word.Location, 0), tokenizer, LearningLanguage.English));

        Assert.Equal(LearningTranslationFailure.SentenceTokenizerUnavailable, error.Failure);
        Assert.DoesNotContain("host", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaretTokenizerReceivesMacSizedWindowExpandedToWholeGraphemes()
    {
        const string family = "👩🏽‍💻";
        const int expectedStart = 32_767;
        const int secondFamilyStart = 98_302;
        const int caret = 65_537;
        var value = string.Concat(
            new string('a', expectedStart),
            family,
            new string('a', secondFamilyStart - expectedStart - family.Length),
            family,
            new string('a', 2_000));
        var localCaret = caret - expectedStart;
        var localSentence = new LearningTextRange(localCaret - 5, 10);
        var tokenizer = new FixtureSentenceTokenizer([localSentence]);

        var result = LearningTextScopePolicy.ConversionRange(
            value, new LearningTextRange(caret, 0), tokenizer, LearningLanguage.English);

        Assert.NotNull(tokenizer.CapturedValue);
        var capturedValue = tokenizer.CapturedValue!;
        Assert.StartsWith(family, capturedValue, StringComparison.Ordinal);
        Assert.EndsWith(family, capturedValue, StringComparison.Ordinal);
        Assert.Equal(secondFamilyStart + family.Length - expectedStart, capturedValue.Length);
        Assert.Equal(new LearningTextRange(expectedStart + localSentence.Location, localSentence.Length), result);
        Assert.Equal(LearningLanguage.English, tokenizer.CapturedLanguage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SentenceTouchingAnArtificialNeighborhoodEdgeIsRejectedWhole(bool touchesLeftEdge)
    {
        var value = new string('a', 100_000);
        const int caret = 50_000;
        const int windowLength = 65_536;
        const int localCaret = 32_768;
        var incompleteSentence = touchesLeftEdge
            ? new LearningTextRange(0, localCaret + 1)
            : new LearningTextRange(localCaret - 1, windowLength - localCaret + 1);
        var tokenizer = new FixtureSentenceTokenizer([incompleteSentence]);

        var error = Assert.Throws<LearningTranslationException>(() => LearningTextScopePolicy.ConversionRange(
            value, new LearningTextRange(caret, 0), tokenizer, LearningLanguage.English));

        Assert.Equal(LearningTranslationFailure.SentenceBeyondVerifiedRange, error.Failure);
        Assert.Equal(windowLength, tokenizer.CapturedValue?.Length ?? -1);
        Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
    }

    private static LearningTextRange RangeOf(string value, string text)
    {
        var location = value.IndexOf(text, StringComparison.Ordinal);
        Assert.True(location >= 0);
        return new LearningTextRange(location, text.Length);
    }

    private static IReadOnlyList<LearningTextRange> FindAllRanges(string value, string text)
    {
        var ranges = new List<LearningTextRange>();
        var start = 0;
        while (start < value.Length)
        {
            var location = value.IndexOf(text, start, StringComparison.Ordinal);
            if (location < 0) break;
            ranges.Add(new LearningTextRange(location, text.Length));
            start = location + text.Length;
        }

        return ranges;
    }

    private sealed class FixtureSentenceTokenizer(IReadOnlyList<LearningTextRange> ranges) : ILearningSentenceTokenizer
    {
        public string? CapturedValue { get; private set; }

        public LearningLanguage? CapturedLanguage { get; private set; }

        public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language)
        {
            CapturedValue = value;
            CapturedLanguage = language;
            return ranges;
        }
    }

    private sealed class ThrowingSentenceTokenizer : ILearningSentenceTokenizer
    {
        public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language) =>
            throw new InvalidOperationException("host adapter detail");
    }
}
