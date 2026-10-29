using System.Text;
using Cotabby.Core.CLI;

namespace Cotabby.Core.Tests;

public sealed class CliInputContractTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    [Fact]
    public void CaptureWrapsExactMaterialInVersionTwoBatch()
    {
        var batch = CliInputContract.DecodeCapture(Bytes("""{"version":1,"idempotencyKey":"task","entries":[{"expression":"bank","senses":[{"meaning":"money"}]}]}"""));
        var value = batch.CopyValue(); Assert.Equal(2, value["version"]!.GetValue<int>()); Assert.Equal("task", (string?)value["idempotencyKey"]);
        Assert.Equal("money", (string?)value["operations"]![0]!["capture"]!["entries"]![0]!["senses"]![0]!["meaning"]);
        value["idempotencyKey"] = "changed"; Assert.Equal("task", (string?)batch.CopyValue()["idempotencyKey"]);
    }
    [Theory]
    [InlineData("{\"version\":1,\"idempotencyKey\":\"x\",\"entries\":[]}")]
    [InlineData("{\"version\":1,\"idempotencyKey\":\"x\",\"entries\":[{\"expression\":\"x\",\"senses\":[],\"misspelled\":true}]}")]
    [InlineData("{\"version\":1,\"idempotencyKey\":\"x\",\"entries\":[{\"expression\":\"x\",\"senses\":[]}]}")]
    public void CaptureRejectsUnknownOrEmptyMaterial(string json) => Assert.Throws<CliException>(() => CliInputContract.DecodeCapture(Bytes(json)));
    [Fact]
    public void ResourcePatchRequiresExpectedRevisionAndPreservesExplicitNull()
    {
        string id = Guid.NewGuid().ToString("D"); string json = """{"version":2,"idempotencyKey":"k","expectedRevision":7,"operations":[{"put":{"resource":"categoryOverrides","value":{"categoryID":"PLACEHOLDER","scopeNote":null}}}]}""".Replace("PLACEHOLDER", id);
        var value = CliInputContract.DecodeBatch(Bytes(json)).CopyValue(); Assert.True(value["operations"]![0]!["put"]!["value"]!.AsObject().ContainsKey("scopeNote"));
        Assert.Throws<CliException>(() => CliInputContract.DecodeBatch(Bytes(json.Replace("\"expectedRevision\":7,", ""))));
    }
    [Fact]
    public void ClientRejectsInlineImageButWireAcceptsBoundedBytes()
    {
        string input = """{"version":1,"idempotencyKey":"k","operations":[{"add":{"expression":"word","language":"en","kind":"word","meaning":"m","content":[{"image":{"data":"AA=="}}]}}]}""";
        Assert.Throws<CliException>(() => CliInputContract.DecodeBatch(Bytes(input)));
        var wire = System.Text.Json.Nodes.JsonNode.Parse(input)!.AsObject(); Assert.NotNull(CliInputContract.ValidateWire(wire));
    }
}
