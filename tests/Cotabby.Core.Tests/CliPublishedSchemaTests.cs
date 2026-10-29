using Cotabby.Core.CLI;

namespace Cotabby.Core.Tests;

public sealed class CliPublishedSchemaTests
{
    [Fact]
    public void BatchAndCaptureExposeSourceContracts()
    {
        var batch = CliPublishedSchema.Batch(); var capture = CliPublishedSchema.Capture();
        Assert.Equal("Lingrove vocabulary and resource batch", batch["title"]!.GetValue<string>());
        Assert.Equal(21, batch["properties"]!["operations"]!["items"]!["oneOf"]!.AsArray().Count);
        Assert.Equal("unknown", batch["$defs"]!["example"]!["properties"]!["completeness"]!["enum"]![2]!.GetValue<string>());
        Assert.Equal(1, capture["properties"]!["version"]!["const"]!.GetValue<int>());
        Assert.Contains(capture["required"]!.AsArray(), item => item!.GetValue<string>() == "entries");
    }
}
