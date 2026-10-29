using System.Text.Json.Nodes;
using Cotabby.Core.CLI;
using Cotabby.Win32.CLI;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests.CLI;

public sealed class CliMailboxServerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-cli-server-" + Guid.NewGuid().ToString("N"));
    public Task InitializeAsync() { VocabularyWindowsFile.CreateParentDirectory(_root); return Task.CompletedTask; }
    public Task DisposeAsync() { if (Directory.Exists(_root)) Directory.Delete(_root, true); return Task.CompletedTask; }
    private static async Task WaitUntil(Func<bool> condition)
    { var until = DateTime.UtcNow.AddSeconds(10); while (!condition()) { Assert.True(DateTime.UtcNow < until, "Mailbox operation did not finish."); await Task.Delay(20); } }
    [Fact]
    public async Task DirectoryEventsWakeServerAndSecondLeaseFails()
    {
        await using var server = new CliMailboxServer(_root, request => Task.FromResult(new CliReply { RequestId = request.Id, Ok = true, Result = new JsonObject { ["revision"] = 7 } }));
        await server.StartAsync(); await using var second = new CliMailboxServer(_root, request => Task.FromResult(new CliReply { RequestId = request.Id }));
        Assert.Equal(CliErrorCode.Busy, (await Assert.ThrowsAsync<CliException>(async () => await second.StartAsync())).Code);
        using var requests = new CliMailboxDirectory(Path.Combine(_root, "requests"), false); using var replies = new CliMailboxDirectory(Path.Combine(_root, "replies"), false);
        var input = new CliRequest { ExpiresAt = CliWire.Now() + 30 }; string name = input.Id.ToString("D") + ".json"; requests.Publish(name, CliWire.Encode(input));
        await WaitUntil(() => replies.Exists(name)); var reply = CliWire.Decode<CliReply>(replies.Read(name)!); Assert.True(reply.Ok); Assert.Equal(input.Id, reply.RequestId); Assert.Equal(7, reply.Result!["revision"]!.GetValue<int>());
    }
    [Fact]
    public async Task StartupRequeuesClaimedRequestAndStopJoinsAcknowledgement()
    {
        using var requests = new CliMailboxDirectory(Path.Combine(_root, "requests")); using var working = new CliMailboxDirectory(Path.Combine(_root, "working")); using var replies = new CliMailboxDirectory(Path.Combine(_root, "replies"));
        var request = new CliRequest { ExpiresAt = CliWire.Now() + 30 }; string name = request.Id.ToString("D") + ".json"; working.Publish(name, CliWire.Encode(request));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new CliMailboxServer(_root, async input => { entered.TrySetResult(); await release.Task; return new CliReply { RequestId = input.Id, Ok = true }; });
        await server.StartAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var stopping = server.DisposeAsync().AsTask(); Assert.False(stopping.IsCompleted);
        release.SetResult(); await stopping; Assert.True(replies.Exists(name)); Assert.False(working.Exists(name)); Assert.False(requests.Exists(name));
    }
    [Fact]
    public async Task ClientAndServerExchangeAnAcknowledgedRequest()
    {
        await using var server = new CliMailboxServer(_root, request => Task.FromResult(new CliReply { RequestId = request.Id, Ok = true, Result = new JsonObject { ["revision"] = 9 } }));
        await server.StartAsync();
        var request = new CliRequest { ExpiresAt = CliWire.Now() + 10 };
        var reply = await CliMailboxClient.SendAsync(_root, request);
        Assert.True(reply.Ok); Assert.Equal(request.Id, reply.RequestId); Assert.Equal(9, reply.Result!["revision"]!.GetValue<int>());
        using var replies = new CliMailboxDirectory(Path.Combine(_root, "replies"), false);
        Assert.False(replies.Exists(request.Id.ToString("D") + ".json"));
    }
}
