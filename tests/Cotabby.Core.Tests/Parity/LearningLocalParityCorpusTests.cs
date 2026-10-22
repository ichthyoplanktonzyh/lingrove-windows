using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.Parity;

/// <summary>
/// Runs the pinned macOS local-template corpus against portable Core policy. The sibling Swift
/// verifier executes the actual Mac implementations first; this test consumes the same expected
/// cases without loading GGUF weights or constructing a native runtime.
/// </summary>
public sealed class LearningLocalParityCorpusTests
{
    [Fact]
    public void TemplateIdentifiersAndRoleAvailabilityMatchMac()
    {
        using var corpus = LoadCorpus();
        string[] identifiers = Strings(corpus.RootElement.GetProperty("templateIdentifiers"));

        Assert.Equal(identifiers, Enum.GetValues<LearningChatTemplate>().Select(Identifier));

        foreach (JsonElement row in Rows(corpus.RootElement, "templateAvailability"))
        {
            LearningModelRole role = Role(String(row, "role"));
            LearningChatTemplate[] expected = Strings(row.GetProperty("expectedTemplates"))
                .Select(Template)
                .ToArray();

            Assert.Equal(expected, LearningChatTemplatePolicy.AvailableFor(role));
        }
    }

    [Fact]
    public void MetadataResolvesTheSameTemplateFamilyAndRecommendedSamplingAsMac()
    {
        using var corpus = LoadCorpus();
        foreach (JsonElement row in Rows(corpus.RootElement, "profileCases"))
        {
            string id = String(row, "id");
            LearningChatTemplate requestedTemplate = Template(String(row, "requestedTemplate"));
            string modelFileName = String(row, "modelFileName");
            var configuration = new LearningLocalModelConfiguration(
                Path.Combine("fixture", modelFileName),
                requestedTemplate,
                contextWindowTokens: 8_192);
            LearningGgufMetadata? metadata = Metadata(row.GetProperty("metadata"));

            if (row.TryGetProperty("expectedFailure", out JsonElement expectedFailure) && expectedFailure.GetBoolean())
            {
                Assert.Throws<LearningTranslationException>(() =>
                    LearningLocalPrompt.ResolveProfile(configuration, metadata, modelFileName));
                Assert.False(string.IsNullOrWhiteSpace(id));
                continue;
            }

            LearningLocalModelProfile actual =
                LearningLocalPrompt.ResolveProfile(configuration, metadata, modelFileName);
            JsonElement expectedSampling = row.GetProperty("expectedSampling");

            Assert.Equal(Template(String(row, "expectedTemplate")), actual.Template);
            Assert.Equal(Family(String(row, "expectedFamily")), actual.Family);
            Assert.Equal(
                new LearningInstructionSampling(
                    Number(expectedSampling, "temperature"),
                    Number(expectedSampling, "topP"),
                    Integer(expectedSampling, "topK"),
                    Number(expectedSampling, "repetitionPenalty")),
                actual.RecommendedSampling);
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void TrustedTemplateDelimitersKeepThePayloadLiteral()
    {
        using var corpus = LoadCorpus();
        string payload = String(corpus.RootElement, "literalPayload");

        foreach (JsonElement row in Rows(corpus.RootElement, "delimiterCases"))
        {
            LearningChatTemplate template = Template(String(row, "template"));
            bool expectedFailure = row.TryGetProperty("expectedFailure", out JsonElement failure) && failure.GetBoolean();

            if (expectedFailure)
            {
                Assert.Throws<LearningTranslationException>(() => LearningLocalPrompt.GetSegments(template, payload));
                continue;
            }

            string expectedPrefix = String(row, "expectedPrefix");
            string expectedSuffix = String(row, "expectedSuffix");
            (string prefix, string suffix) = LearningLocalPrompt.Delimiters(template);
            LearningPromptSegments segments = LearningLocalPrompt.GetSegments(template, payload);

            Assert.Equal(expectedPrefix, prefix);
            Assert.Equal(expectedSuffix, suffix);
            Assert.Equal(expectedPrefix, segments.TrustedPrefix);
            Assert.Equal(payload, segments.LiteralBody);
            Assert.Equal(expectedSuffix, segments.TrustedSuffix);
            Assert.Equal(expectedPrefix + payload + expectedSuffix, segments.RenderedText);
        }
    }

    [Fact]
    public void SpecialistAndWholeContextBudgetsMatchMacWithoutClipping()
    {
        using var corpus = LoadCorpus();
        foreach (JsonElement row in Rows(corpus.RootElement, "budgetCases"))
        {
            string id = String(row, "id");
            bool expectedAccepted = Boolean(row, "expectedAccepted");
            Action validate = () => LearningLocalPrompt.ValidateTokenBudget(
                Integer(row, "inputTokens"),
                Integer(row, "outputTokens"),
                Template(String(row, "template")),
                Integer(row, "contextWindowTokens"));

            if (expectedAccepted)
            {
                validate();
            }
            else
            {
                Assert.Throws<LearningTranslationException>(validate);
            }
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }

    [Fact]
    public void ExactCatalogRecommendationsAndSettingsFallbackMatchMac()
    {
        using var corpus = LoadCorpus();
        foreach (JsonElement row in Rows(corpus.RootElement, "contextRecommendations"))
        {
            int? expected = row.GetProperty("expectedRecommendation").ValueKind == JsonValueKind.Null
                ? null
                : Integer(row, "expectedRecommendation");
            Assert.Equal(expected, LearningRuntimeCatalog.DefaultTranslationContextWindowTokens(String(row, "filename")));
        }

        int fallback = Integer(corpus.RootElement, "settingsFallbackContextTokens");
        var defaultConfiguration = new LearningLocalModelConfiguration("default.gguf");
        Assert.Equal(fallback, defaultConfiguration.ContextWindowTokens);
        Assert.Equal(LearningChatTemplate.Automatic, defaultConfiguration.RequestedTemplate);
        Assert.Equal(0, defaultConfiguration.GpuLayerCount);

        int maximumLocalTokens = Integer(corpus.RootElement, "maximumLocalTokens");
        Assert.Equal(ModelContextBudget.MaximumLocalTokens, maximumLocalTokens);
        Assert.Equal(
            maximumLocalTokens,
            new LearningLocalModelConfiguration("maximum.gguf", contextWindowTokens: maximumLocalTokens)
                .ContextWindowTokens);
        var oversized = Assert.Throws<LearningTranslationException>(() =>
            new LearningLocalModelConfiguration("oversized.gguf", contextWindowTokens: maximumLocalTokens + 1));
        Assert.Equal(LearningTranslationFailure.InvalidContextBudget, oversized.Failure);

        JsonElement probe = corpus.RootElement.GetProperty("configurationProbe");
        LearningChatTemplate template = Template(String(probe, "template"));
        int context = Integer(probe, "contextWindowTokens");
        string modelPath = Path.Combine("fixture", String(probe, "modelFileName"));
        var explicitConfiguration = new LearningLocalModelConfiguration(modelPath, template, context);
        Assert.Equal(modelPath, explicitConfiguration.ModelPath);
        Assert.Equal(template, explicitConfiguration.RequestedTemplate);
        Assert.Equal(context, explicitConfiguration.ContextWindowTokens);
    }

    private static LearningGgufMetadata? Metadata(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        return new LearningGgufMetadata(
            value.TryGetProperty("architecture", out JsonElement architecture) ? architecture.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("name", out JsonElement name) ? name.GetString() : null,
            value.TryGetProperty("chatTemplate", out JsonElement chatTemplate) ? chatTemplate.GetString() : null);
    }

    private static LearningChatTemplate Template(string code) =>
        JsonSerializer.Deserialize<LearningChatTemplate>(JsonSerializer.Serialize(code));

    private static string Identifier(LearningChatTemplate template) =>
        JsonSerializer.Serialize(template).Trim('"');

    private static LearningModelRole Role(string code) => code switch
    {
        "translation" => LearningModelRole.Translation,
        "explanation" => LearningModelRole.Explanation,
        _ => throw new InvalidDataException($"Unknown learning model role '{code}'."),
    };

    private static LearningTranslationPrompt.Family Family(string code) => code switch
    {
        "generic" => LearningTranslationPrompt.Family.Generic,
        "hyMT" => LearningTranslationPrompt.Family.HyMt,
        "translateGemma" => LearningTranslationPrompt.Family.TranslateGemma,
        _ => throw new InvalidDataException($"Unknown translation family '{code}'."),
    };

    private static JsonDocument LoadCorpus()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "learning-local-v1.json");
        using FileStream stream = File.OpenRead(path);
        JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (Integer(root, "schemaVersion") != 1)
            throw new InvalidDataException($"Unsupported local learning fixture version {Integer(root, "schemaVersion")}.");
        if (String(root, "macBaselineRevision") != "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f")
            throw new InvalidDataException($"Unexpected macOS local-learning fixture baseline '{String(root, "macBaselineRevision")}'.");

        ValidateProvenance(root);
        return document;
    }

    private static void ValidateProvenance(JsonElement root)
    {
        string[] groups =
        [
            "templateAvailability",
            "profileCases",
            "delimiterCases",
            "budgetCases",
            "contextRecommendations",
        ];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string group in groups)
        {
            JsonElement cases = root.GetProperty(group);
            if (cases.ValueKind != JsonValueKind.Array || cases.GetArrayLength() == 0)
                throw new InvalidDataException($"Local learning fixture category '{group}' must contain cases.");
            foreach (JsonElement row in cases.EnumerateArray())
            {
                if (string.IsNullOrWhiteSpace(String(row, "id")) || !ids.Add(String(row, "id")))
                    throw new InvalidDataException($"Local learning fixture '{group}' has a missing or duplicate case id.");
                if (string.IsNullOrWhiteSpace(String(row, "macTest")))
                    throw new InvalidDataException($"Local learning fixture case '{String(row, "id")}' has no Mac provenance.");
            }
        }
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string category) =>
        root.GetProperty(category).EnumerateArray();

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static string String(JsonElement row, string key) => row.GetProperty(key).GetString()!;
    private static int Integer(JsonElement row, string key) => row.GetProperty(key).GetInt32();
    private static double Number(JsonElement row, string key) => row.GetProperty(key).GetDouble();
    private static bool Boolean(JsonElement row, string key) => row.GetProperty(key).GetBoolean();
}
