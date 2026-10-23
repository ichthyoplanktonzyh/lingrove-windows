using System.Globalization;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Text;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyCaptureTests
{
    private static readonly VocabularyExample Usage = new(Guid.NewGuid(), "Please keep me posted.", "请随时告诉我。",
        [new(7, 4), new(15, 6)], VocabularyExampleCompleteness.Sentence, "持续通知", DateTimeOffset.UnixEpoch,
        LearningLanguage.Chinese, [new("Mantenme informado.", LearningLanguage.Spanish)],
        LearningLanguage.Chinese, VocabularyMeaningOrigin.User, "fixture.app", [Record]);
    private static LearningExplanationRecord Record => new(Guid.Parse("11111111-1111-1111-1111-111111111111"),
        new("keep someone posted", "keep someone posted", LearningLanguage.English, VocabularyEntryKind.Pattern,
            "Please keep me posted.", [new(7, 4), new(15, 6)]), LearningLanguage.Chinese,
        new("持续通知", "An annotation."), "fixture", DateTimeOffset.UnixEpoch);

    [Fact]
    public void EditsRetainUsageIdAndExplicitCopyChangesOnlyId()
    {
        var edited = Usage with { Meaning = "Edited", Translation = "Edited translation" };
        Assert.Equal(Usage.Id, edited.Id); Assert.Equal("持续通知", Usage.Meaning);
        var copy = Usage.CopiedWithNewId();
        Assert.NotEqual(Usage.Id, copy.Id); Assert.NotEqual(Guid.Empty, copy.Id);
        Assert.Equal(Usage.Text, copy.Text); Assert.Equal(Usage.Translation, copy.Translation);
        Assert.Equal(Usage.TranslationLanguage, copy.TranslationLanguage); Assert.Equal(Usage.TranslationVariants, copy.TranslationVariants);
        Assert.Equal(Usage.Ranges, copy.Ranges); Assert.Equal(Usage.Completeness, copy.Completeness);
        Assert.Equal(Usage.Meaning, copy.Meaning); Assert.Equal(Usage.MeaningLanguage, copy.MeaningLanguage);
        Assert.Equal(Usage.MeaningOrigin, copy.MeaningOrigin); Assert.Equal(Usage.SourceApplicationId, copy.SourceApplicationId);
        Assert.Equal(Usage.CreatedAt, copy.CreatedAt); Assert.Equal(Usage.Explanations, copy.Explanations);
    }

    [Fact]
    public void ConstructorAndWithDetachAllMutableListsAndPublishReadonlySnapshots()
    {
        var ranges = new List<VocabularyTextRange> { new(7, 4) };
        var variants = new List<VocabularyMeaning> { new("original", LearningLanguage.Spanish) };
        var records = new List<LearningExplanationRecord> { Record };
        var example = new VocabularyExample(Usage.Id, Usage.Text, "", ranges, Usage.Completeness, "", Usage.CreatedAt,
            translationVariants: variants, explanations: records);
        var edited = example with { Ranges = ranges, TranslationVariants = variants, Explanations = records };
        ranges.Clear(); variants.Clear(); records.Clear();
        Assert.Single(example.Ranges); Assert.Single(example.TranslationVariants); Assert.Single(example.Explanations);
        Assert.Equal(example, edited); Assert.Equal(example.GetHashCode(), edited.GetHashCode());
        Assert.True(Assert.IsAssignableFrom<ICollection<VocabularyTextRange>>(edited.Ranges).IsReadOnly);
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularyTextRange>)edited.Ranges)[0] = new(0, 1));
    }

    [Fact]
    public void CanonicalEqualityKeepsRawValuesAndUsageHashRetainsExactEncounteredText()
    {
        var first = Usage with { Text = "cafe\u0301", Meaning = "cafe\u0301", Ranges = [new(0, 1)] };
        var second = first with { Text = "café", Meaning = "café" };
        Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("cafe\u0301", first.Text);
        // Value equality follows Swift, while its usage key deliberately hashes original UTF-8.
        Assert.NotEqual(VocabularyCapturePolicy.ExampleKey(first), VocabularyCapturePolicy.ExampleKey(second));
        var a = new VocabularyMeaning("cafe\u0301", null); var b = new VocabularyMeaning("café", null);
        Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode()); Assert.Equal("unknown", a.Id);
    }

    [Fact]
    public void AnnotationAppLanguageAndExplicitDestinationDoNotSplitUsageIdentity()
    {
        var original = new VocabularyCapture("keep someone posted", "keep someone posted", LearningLanguage.English,
            VocabularyEntryKind.Pattern, Example: Usage);
        var edited = original with { DictionaryForm = "other", Meaning = "User", MeaningLanguage = LearningLanguage.Spanish,
            Explanation = Record, SenseId = Guid.NewGuid(), Example = Usage with { Meaning = "User edit", Translation = "Nueva",
                TranslationLanguage = LearningLanguage.Spanish, SourceApplicationId = "another.app", Explanations = [] } };
        Assert.NotEqual(original, edited);
        Assert.Equal(VocabularyCapturePolicy.CaptureKey(original), VocabularyCapturePolicy.CaptureKey(edited));
        Assert.NotEqual(VocabularyCapturePolicy.ExampleKey(Usage), VocabularyCapturePolicy.ExampleKey(Usage with { Completeness = VocabularyExampleCompleteness.Fragment }));
        Assert.NotEqual(VocabularyCapturePolicy.ExampleKey(Usage), VocabularyCapturePolicy.ExampleKey(Usage with { Ranges = Usage.Ranges.Reverse().ToArray() }));
    }

    [Fact]
    public void EntryNormalizationDoesNotLowercaseOrTrimUsingFoundationContentWhitespace()
    {
        var first = new VocabularyCapture(" \tcafe\u0301\r\n", "ignored", LearningLanguage.French, VocabularyEntryKind.Word);
        var second = first with { Expression = "café" };
        Assert.Equal(VocabularyCapturePolicy.EntryKey(first), VocabularyCapturePolicy.EntryKey(second));
        Assert.NotEqual(VocabularyCapturePolicy.EntryKey(first), VocabularyCapturePolicy.EntryKey(first with { Expression = "Café" }));
        Assert.Equal("\u200b", VocabularyCapturePolicy.Normalized("\u200b"));
        VocabularyCapturePolicy.Validate(first with { Expression = "\u200b" });
        Assert.Equal("no-context", VocabularyCapturePolicy.ExampleKey(null));
        Assert.EndsWith(":standalone", VocabularyCapturePolicy.CaptureKey(first));
    }

    [Fact]
    public void ReceiptDecimalFramingIsIndependentOfCurrentCulture()
    {
        string key = VocabularyCapturePolicy.ExampleKey(Usage);
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA"); Assert.Equal(key, VocabularyCapturePolicy.ExampleKey(Usage)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void MalformedDotNetStringCannotBecomeACaptureOrReceipt(int codeUnit)
    {
        string text = new((char)codeUnit, 1);
        var capture = new VocabularyCapture(text, "form", LearningLanguage.English, VocabularyEntryKind.Word);
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Validate(capture));
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.EntryKey(capture));
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.ExampleKey(Usage with { Text = text }));
    }

    [Fact]
    public void UnknownDotNetEnumValuesHaveNoSwiftCounterpartAndFailAdmission()
    {
        var capture = new VocabularyCapture("word", "word", LearningLanguage.English, VocabularyEntryKind.Word, Example: Usage);
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Validate(capture with { Language = (LearningLanguage)999 }));
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Validate(capture with { Kind = (VocabularyEntryKind)999 }));
        Assert.Throws<VocabularyContentValidationException>(() => VocabularyCapturePolicy.Validate(capture with { Example = Usage with { Completeness = (VocabularyExampleCompleteness)999 } }));
    }

    [Fact]
    public void FoundationEvidenceRangesKeepCrLfBoundaryWhileVisualSelectionKeepsWholeGrapheme()
    {
        var capture = new VocabularyCapture("word", "word", LearningLanguage.English, VocabularyEntryKind.Word,
            Example: Usage with { Text = "a\r\nb", Ranges = [new(1, 1)] });
        VocabularyCapturePolicy.Validate(capture);
        var input = LearningExplanationPolicy.Input(capture);
        Assert.True(LearningExplanationPolicy.IsValid(input)); Assert.Equal(new VocabularyTextRange(1, 1), Assert.Single(input.Ranges));
        Assert.Equal("\r\n", LiteralTextSelection.Create("a\r\nb", 1, 2)!.Text);
    }
}
