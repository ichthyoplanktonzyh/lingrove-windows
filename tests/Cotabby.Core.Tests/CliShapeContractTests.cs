using System.Text.Json.Nodes;
using Cotabby.Core.CLI;

namespace Cotabby.Core.Tests;

public sealed class CliShapeContractTests
{
    [Fact]
    public void CategoryGuidanceSchemaAndNestedDefaultsPreserveMeaning()
    {
        var shape = CliShapeContract.Resource("categories"); string id = Guid.NewGuid().ToString("D"), label = Guid.NewGuid().ToString("D");
        var input = JsonNode.Parse($$"""{"id":"{{id}}","axis":"content","labels":[{"id":"{{label}}","text":"bank"}],"scopeNote":"guidance","localizations":[{"language":"fr","name":"banque"}]}""");
        var normalized = shape.Normalize(input); Assert.True((bool)normalized["labels"]![0]!["preferred"]!); Assert.Equal("guidance", (string?)normalized["scopeNote"]);
        Assert.Equal("banque", (string?)normalized["localizations"]![0]!["name"]); Assert.True(shape.Schema()["additionalProperties"]!.GetValue<bool>() == false);
        Assert.Throws<CliException>(() => shape.Normalize(JsonNode.Parse($$"""{"id":"{{id}}","axis":"content","labels":[],"typo":1}""")));
    }
    [Fact]
    public void PatchOmissionAndExplicitNullRemainDistinct()
    {
        var shape = CliShapeContract.Resource("categoryOverrides"); var id = Guid.NewGuid().ToString("D");
        var result = shape.Normalize(JsonNode.Parse($$"""{"categoryID":"{{id}}","scopeNote":null}"""), creating: false);
        Assert.True(result.ContainsKey("scopeNote")); Assert.Null(result["scopeNote"]); Assert.False(result.ContainsKey("definition"));
        Assert.Throws<CliException>(() => shape.Normalize(JsonNode.Parse($$"""{"categoryID":"{{id}}","isHidden":null}"""), creating: false));
    }
    [Fact]
    public void CaptureRejectsUnknownFieldsAndOversizedUtf8()
    {
        var valid = JsonNode.Parse("""{"entries":[{"expression":"bank","senses":[{"meaning":"money","associations":[{"meaning":"finance"}]}]}]}""");
        Assert.NotNull(CliShapeContract.Capture.Normalize(valid));
        Assert.Throws<CliException>(() => CliShapeContract.Capture.Normalize(JsonNode.Parse("""{"entries":[{"expression":"bank","senses":[],"notReal":true}]}""")));
        var oversized = new string('中', 10667); Assert.Throws<CliException>(() => CliShapeContract.Capture.Normalize(new JsonObject { ["entries"] = new JsonArray(new JsonObject { ["expression"] = oversized, ["senses"] = new JsonArray() }) }));
    }
    [Fact]
    public void EveryListedResourceHasClosedShapeAndPatchDoesNotInventDefaults()
    {
        foreach (string name in CliArguments.Resources) { var shape = CliShapeContract.Resource(name); Assert.NotNull(shape.Schema()); Assert.Empty(shape.Normalize(new JsonObject(), creating: false)); }
        Assert.Throws<CliException>(() => CliShapeContract.Resource("unknown"));
    }
}
