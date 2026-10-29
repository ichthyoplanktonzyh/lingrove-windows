using System.Text;
using Cotabby.Core.CLI;

namespace Cotabby.Core.Tests;

public sealed class CliWireTests
{
    [Fact]
    public void RequestIdentityAndSourceDateRoundtripWithoutSharedMutableInput()
    {
        var source = CliRequest.FromArguments(CliArguments.Parse(["vocab", "list", "--language", "en"]), 123.125);
        var copy = CliWire.DecodeRequest(CliWire.Encode(source)); Assert.Equal(source.Id, copy.Id); Assert.Equal(123.125, copy.ExpiresAt); Assert.Equal("list", copy.Command); Assert.Equal("en", copy.Language);
        Assert.Throws<CliException>(() => CliWire.DecodeRequest(Encoding.UTF8.GetBytes("""{"command":"status"}""")));
    }
    [Fact]
    public void StableExitErrorsDoNotContainSubmittedContent()
    {
        var id = Guid.NewGuid(); var reply = CliWire.Failure(id, new CliException(CliErrorCode.Conflict, "Notebook revision changed."));
        var decoded = CliWire.Decode<CliReply>(CliWire.Encode(reply)); Assert.Equal(id, decoded.RequestId); Assert.False(decoded.Ok); Assert.Equal("conflict", decoded.Error!.Code);
        Assert.Equal(4, new CliException(CliErrorCode.Conflict, "").ExitCode);
    }
}
