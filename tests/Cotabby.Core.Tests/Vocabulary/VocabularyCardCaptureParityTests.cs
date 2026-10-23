using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Tests.Shared;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyCardCaptureParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-card-capture-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    private static LearningTextRange Range(JsonElement value) => new(value.GetProperty("location").GetInt32(), value.GetProperty("length").GetInt32());
    private static LearningDictionaryWord Word(JsonElement value) => new(value.GetProperty("text").GetString()!, Range(value.GetProperty("range")));
    private static LearningUnit Unit(JsonElement value) => new(value.GetProperty("id").GetInt32(), value.GetProperty("surface").GetString()!, value.GetProperty("query").GetString()!,
        Enum.Parse<LearningUnitKind>(value.GetProperty("kind").GetString()!, true), value.GetProperty("ranges").EnumerateArray().Select(Range).ToArray(), value.GetProperty("words").EnumerateArray().Select(Word).ToArray());
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalMacFactoryRetainsTranslationsMeaningsAndExactOccurrences(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id); var value = row.GetProperty("presentation");
        var state = new LearningPreviewState
        {
            RequestId = id, Source = value.GetProperty("source").GetString()!, Result = value.GetProperty("translation").GetString()!, Status = Enum.Parse<LearningPreviewStatus>(value.GetProperty("status").GetString()!),
            Options = new() { SourceLanguage = LearningExplanationCaptureCorpus.Language(value.GetProperty("sourceLanguage")), TargetLanguage = LearningExplanationCaptureCorpus.Language(value.GetProperty("targetLanguage")) },
        };
        var facts = new LearningExplanationCaptureCorpus.RecordedFacts(row.GetProperty("facts")); var factory = new VocabularyCaptureFactory(facts, facts);
        string mode = row.GetProperty("mode").GetString()!; string app = row.GetProperty("app").GetString()!;
        VocabularyCapture? capture;
        if (mode == "preview") capture = factory.FromWritingSentencePreview(state, app);
        else
        {
            var unit = Unit(row.GetProperty("unit")); capture = factory.FromUnit(unit, state, app);
            if (mode == "component" && capture is not null) capture = VocabularyCaptureFactory.FromComponent(Word(row.GetProperty("clicked")), unit, capture);
        }
        if (capture is not null)
        {
            Assert.Null(capture.SenseId); Assert.Null(capture.Explanation);
            if (capture.Example is { } usage) { Assert.NotEqual(Guid.Empty, usage.Id); Assert.Equal(VocabularyMeaningOrigin.Captured, usage.MeaningOrigin); Assert.Empty(usage.TranslationVariants); Assert.Empty(usage.Explanations); }
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(Describe(capture));
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{id}: {actual}");
    }
    private static object? Describe(VocabularyCapture? capture)
    {
        if (capture is null) return null;
        var usage = capture.Example;
        object? example = usage is null ? null : new { text = usage.Text, translation = usage.Translation, translationLanguage = usage.TranslationLanguage?.Code(),
            ranges = usage.Ranges.Select(range => new { location = range.Location, length = range.Length }), completeness = usage.Completeness.ToString().ToLowerInvariant(),
            meaning = usage.Meaning, meaningLanguage = usage.MeaningLanguage?.Code(), meaningOrigin = usage.MeaningOrigin.ToString().ToLowerInvariant(), app = usage.SourceApplicationId };
        return new { expression = capture.Expression, dictionaryForm = capture.DictionaryForm, language = capture.Language.Code(), kind = capture.Kind.ToString().ToLowerInvariant(),
            meaning = capture.Meaning, meaningLanguage = capture.MeaningLanguage?.Code(), example, input = LearningExplanationPolicy.Input(capture) };
    }
}
