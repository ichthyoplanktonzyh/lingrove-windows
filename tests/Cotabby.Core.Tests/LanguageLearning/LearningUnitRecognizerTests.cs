using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningUnitRecognizerTests
{
    [Theory]
    [InlineData(-1, 1, "x")]
    [InlineData(0, 99, "x")]
    [InlineData(1, 1, "x")]
    [InlineData(0, 2, "different")]
    public void UntrustedWordRangesCannotBecomeClickTargets(int location, int length, string word)
    {
        var text = new WordProvider([new LearningDictionaryWord(word, new(location, length))]);
        var recognizer = new LearningUnitRecognizer([], text);
        var failure = Assert.Throws<LearningLexicalException>(() => recognizer.Recognize("😀x", LearningLanguage.English));
        Assert.Equal(LearningLexicalFailure.InvalidWordRanges, failure.Failure);
    }

    [Fact]
    public void OverlappingWordsAndBrokenGraphemeEdgesAreRejected()
    {
        var overlap = new LearningUnitRecognizer([], new WordProvider([
            new("a", new(0, 1)), new("a", new(0, 1))]));
        Assert.Throws<LearningLexicalException>(() => overlap.Recognize("aa", LearningLanguage.English));
        var combining = new LearningUnitRecognizer([], new WordProvider([new("e", new(0, 1))]));
        Assert.Throws<LearningLexicalException>(() => combining.Recognize("e\u0301", LearningLanguage.English));
    }

    [Fact]
    public void DictionarySnapshotsDetachCallerCollectionsAndRetainAttribution()
    {
        var forms = new List<string> { "explicit form" };
        var definitions = new List<string> { "source meaning" };
        var labels = new List<string> { "formal" };
        var sense = new LearningLexiconSense("verb", definitions, labels);
        var senses = new List<LearningLexiconSense> { sense };
        var phonetics = new List<string> { "source phonetics" };
        var entry = new LearningLexiconEntry("stable-id", "en", "headword", forms, senses,
            "https://example.invalid/source", phonetics: phonetics,
            sourceName: "Fixture source", licenseUrl: "https://example.invalid/license", licenseName: "Fixture license");
        forms.Clear(); definitions.Clear(); labels.Clear(); senses.Clear(); phonetics.Clear();

        Assert.Equal(["explicit form"], entry.Forms);
        Assert.Equal(["source meaning"], Assert.Single(entry.Senses).Definitions);
        Assert.Equal(["formal"], sense.Labels);
        Assert.Equal(["source phonetics"], entry.Phonetics);
        Assert.Equal("stable-id", entry.Id);
        Assert.Equal("Fixture source", entry.SourceName);
        Assert.Equal("Fixture license", entry.LicenseName);
        Assert.Equal("https://example.invalid/source", entry.SourceUrl);
        Assert.Equal("https://example.invalid/license", entry.LicenseUrl);
    }

    [Fact]
    public void IndependentLexicalSnapshotsHaveOrderedStructuralEquality()
    {
        LearningLexiconEntry Entry(string[] definitions, string[]? examples = null) =>
            new("source-id", "en", "form", ["forms"],
                [new LearningLexiconSense("noun", definitions, ["label"])], "source", examples: examples);
        var first = Entry(["meaning one", "meaning two"], ["example"]);
        var same = Entry(["meaning one", "meaning two"], ["example"]);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.True(first.Senses[0] == same.Senses[0]);
        Assert.Equal(first.Senses[0].GetHashCode(), same.Senses[0].GetHashCode());
        Assert.True(first != Entry(["meaning two", "meaning one"], ["example"]));
        Assert.True(Entry(["meaning"]) != Entry(["meaning"], []));

        LearningUnit Unit(int location) => new(1, "surface", "query", LearningUnitKind.Expression,
            [new LearningTextRange(location, 7)], [new("surface", new(location, 7))], "source-id");
        var occurrence = Unit(2); var copy = Unit(2);
        Assert.True(occurrence == copy);
        Assert.Equal(occurrence.GetHashCode(), copy.GetHashCode());
        Assert.True(occurrence != Unit(3));
        Assert.Single(new HashSet<LearningUnit> { occurrence, copy });
    }

    [Fact]
    public void CanonicallyEquivalentStringsCompareAsValuesWithoutChangingOriginalText()
    {
        const string composed = "café", decomposed = "cafe\u0301";
        var first = new LearningDictionaryWord(composed, new(0, 5));
        var second = new LearningDictionaryWord(decomposed, new(0, 5));
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(5, second.Text.Length);
        LearningLexiconEntry Entry(string value) => new(value, "fr", value, [value],
            [new(value, [value], [value])], value, [value], [value], value, value, value);
        var entry = Entry(composed); var same = Entry(decomposed);
        Assert.True(entry == same);
        Assert.Equal(entry.GetHashCode(), same.GetHashCode());
        var unit = new LearningUnit(0, composed, composed, LearningUnitKind.Word, [first.Range], [first], composed);
        var sameUnit = new LearningUnit(0, decomposed, decomposed, LearningUnitKind.Word, [second.Range], [second], decomposed);
        Assert.True(unit == sameUnit);
        Assert.Equal(unit.GetHashCode(), sameUnit.GetHashCode());
        Assert.Equal(decomposed, sameUnit.Surface);
        Assert.True(new LearningDictionaryWord("\ud800", new(0, 1)) == new LearningDictionaryWord("\ud800", new(0, 1)));
    }

    private sealed class WordProvider(IReadOnlyList<LearningDictionaryWord> words) : ILearningLexicalText
    {
        public string Key(string value) => value;
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language) => words;
        public bool IsPatternText(string value) => true;
        public bool FirstIsUppercase(string value) => false;
    }
}
