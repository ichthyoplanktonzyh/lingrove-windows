using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Tests.Shared;
using Cotabby.Win32.Text;

namespace Cotabby.Inference.Tests.LanguageLearning;

/// <summary>Real Windows ICU capture behavior; this never creates an editor or inference runtime.</summary>
public sealed class LearningExplanationCaptureNativeTests
{
    public static IEnumerable<object[]> CaseIds() => LearningExplanationCaptureCorpus.Cases()
        .Where(row => row.GetProperty("nativeTextParity").GetBoolean()).Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(CaseIds))]
    public void RealWordAndEvidenceSentenceAdaptersPreserveTheScopedMacCapture(string id)
    {
        var row = LearningExplanationCaptureCorpus.Cases().Single(row => row.GetProperty("id").GetString() == id);
        var actual = LearningExplanationCaptureCorpus.Run(row, new(new IcuLearningLexicalText(), new IcuLearningEvidenceSentenceTokenizer()));
        var expected = row.GetProperty("expected");
        if (expected.ValueKind == JsonValueKind.Null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected.GetProperty("valid").GetBoolean(), LearningExplanationPolicy.IsValid(actual));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("value").GetRawText()), JsonSerializer.SerializeToNode(actual)), id);
    }
}
