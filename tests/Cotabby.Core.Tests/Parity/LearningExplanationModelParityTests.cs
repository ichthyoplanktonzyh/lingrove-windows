using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Actual pinned Mac capability predicates, including the settings property's pending state.</summary>
public sealed class LearningExplanationModelParityTests
{
    public static IEnumerable<object[]> CaseIds() => Cases().Select(row => new object[] { row.GetProperty("id").GetString()! });
    private static JsonElement[] Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "parity", "learning-explanation-model-v1.json")));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", document.RootElement.GetProperty("macReference").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(row => row.Clone()).ToArray();
    }
    [Theory]
    [MemberData(nameof(CaseIds))]
    public void MacCapabilityResultsRemainTheAuthority(string id)
    {
        var row = Cases().Single(row => row.GetProperty("id").GetString() == id);
        if (row.GetProperty("mode").GetString() == "output")
        {
            Assert.Equal(row.GetProperty("expected").GetProperty("tokens").GetInt32(),
                LearningExplanationModelPolicy.ResolveOutputTokens(row.GetProperty("configured").Deserialize<int?>()));
            return;
        }
        string model = row.GetProperty("model").GetString()!;
        var template = row.GetProperty("template").Deserialize<LearningChatTemplate>();
        bool? actual = row.GetProperty("mode").GetString() == "supports"
            ? LearningExplanationModelPolicy.SupportsGeneralInstructions(model, template)
            : LearningExplanationModelPolicy.GeneralInstructionsAvailable(row.GetProperty("local").GetBoolean(), model, template,
                row.GetProperty("resolvedTemplate").Deserialize<LearningChatTemplate?>());
        var expected = row.GetProperty("expected").GetProperty("available");
        Assert.Equal(expected.ValueKind == JsonValueKind.Null ? (bool?)null : expected.GetBoolean(), actual);
    }
}
