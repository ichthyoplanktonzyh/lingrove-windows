using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.LanguageLearning;
using Cotabby.Tests.Shared;

namespace Cotabby.Core.Tests.Parity;

public sealed class LearningExplanationCaptureParityTests
{
    public static IEnumerable<object[]> CaseIds() => LearningExplanationCaptureCorpus.Cases().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(CaseIds))]
    public void PinnedMacCaptureCompositionOwnsTheExpectedInput(string id)
    {
        var row = LearningExplanationCaptureCorpus.Cases().Single(row => row.GetProperty("id").GetString() == id);
        var facts = new LearningExplanationCaptureCorpus.RecordedFacts(row.GetProperty("facts"));
        var actual = LearningExplanationCaptureCorpus.Run(row, new(facts, facts));
        var expected = row.GetProperty("expected");
        if (expected.ValueKind == JsonValueKind.Null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected.GetProperty("valid").GetBoolean(), LearningExplanationPolicy.IsValid(actual));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetProperty("value").GetRawText()), JsonSerializer.SerializeToNode(actual)), id);
    }
}
