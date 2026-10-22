using System.Text.Json;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.Parity;

/// <summary>Expected values are captured from complete pinned Mac link/viewport production code.</summary>
public sealed class LearningDictionaryUiParityTests
{
    public static IEnumerable<object[]> CaseIds() => Cases().Select(row => new object[] { row.GetProperty("id").GetString()! });
    private static JsonElement[] Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "parity", "learning-dictionary-ui-v1.json")));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", document.RootElement.GetProperty("macReference").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(row => row.Clone()).ToArray();
    }
    [Theory]
    [MemberData(nameof(CaseIds))]
    public void MacLinkAndViewportObservationsRemainTheAuthority(string id)
    {
        var row = Cases().Single(row => row.GetProperty("id").GetString() == id);
        var expected = row.GetProperty("expected");
        if (row.GetProperty("kind").GetString() == "link")
            Assert.Equal(expected.GetProperty("allowed").GetBoolean(),
                DictionaryLinkPolicy.ExternalUrl(row.GetProperty("value").GetString()!) is not null);
        else
        {
            var viewport = new LearningDictionaryViewport();
            var heights = new List<double>();
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                var height = step.GetProperty("height");
                string query = step.GetProperty("query").GetString()!;
                viewport.Measure(height.ValueKind == JsonValueKind.Number ? height.GetDouble() : double.NaN,
                    query, step.GetProperty("resolved").GetBoolean());
                heights.Add(viewport.HeightFor(query));
            }
            Assert.Equal(expected.GetProperty("heights").EnumerateArray().Select(value => value.GetDouble()), heights);
        }
    }
}
