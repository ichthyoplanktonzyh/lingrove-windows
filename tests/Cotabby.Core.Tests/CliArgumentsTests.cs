using Cotabby.Core.CLI;

namespace Cotabby.Core.Tests;

public sealed class CliArgumentsTests
{
    [Theory]
    [InlineData("", CliMode.Help)] [InlineData("--help", CliMode.Help)] [InlineData("help", CliMode.Help)] [InlineData("--version", CliMode.Version)] [InlineData("schema", CliMode.Schema)] [InlineData("schema capture", CliMode.CaptureSchema)]
    public void InformationalCommandsRequireExactTokens(string command, CliMode mode) => Assert.Equal(mode, CliArguments.Parse(command.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Mode);
    [Fact]
    public void CaptureAndResourceFlagsAreOwnedByTheirCommands()
    {
        var capture = CliArguments.Parse(["--no-launch", "--timeout", "1.5", "vocab", "capture", "--stdin", "--dry-run"]);
        Assert.Equal(CliCommand.Apply, capture.Command); Assert.Equal("-", capture.InputPath); Assert.True(capture.CaptureInput && capture.DryRun); Assert.False(capture.Launch); Assert.Equal(1.5, capture.TimeoutSeconds);
        var id = Guid.NewGuid(); var resource = CliArguments.Parse(["data", "list", "mappings", "--related", id.ToString("D"), "--limit", "20", "--query", "bank"]);
        Assert.Equal(CliCommand.ResourceList, resource.Command); Assert.Equal(id, resource.RelatedId); Assert.Equal(20, resource.Limit); Assert.Equal("bank", resource.Query);
    }
    [Theory]
    [InlineData("status --dry-run")] [InlineData("schema --app x")] [InlineData("vocab list --limit 1 --limit 2")] [InlineData("vocab apply --file a --stdin")] [InlineData("vocab get bad")] [InlineData("data get unknown 11111111-1111-1111-1111-111111111111")] [InlineData("status --timeout NaN")] [InlineData("status --timeout 121")] [InlineData("status --limit 2")]
    public void RejectsDuplicateInvalidAndIrrelevantOptions(string command) => Assert.Equal(CliErrorCode.InvalidInput, Assert.Throws<CliException>(() => CliArguments.Parse(command.Split(' '))).Code);
}
