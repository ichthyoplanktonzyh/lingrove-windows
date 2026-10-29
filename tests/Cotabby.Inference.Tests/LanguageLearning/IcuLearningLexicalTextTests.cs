using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Tests.Parity;
using Cotabby.Win32.Text;

namespace Cotabby.Inference.Tests.LanguageLearning;

public sealed class IcuLearningLexicalTextTests
{
    [Fact]
    public void UnknownLanguageUsesRootWordIteratorWithoutInventingALanguage()
    {
        var api = new LocaleApi(); var text = new IcuLearningLexicalText(api);
        Assert.Equal("bank", Assert.Single(text.WordsUnknown("bank")).Text);
        Assert.Equal("", api.Locale);
        Assert.Equal(new[] { "hello", "world" }, new IcuLearningLexicalText().WordsUnknown("hello world").Select(word => word.Text));
    }
    public static IEnumerable<object[]> CaseIds() => LearningLexicalCorpus.Cases().Select(row => new object[] { LearningLexicalCorpus.Text(row, "id") });

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void UnicodeFactsMatchMacAndSelectedWordCasesMatchItsTokenizer(string id)
    {
        var row = LearningLexicalCorpus.Cases().Single(row => LearningLexicalCorpus.Text(row, "id") == id);
        var text = new IcuLearningLexicalText();
        var expected = row.GetProperty("expected");
        foreach (var fact in expected.GetProperty("facts").EnumerateArray())
        {
            var value = LearningLexicalCorpus.Text(fact, "text");
            Assert.Equal(LearningLexicalCorpus.Text(fact, "key"), text.Key(value));
            Assert.Equal(fact.GetProperty("patternText").GetBoolean(), text.IsPatternText(value));
            Assert.Equal(fact.GetProperty("firstUppercase").GetBoolean(), text.FirstIsUppercase(value));
        }
        var source = LearningLexicalCorpus.Text(row, "text");
        var words = text.Words(source, LearningLexicalCorpus.Language(row));
        foreach (var word in words)
            Assert.Equal(word.Text, source.Substring(word.Range.Location, word.Range.Length));
        if (row.GetProperty("nativeWordParity").GetBoolean())
        {
            Assert.Equal(expected.GetProperty("words").EnumerateArray().Select(LearningLexicalCorpus.Word), words);
            LearningLexicalCorpus.AssertRecognition(row, text);
        }
    }

    [Fact]
    public void MissingIcuFailsClosedWithFixedTextAndNoHeuristic()
    {
        var text = new IcuLearningLexicalText(new UnavailableApi());
        Action[] operations = [() => text.Key("private source"),
            () => text.Words("private source", LearningLanguage.English),
            () => text.IsPatternText("private source"), () => text.FirstIsUppercase("Private")];
        foreach (var operation in operations)
        {
            var failure = Assert.Throws<LearningLexicalException>(operation);
            Assert.Equal(LearningLexicalFailure.TextProviderUnavailable, failure.Failure);
            Assert.DoesNotContain("private", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EmptyValuesNeedNoNativeWork()
    {
        var text = new IcuLearningLexicalText(new UnavailableApi());
        Assert.Equal("", text.Key(""));
        Assert.Empty(text.Words("", LearningLanguage.English));
        Assert.True(text.IsPatternText(""));
        Assert.False(text.FirstIsUppercase(""));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 999)]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    public void BadOrIncompleteNativeRangesCannotBecomeWordAnchors(int location, int length)
    {
        var text = new IcuLearningLexicalText(new RangeApi([new LearningTextRange(location, length)]));
        var failure = Assert.Throws<LearningLexicalException>(() => text.Words("😀a", LearningLanguage.English));
        Assert.Equal(LearningLexicalFailure.InvalidWordRanges, failure.Failure);
    }

    [Fact]
    public void MalformedUtf16AndUnknownLanguagesAreRejectedBeforeNativeWork()
    {
        var text = new IcuLearningLexicalText(new UnavailableApi());
        Assert.Equal(LearningLexicalFailure.InvalidInput,
            Assert.Throws<LearningLexicalException>(() => text.Key("a\ud800")).Failure);
        Assert.Equal(LearningLexicalFailure.InvalidInput,
            Assert.Throws<LearningLexicalException>(() => text.Words("word", (LearningLanguage)999)).Failure);
    }

    private sealed class UnavailableApi : IIcuLexicalApi
    {
        public string ChangeCase(string value, bool upper) => throw new EntryPointNotFoundException("private source");
        public IReadOnlyList<LearningTextRange> WordRanges(string value, string locale) => throw new DllNotFoundException("private source");
        public bool HasBinaryProperty(int scalar, int property) => throw new EntryPointNotFoundException("private source");
    }
    private sealed class RangeApi(IReadOnlyList<LearningTextRange> ranges) : IIcuLexicalApi
    {
        public string ChangeCase(string value, bool upper) => value;
        public IReadOnlyList<LearningTextRange> WordRanges(string value, string locale) => ranges;
        public bool HasBinaryProperty(int scalar, int property) => true;
    }
    private sealed class LocaleApi : IIcuLexicalApi
    {
        public string? Locale;
        public string ChangeCase(string value, bool upper) => value;
        public IReadOnlyList<LearningTextRange> WordRanges(string value, string locale) { Locale = locale; return [new(0, value.Length)]; }
        public bool HasBinaryProperty(int scalar, int property) => true;
    }
}
