using System.Globalization;
using System.Text;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.Parity;

/// <summary>
/// Runs the language-learning policy port against expectations produced by pure policies from the
/// pinned macOS commit. The same JSON is consumed by the Swift-source verifier, so test values are
/// reviewed independently of this C# implementation and never depend on inference or host APIs.
/// </summary>
public sealed class LearningParityCorpusTests
{
    private static readonly JsonDocument Corpus = LoadCorpus();

    [Fact]
    public void StableLanguageIdsMatchMacFixture()
    {
        var root = Corpus.RootElement;
        string[] expected = Strings(root.GetProperty("languageCodes"));
        string[] actual = LearningLanguages.All.Select(language => language.Code()).ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, actual.Distinct(StringComparer.Ordinal).Count());
        foreach (string code in Strings(root.GetProperty("unsupportedLanguageCodes")))
            Assert.Null(LearningLanguages.FromCode(code));
        Assert.Equal(root.GetProperty("serializedChinese").GetString(),
            JsonSerializer.Serialize(LearningLanguage.Chinese));
    }

    [Fact]
    public void TranslationStyleInstructionsAndDormantValuesMatchMacFixture()
    {
        foreach (JsonElement row in Rows("styleCases"))
        {
            string id = String(row, "id");
            var style = new LearningTranslationStyle(
                Register(String(row, "register")),
                Text(row.GetProperty("customInput")));
            var effective = style.Effective;

            Assert.Equal(Text(row.GetProperty("expectedNormalized")), style.Normalized.CustomInstruction);
            Assert.Equal(OptionalString(row, "expectedInstruction"), style.Instruction);
            Assert.Equal(
                Register(String(row, "expectedEffectiveRegister")),
                effective.Register);
            Assert.Equal(Text(row.GetProperty("expectedEffectiveCustom")), effective.CustomInstruction);
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void CustomStyleBoundsMatchMacUtf8AndGraphemeRules()
    {
        foreach (JsonElement row in Rows("styleBounds"))
        {
            string id = String(row, "id");
            var style = new LearningTranslationStyle(
                Register(String(row, "register")),
                Text(row.GetProperty("customInput")));
            string actual = style.Normalized.CustomInstruction;

            Assert.Equal(Text(row.GetProperty("expectedNormalized")), actual);
            Assert.Equal(Integer(row, "expectedUtf8Bytes"), Encoding.UTF8.GetByteCount(actual));
            Assert.Equal(Integer(row, "expectedGraphemes"), StringInfo.ParseCombiningCharacters(actual).Length);
            Assert.Equal(Boolean(row, "expectedContainsReplacement"), actual.Contains('\uFFFD'));
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void TokenEstimatesAllowancesAndOutputReservationsMatchMacFixture()
    {
        var root = Corpus.RootElement;
        var constants = root.GetProperty("budgetConstants");
        Assert.Equal(ModelContextBudget.MaximumLocalTokens, Integer(constants, "maximumLocalTokens"));
        Assert.Equal(ModelContextBudget.MaximumEndpointTokens, Integer(constants, "maximumEndpointTokens"));
        Assert.Equal(ModelContextBudget.MaximumTextBytes, Integer(constants, "maximumTextBytes"));
        Assert.Equal(ModelContextBudget.MaximumTranslationOutputTokens, Integer(constants, "maximumTranslationOutputTokens"));

        foreach (JsonElement row in Rows("tokenEstimates"))
        {
            Assert.Equal(Integer(row, "expectedTokens"), ModelContextBudget.EstimatedTokens(Text(row.GetProperty("text"))));
        }

        foreach (JsonElement row in Rows("inputAllowances"))
        {
            Assert.Equal(Integer(row, "expectedAllowance"), ModelContextBudget.InputAllowance(
                Integer(row, "total"), Integer(row, "output"), Boolean(row, "estimated")));
        }

        foreach (JsonElement row in Rows("outputReservations"))
        {
            string id = String(row, "id");
            if (Boolean(row, "expectedFailure"))
            {
                var error = Assert.Throws<LearningTranslationException>(() => ModelContextBudget.TranslationOutputTokens(
                    Text(row.GetProperty("source")), Integer(row, "total")));
                Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);
            }
            else
            {
                Assert.Equal(Integer(row, "expectedTokens"), ModelContextBudget.TranslationOutputTokens(
                    Text(row.GetProperty("source")), Integer(row, "total")));
            }
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void GenericAndSpecialistPromptsMatchMacTemplatesAndSampling()
    {
        foreach (JsonElement row in Rows("promptCases"))
        {
            string id = String(row, "id");
            var family = Family(String(row, "family"));
            var style = new LearningTranslationStyle(
                Register(row.TryGetProperty("register", out var register) ? register.GetString()! : "natural"),
                row.TryGetProperty("customInput", out var custom) ? Text(custom) : string.Empty);
            string source = Text(row.GetProperty("source"));
            var sourceLanguage = Language(String(row, "sourceLanguage"));
            var targetLanguage = Language(String(row, "targetLanguage"));

            if (Boolean(row, "expectedFailure"))
            {
                var error = Assert.Throws<LearningTranslationException>(() => LearningTranslationPrompt.Render(
                    source, sourceLanguage, targetLanguage, family, style));
                Assert.Equal(Enum.Parse<LearningTranslationFailure>(String(row, "expectedFailureCode"), ignoreCase: false), error.Failure);
                continue;
            }

            var actual = LearningTranslationPrompt.Render(source, sourceLanguage, targetLanguage, family, style);
            var sampling = row.GetProperty("sampling");
            Assert.Equal(Text(row.GetProperty("expectedPrompt")), actual.Prompt);
            Assert.Equal(Boolean(row, "expectedUserOnly"), actual.UserOnly);
            Assert.Equal(new LearningInstructionSampling(
                Double(sampling, "temperature"), Double(sampling, "topP"), Integer(sampling, "topK"),
                Double(sampling, "repetitionPenalty")), actual.Sampling);
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void CompleteTranslationTargetsAreBudgetedWholeAndNeverClipped()
    {
        foreach (JsonElement row in Rows("promptBudgets"))
        {
            string id = String(row, "id");
            string source = Text(row.GetProperty("source"));
            var style = new LearningTranslationStyle(
                Register(row.TryGetProperty("register", out var register) ? register.GetString()! : "natural"),
                row.TryGetProperty("customInput", out var custom) ? Text(custom) : string.Empty);
            var input = new LearningTranslationInput(source, style);
            var sourceLanguage = Language(String(row, "sourceLanguage"));
            var targetLanguage = Language(String(row, "targetLanguage"));
            var family = Family(String(row, "family"));
            int Count(string prompt) => String(row, "countMode") switch
            {
                "characters" => prompt.Length,
                "utf8Bytes" => Encoding.UTF8.GetByteCount(prompt),
                "estimatedTokens" => ModelContextBudget.EstimatedTokens(prompt),
                var mode => throw new InvalidDataException($"Unknown fixture count mode '{mode}'."),
            };

            if (Boolean(row, "expectedAccepted"))
            {
                var actual = LearningTranslationBudget.Prompt(input, sourceLanguage, targetLanguage, family,
                    Integer(row, "total"), Integer(row, "output"), Boolean(row, "estimated"), Count);
                if (row.TryGetProperty("expectedSourceSuffix", out var expectedSuffix))
                    Assert.EndsWith(expectedSuffix.GetString(), actual.Prompt, StringComparison.Ordinal);
            }
            else
            {
                var error = Assert.Throws<LearningTranslationException>(() => LearningTranslationBudget.Prompt(
                    input, sourceLanguage, targetLanguage, family, Integer(row, "total"), Integer(row, "output"),
                    Boolean(row, "estimated"), Count));
                Assert.Equal(LearningTranslationFailure.OversizedTarget, error.Failure);
            }
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void TranslationOutputValidationMatchesMacWholeResponseByteBoundary()
    {
        foreach (JsonElement row in Rows("outputValidation"))
        {
            string id = String(row, "id");
            string raw = Text(row.GetProperty("raw"));
            if (Boolean(row, "expectedValid"))
            {
                string actual = LearningExpressionOutputPolicy.ValidatedText(raw);
                Assert.Equal(Text(row.GetProperty("expectedText")), actual);
                Assert.Equal(Integer(row, "expectedUtf8Bytes"), Encoding.UTF8.GetByteCount(actual));
            }
            else
            {
                var error = Assert.Throws<LearningTranslationException>(() => LearningExpressionOutputPolicy.ValidatedText(raw));
                Assert.Equal(LearningTranslationFailure.InvalidOutput, error.Failure);
            }
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void SourceLanguageConfidenceRankingMatchesMacFixture()
    {
        foreach (JsonElement row in Rows("sourceLanguageScoreCases"))
        {
            var scores = new Dictionary<LearningLanguage, double>();
            foreach (JsonElement score in row.GetProperty("scores").EnumerateArray())
                scores.Add(Language(String(score, "language")), Double(score, "score"));

            Assert.Equal(OptionalString(row, "expectedLanguage"),
                LearningSourceLanguageResolver.ConfidentLanguage(scores)?.Code());
        }
    }

    [Fact]
    public void ExplicitSingleSourceResolutionFiltersWithoutCallingTheDetector()
    {
        foreach (JsonElement row in Rows("sourceLanguageResolutionCases"))
        {
            var detector = new CountingLanguageDetector();
            string? actual = LearningSourceLanguageResolver.Resolve(
                String(row, "text"),
                Strings(row.GetProperty("enabledSourceLanguages")).Select(Language).ToArray(),
                Language(String(row, "targetLanguage")),
                detector)?.Code();

            Assert.Equal(OptionalString(row, "expectedLanguage"), actual);
            Assert.Equal(Integer(row, "expectedDetectorCalls"), detector.Calls);
        }
    }

    [Fact]
    public void StreamedOutputPrefixesAndRejectionsMatchMacFixture()
    {
        foreach (JsonElement row in Rows("streamAppendCases"))
        {
            JsonElement[] chunks = row.GetProperty("chunks").EnumerateArray().ToArray();
            JsonElement[] expectedPrefixes = row.GetProperty("expectedPrefixes").EnumerateArray().ToArray();
            Assert.Equal(chunks.Length, expectedPrefixes.Length);

            int? expectedFailureAt = row.TryGetProperty("expectedFailureAt", out var failureIndex)
                && failureIndex.ValueKind == JsonValueKind.Number
                ? failureIndex.GetInt32()
                : null;
            var stream = new LearningExpressionOutputPolicy.Stream();
            var failedAt = (int?)null;

            for (var index = 0; index < chunks.Length; index++)
            {
                if (expectedFailureAt == index)
                {
                    var error = Assert.Throws<LearningTranslationException>(() => stream.Append(Text(chunks[index])));
                    Assert.Equal(LearningTranslationFailure.InvalidOutput, error.Failure);
                    failedAt = index;
                    break;
                }

                string? expectedPrefix = expectedPrefixes[index].ValueKind == JsonValueKind.Null
                    ? null
                    : Text(expectedPrefixes[index]);
                Assert.Equal(expectedPrefix, stream.Append(Text(chunks[index])));
            }

            Assert.Equal(expectedFailureAt, failedAt);
        }
    }

    private static JsonDocument LoadCorpus()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "learning-v1.json");
        using FileStream stream = File.OpenRead(path);
        JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (Integer(root, "schemaVersion") != 1)
            throw new InvalidDataException($"Unsupported learning parity fixture version {Integer(root, "schemaVersion")}.");
        if (String(root, "macBaselineRevision") != "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f")
            throw new InvalidDataException($"Unexpected macOS learning fixture baseline '{String(root, "macBaselineRevision")}'.");

        ValidateProvenance(root);
        return document;
    }

    private static void ValidateProvenance(JsonElement root)
    {
        string[] groups = ["styleCases", "styleBounds", "tokenEstimates", "inputAllowances", "outputReservations", "promptCases", "promptBudgets", "outputValidation", "sourceLanguageScoreCases", "sourceLanguageResolutionCases", "streamAppendCases"];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string group in groups)
        {
            JsonElement category = root.GetProperty(group);
            if (category.ValueKind != JsonValueKind.Array || category.GetArrayLength() == 0)
                throw new InvalidDataException($"Learning parity fixture category '{group}' must contain cases.");
            foreach (JsonElement row in category.EnumerateArray())
            {
                string id = String(row, "id");
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || string.IsNullOrWhiteSpace(String(row, "macTest")))
                    throw new InvalidDataException("Learning parity cases require unique IDs and macOS provenance.");
            }
        }
    }

    private static IEnumerable<JsonElement> Rows(string name) => Corpus.RootElement.GetProperty(name).EnumerateArray().ToArray();

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static string Text(JsonElement spec)
    {
        if (spec.ValueKind == JsonValueKind.String) return spec.GetString()!;
        if (spec.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid learning parity text spec.");
        if (spec.TryGetProperty("repeat", out var repeat))
            return string.Concat(Enumerable.Repeat(Text(repeat.GetProperty("text")), Integer(repeat, "count")));
        if (spec.TryGetProperty("concat", out var concat))
            return string.Concat(concat.EnumerateArray().Select(Text));
        throw new InvalidDataException("Unknown learning parity text spec.");
    }

    private static string String(JsonElement row, string key) => row.GetProperty(key).GetString()!;
    private static string? OptionalString(JsonElement row, string key) =>
        row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static int Integer(JsonElement row, string key) => row.GetProperty(key).GetInt32();
    private static double Double(JsonElement row, string key) => row.GetProperty(key).GetDouble();
    private static bool Boolean(JsonElement row, string key) => row.GetProperty(key).GetBoolean();

    private static LearningLanguage Language(string code) =>
        LearningLanguages.FromCode(code) ?? throw new InvalidDataException($"Unknown fixture language '{code}'.");

    private static LearningTranslationRegister Register(string code) =>
        JsonSerializer.Deserialize<LearningTranslationRegister>(JsonSerializer.Serialize(code));

    private static LearningTranslationPrompt.Family Family(string name) => name switch
    {
        "generic" => LearningTranslationPrompt.Family.Generic,
        "hyMT" => LearningTranslationPrompt.Family.HyMt,
        "translateGemma" => LearningTranslationPrompt.Family.TranslateGemma,
        _ => throw new InvalidDataException($"Unknown learning prompt family '{name}'."),
    };

    private sealed class CountingLanguageDetector : ILearningLanguageDetector
    {
        public int Calls { get; private set; }

        public IReadOnlyDictionary<LearningLanguage, double> Scores(
            string sample, IReadOnlyList<LearningLanguage> candidates)
        {
            Calls++;
            return new Dictionary<LearningLanguage, double>();
        }
    }
}
