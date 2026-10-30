using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using VocabularyCapture = Cotabby.Core.Vocabulary.VocabularyCapture;

namespace Cotabby.Core.Tests.Vocabulary;

/// <summary>Includes the committed 3b expressionAssistCandidateSavesBaseFormWithConstructedSentenceContext case.</summary>
public sealed class ExpressionAssistCaptureTests
{
    [Fact]
    public void HistoricalLeafShapeRemainsCapturedOnlyRatherThanDroppingGeneratedProvenance()
    {
        var example = Factory().FromExpressionAssist(new("kept me posted", "释义", "keep someone posted"), Request())!.Example!;
        Assert.Throws<Cotabby.Core.Vocabulary.VocabularyException>(() => VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Usage(example)));
        var bytes = VocabularyPayloadCodec.Encode(new VocabularyDatabasePayload.Usage(example with { SourceKind = Cotabby.Core.Vocabulary.Resources.VocabularySourceKind.Captured }));
        Assert.DoesNotContain("sourceKind", System.Text.Encoding.UTF8.GetString(bytes));
        var decoded = Assert.IsType<VocabularyDatabasePayload.Usage>(VocabularyPayloadCodec.Decode(bytes, Cotabby.Core.Vocabulary.VocabularyDatabaseTable.Usages));
        Assert.Equal(Cotabby.Core.Vocabulary.Resources.VocabularySourceKind.Captured, decoded.Value.SourceKind);
    }
    internal sealed class Facts : ILearningLexicalText, ILearningEvidenceSentenceTokenizer
    {
        public IReadOnlyList<LearningTextRange> Tokenize(string text, LearningLanguage language) => [new(0, text.Length)];
        public IReadOnlyList<LearningDictionaryWord> Words(string text, LearningLanguage language) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new LearningDictionaryWord(word, new(text.IndexOf(word, StringComparison.Ordinal), word.Length))).ToArray();
        public string Key(string text) => text.ToLowerInvariant();
        public bool IsPatternText(string text) => false;
        public bool FirstIsUppercase(string text) => text.Length > 0 && char.IsUpper(text[0]);
    }
    private static VocabularyCaptureFactory Factory() { var facts = new Facts(); return new(facts, facts); }
    private static ExpressionAssistRequest Request(string left = "Thanks, she ", string right = " throughout the week.") => new("随时了解情况", left, right, "", "en", "zh-Hans");

    [Fact]
    public void MacBaseFormCasePreservesGeneratedHypotheticalOccurrenceAndAiMeaning()
    {
        var capture = Factory().FromExpressionAssist(new("kept me posted", "让我随时了解最新情况", "keep someone posted"), Request(), "com.apple.TextEdit");
        Assert.NotNull(capture); Assert.Equal("keep someone posted", capture.Expression); Assert.Equal(capture.Expression, capture.DictionaryForm);
        Assert.Equal(VocabularyEntryKind.Expression, capture.Kind); Assert.Equal(LearningLanguage.English, capture.Language);
        Assert.Equal("让我随时了解最新情况", capture.Meaning); Assert.Equal(LearningLanguage.Chinese, capture.MeaningLanguage);
        var example = Assert.IsType<Cotabby.Core.Vocabulary.VocabularyExample>(capture.Example);
        Assert.Equal("Thanks, she kept me posted throughout the week.", example.Text);
        var range = Assert.Single(example.Ranges); Assert.Equal("kept me posted", example.Text.Substring(range.Location, range.Length));
        Assert.Equal(capture.Meaning, example.Meaning); Assert.Equal(VocabularyMeaningOrigin.Ai, example.MeaningOrigin);
        Assert.Equal(Cotabby.Core.Vocabulary.Resources.VocabularySourceKind.Generated, example.SourceKind);
        Assert.Equal("com.apple.TextEdit", example.SourceApplicationId);
        VocabularyCapturePolicy.Validate(capture);
        var adapted = CanonicalWritingPolicy.FromLive(capture);
        Assert.Equal(example.SourceKind, adapted.Example!.SourceKind); Assert.Equal(example.Id, adapted.Example.Id);
        Assert.Equal(example.SourceKind, example.CopiedWithNewId().SourceKind);
    }

    [Fact]
    public void ExactUntrimmedSurfaceRetainsItsUtf16OffsetWhileTheBaseFormIsCanonical()
    {
        var capture = Factory().FromExpressionAssist(new("  😀 kept me posted  ", "  释义  ", " keep someone posted "), Request("😀 ", " later."));
        Assert.NotNull(capture); var range = Assert.Single(capture.Example!.Ranges);
        Assert.Equal(5, range.Location); Assert.Equal("😀 kept me posted", capture.Example.Text.Substring(range.Location, range.Length));
        Assert.Equal("keep someone posted", capture.Expression); Assert.Equal("释义", capture.Example.Meaning);
        Assert.Null(capture.Example.SourceApplicationId);
    }

    [Fact]
    public void ContextlessCandidateDoesNotInventAnEncounterOrTreatTheQueryAsItsMeaning()
    {
        var capture = Factory().FromExpressionAssist(new(" go ", " ", null), Request("  ", "\n"));
        Assert.NotNull(capture); Assert.Equal(VocabularyEntryKind.Word, capture.Kind); Assert.Equal("go", capture.Expression);
        Assert.Null(capture.Example); Assert.Empty(capture.Meaning); Assert.Null(capture.MeaningLanguage);
        Assert.Null(Factory().FromExpressionAssist(new("  ", "hint", null), Request()));
        Assert.Null(Factory().FromExpressionAssist(new("hello", "hint", null), Request() with { TargetLanguage = "unsupported" }));
    }

    [Fact]
    public void CaptureKeysStayStableAcrossNewEncounterIdentityButIgnoreNoSourceText()
    {
        var first = Factory().FromExpressionAssist(new("kept me posted", "释义", "keep someone posted"), Request())!;
        var second = Factory().FromExpressionAssist(new("kept me posted", "释义", "keep someone posted"), Request())!;
        Assert.NotEqual(first.Example!.Id, second.Example!.Id);
        Assert.Equal(VocabularyCapturePolicy.CaptureKey(first), VocabularyCapturePolicy.CaptureKey(second));
        Assert.NotEqual(VocabularyCapturePolicy.CaptureKey(first), VocabularyCapturePolicy.CaptureKey(first with
            { Example = first.Example with { Text = "Other text", Ranges = [new(0, 5)] } }));
    }
}
