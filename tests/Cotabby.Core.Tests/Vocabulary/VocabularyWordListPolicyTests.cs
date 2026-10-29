using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyWordListPolicyTests
{
    private static VocabularyWordList Table(string text, string suffix = "csv") => VocabularyWordListPolicy.Decode(Encoding.UTF8.GetBytes(text), suffix);
    private static IReadOnlyList<VocabularyEntryInput> Inputs(VocabularyWordList table) => VocabularyWordListPolicy.Inputs(table, VocabularyWordListPolicy.Configuration(table, LearningLanguage.English));

    [Fact]
    public void QuotedCsvPreservesUnicodeQuotesNewlinesAndPhysicalLines()
    {
        var table = Table("\ufeffexpression,meaning,notes\r\n\"keep me posted\",\"告诉我，最新进展\",\"say \"\"hello\"\"\r\nsecond line\"\r\nbank,河岸,\r\n");
        var inputs = Inputs(table);
        Assert.Equal(new[] { 1, 2, 4 }, table.Rows.Select(r => r.Line));
        Assert.Equal("告诉我，最新进展", inputs[0].Meaning);
        Assert.Equal("say \"hello\"\r\nsecond line", inputs[0].Note);
    }

    [Fact]
    public void HeaderlessAndExplicitMappingKeepFirstRow()
    {
        var table = Table("hola,a greeting\nadiós,a farewell");
        var configuration = VocabularyWordListPolicy.Configuration(table, LearningLanguage.Spanish);
        Assert.False(configuration.HasHeader);
        Assert.Equal(new[] { "hola", "adiós" }, VocabularyWordListPolicy.Inputs(table, configuration).Select(x => x.Expression));
        configuration = configuration with { Columns = new Dictionary<VocabularyImportField, int> { [VocabularyImportField.Expression] = 1, [VocabularyImportField.Meaning] = 0 } };
        Assert.Equal("a greeting", VocabularyWordListPolicy.Inputs(table, configuration)[0].Expression);
    }

    [Fact]
    public void ChineseHeadersAndLanguageTypeAndTagsRetainRowIntent()
    {
        var table = Table("单词,释义,语言,类型,释义语言,标签\nbank,河岸,en,word,zh-Hans,work; water\nkeep me posted,让我知道,en,expression,zh-Hans,work");
        var inputs = Inputs(table);
        Assert.Equal(LearningLanguage.English, inputs[0].Language);
        Assert.Equal(LearningLanguage.Chinese, inputs[0].MeaningLanguage);
        Assert.Equal(new[] { "water", "work" }, inputs[0].Tags);
        Assert.Equal(VocabularyEntryKind.Expression, inputs[1].Kind);
    }

    [Fact]
    public void ExampleColumnMakesAmbiguousTranslationAnExampleTranslation()
    {
        var table = Table("expression,meaning,example,translation\nbank,river edge,The bank is steep.,河岸很陡。");
        var input = Assert.Single(Inputs(table));
        Assert.Equal("river edge", input.Meaning); Assert.Equal("河岸很陡。", input.ExampleTranslation);
        Assert.Equal("word meaning", Assert.Single(Inputs(Table("expression,translation\nbank,word meaning"))).Meaning);
    }

    [Fact]
    public void Utf16TextUsesOneExpressionPerLineAndNoFormulaExecution()
    {
        byte[] data = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("  hello  \r\n\r\nkeep me posted\ncomma, stays literal\n👩🏽‍💻\n=1+1")).ToArray();
        var table = VocabularyWordListPolicy.Decode(data, "TXT");
        Assert.Equal(new[] { 1, 3, 4, 5, 6 }, table.Rows.Select(r => r.Line));
        Assert.Equal(new[] { "hello", "keep me posted", "comma, stays literal", "👩🏽‍💻", "=1+1" }, Inputs(table).Select(i => i.Expression));
    }

    [Fact]
    public void FoundationLineTerminatorsRetainPhysicalLineNumbers()
    {
        var table = Table("one\u0085\u2028two\u2029three\r\nfour", "txt");
        Assert.Equal(new[] { 1, 3, 4, 5 }, table.Rows.Select(r => r.Line));
        Assert.Equal(new[] { "one", "two", "three", "four" }, Inputs(table).Select(x => x.Expression));
    }
    [Theory]
    [InlineData("expression,meaning\n\"unterminated")]
    [InlineData("word,\"closed\"oops")]
    [InlineData("un\"escaped,meaning")]
    public void MalformedQuotingRejectsWholeFile(string value) => Assert.Equal(VocabularyWordListError.MalformedCsv, Assert.Throws<VocabularyWordListException>(() => Table(value)).Error);

    [Fact]
    public void InvalidRowsMappingsAndLimitsFailBeforeInput()
    {
        var table = Table("expression,meaning,language\n\"two\nlines\",meaning,en\n,missing,en");
        var config = VocabularyWordListPolicy.Configuration(table, LearningLanguage.English);
        var missing = Assert.Throws<VocabularyWordListException>(() => VocabularyWordListPolicy.Inputs(table, config));
        Assert.Equal(VocabularyWordListError.MissingExpression, missing.Error); Assert.Equal(4, missing.Line);
        var invalid = config with { Columns = new Dictionary<VocabularyImportField, int> { [VocabularyImportField.Expression] = -1 } };
        Assert.Equal(VocabularyWordListError.Mapping, Assert.Throws<VocabularyWordListException>(() => VocabularyWordListPolicy.Inputs(table, invalid)).Error);
        foreach (var source in new[] { "expression,language\nhello,unsupported", "expression,type\nhello,1", "expression,example,translation\nhello,,bonjour" })
            Assert.Throws<VocabularyWordListException>(() => Inputs(Table(source)));
        Assert.Equal(VocabularyWordListError.Encoding, Assert.Throws<VocabularyWordListException>(() => VocabularyWordListPolicy.Decode([0xff], "txt")).Error);
        Assert.Equal(VocabularyWordListError.TooLarge, Assert.Throws<VocabularyWordListException>(() => VocabularyWordListPolicy.Decode(new byte[VocabularyWordListPolicy.MaximumBytes + 1], "txt")).Error);
        Assert.Throws<VocabularyWordListException>(() => Table(string.Concat(Enumerable.Repeat("a\n", 10_001)), "txt"));
        Assert.Throws<VocabularyWordListException>(() => Table(string.Concat(Enumerable.Repeat("a,", 64)) + "a"));
        Assert.Throws<VocabularyWordListException>(() => Table("word", "json"));
        Assert.Throws<VocabularyWordListException>(() => Table("\n \n", "txt"));
    }

    [Fact]
    public void ValidationNormalizesAndBoundsWithoutAssigningIdentity()
    {
        var value = VocabularyEntryInputPolicy.Validated(new("  cafe\u0301  au   lait  ", Tags: ["z", " a ", "z", ""]));
        Assert.Equal("café au lait", value.Expression); Assert.Equal(value.Expression, value.DictionaryForm);
        Assert.Equal(new[] { "a", "z" }, value.Tags);
        Assert.Equal(new[] { "\ue000", "😀" }, VocabularyEntryInputPolicy.Validated(new("word", Tags: ["😀", "\ue000"])).Tags);
        Assert.Equal("meaning", VocabularyEntryInputPolicy.Validated(new("word", Meaning: "\u200bmeaning\u200b")).Meaning);
        Assert.Equal(VocabularyWordListError.TranslationWithoutExample, Assert.Throws<VocabularyWordListException>(() => VocabularyEntryInputPolicy.Validated(new("word", ExampleTranslation: "translation"))).Error);
        Assert.Throws<VocabularyWordListException>(() => VocabularyEntryInputPolicy.Validated(new("word", Note: new string('a', 262_145))));
        Assert.Throws<VocabularyWordListException>(() => VocabularyEntryInputPolicy.Validated(new("word", Tags: Enumerable.Range(0, 65).Select(i => i.ToString()).ToArray())));
        Assert.Throws<VocabularyWordListException>(() => VocabularyEntryInputPolicy.Validated(new("word\0")));
    }

    [Fact]
    public void CancellationIsObservedDuringParseAndMapping()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => VocabularyWordListPolicy.Decode(Encoding.UTF8.GetBytes("word"), "csv", cancelled.Token));
        var table = Table("hello");
        Assert.Throws<OperationCanceledException>(() => VocabularyWordListPolicy.Inputs(table, VocabularyWordListPolicy.Configuration(table, LearningLanguage.English), cancelled.Token));
    }
}
