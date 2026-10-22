using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Tests.Shared;

/// <summary>Only test fixture IO/value adaptation; the provider facts were recorded from actual Mac tokenizers.</summary>
public static class LearningExplanationCaptureCorpus
{
    public static JsonElement[] Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "parity", "learning-explanation-capture-v1.json")));
        if (document.RootElement.GetProperty("version").GetInt32() != 1 || document.RootElement.GetProperty("macReference").GetString()
            != "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f") throw new InvalidOperationException("Unpinned capture fixture.");
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    public static LearningExplanationInput? Run(JsonElement row, LearningExplanationInputFactory factory)
    {
        var value = row.GetProperty("presentation");
        var state = new LearningPreviewState
        {
            RequestId = row.GetProperty("id").GetString()!,
            Status = Enum.Parse<LearningPreviewStatus>(value.GetProperty("status").GetString()!),
            Source = value.GetProperty("source").GetString()!, Result = value.GetProperty("translation").GetString()!,
            Options = new() { SourceLanguage = Language(value.GetProperty("sourceLanguage")), TargetLanguage = Language(value.GetProperty("targetLanguage")) },
        };
        string mode = row.GetProperty("mode").GetString()!;
        if (mode == "preview") return factory.FromWritingSentencePreview(state);
        var unit = Unit(row.GetProperty("unit"));
        var capture = factory.FromUnit(unit, state);
        return mode == "component" && capture is not null ? factory.FromComponent(Word(row.GetProperty("clicked")), unit, capture) : capture;
    }

    public static LearningLanguage Language(JsonElement value) => LearningLanguages.FromCode(value.GetString()!)!.Value;
    private static LearningTextRange Range(JsonElement value) => new(value.GetProperty("location").GetInt32(), value.GetProperty("length").GetInt32());
    private static LearningDictionaryWord Word(JsonElement value) => new(value.GetProperty("text").GetString()!, Range(value.GetProperty("range")));
    private static LearningUnit Unit(JsonElement value) => new(value.GetProperty("id").GetInt32(), value.GetProperty("surface").GetString()!,
        value.GetProperty("query").GetString()!, Enum.Parse<LearningUnitKind>(value.GetProperty("kind").GetString()!, true),
        value.GetProperty("ranges").EnumerateArray().Select(Range).ToArray(), value.GetProperty("words").EnumerateArray().Select(Word).ToArray());

    /// <summary>Fails on unrecorded calls instead of substituting a second implementation of Mac rules.</summary>
    public sealed class RecordedFacts(JsonElement facts) : ILearningLexicalText, ILearningEvidenceSentenceTokenizer
    {
        private void Verify(string text, LearningLanguage language)
        {
            if (!string.Equals(text, facts.GetProperty("text").GetString(), StringComparison.Ordinal)
                || language != Language(facts.GetProperty("language"))) throw new InvalidOperationException("Unrecorded capture text fact.");
        }
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language)
        {
            Verify(value, language); return facts.GetProperty("words").EnumerateArray().Select(Word).ToArray();
        }
        public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language)
        {
            Verify(value, language); return facts.GetProperty("sentences").EnumerateArray().Select(Range).ToArray();
        }
        public string Key(string value) => throw new InvalidOperationException("Capture does not normalize a query.");
        public bool IsPatternText(string value) => throw new InvalidOperationException("Capture does not recognize a new pattern.");
        public bool FirstIsUppercase(string value) => throw new InvalidOperationException("Capture does not recase a target.");
    }
}
