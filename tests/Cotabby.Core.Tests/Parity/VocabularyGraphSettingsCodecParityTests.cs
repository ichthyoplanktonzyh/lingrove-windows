using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

public sealed class VocabularyGraphSettingsCodecParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-graph-presentation-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Where(row => row.GetProperty("id").GetString()!.StartsWith("c-", StringComparison.Ordinal))
        .Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public void CompleteOriginalCodablePayloadKeepsStrictFieldsLabelsAndFoundationInputRules(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(34, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);
        Assert.Equal("source-policy-extension", row.GetProperty("provenanceKind").GetString());
        Assert.Equal("Cotabby/Models/Vocabulary/VocabularyGraphModels.swift", row.GetProperty("macSource").GetString());
        var decoded = VocabularyGraphSettingsCodec.Decode(Convert.FromBase64String(row.GetProperty("data").GetString()!));
        var expected = row.GetProperty("expected");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("decoded").GetRawText()), decoded is null ? null : Describe(decoded)));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("normalized").GetRawText()), decoded is null ? null : Describe(decoded.Normalized)));
        if (decoded is not null) Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("normalized").GetRawText()), JsonNode.Parse(VocabularyGraphSettingsCodec.EncodeNormalized(decoded))));
    }
    [Fact]
    public void UndefinedDotNetLabelCannotEnterTheClosedPreferenceEnum()
    {
        var invalid = new VocabularyGraphSettings { Labels = (VocabularyGraphLabels)999, Center = double.NaN };
        Assert.Equal(VocabularyGraphLabels.Automatic, invalid.Normalized.Labels);
        Assert.Equal(new VocabularyGraphSettings(), VocabularyGraphSettingsCodec.Decode(VocabularyGraphSettingsCodec.EncodeNormalized(invalid)));
    }
    private static JsonNode? Describe(VocabularyGraphSettings s) => JsonSerializer.SerializeToNode(new { nodeSize = s.NodeSize, linkWidth = s.LinkWidth,
        labels = s.Labels.ToString().ToLowerInvariant(), center = s.Center, repulsion = s.Repulsion, linkStrength = s.LinkStrength, linkDistance = s.LinkDistance });
}
