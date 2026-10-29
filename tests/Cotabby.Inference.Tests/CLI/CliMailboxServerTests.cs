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
    [Fact]
    public async Task StartupRequeuesMoreThanOneScanPage()
    {
        using var working = new CliMailboxDirectory(Path.Combine(_root, "working"));
        for (int index = 0; index < 129; index++)
        {
            var request = new CliRequest { ExpiresAt = CliWire.Now() + 60 };
            working.Publish(request.Id.ToString("D") + ".json", CliWire.Encode(request));
        }
        int handled = 0;
        await using var server = new CliMailboxServer(_root, request => { Interlocked.Increment(ref handled); return Task.FromResult(new CliReply { RequestId = request.Id, Ok = true }); });
        await server.StartAsync(); await WaitUntil(() => Volatile.Read(ref handled) == 129);
    }
    [Fact]
    public async Task RealMailboxBorrowsOneNamedStoreForCaptureAndRead()
    {
        var path = Path.Combine(_root, "notebook", FrozenVocabularyStoreFactory.CanonicalFileName);
        await using var store = FrozenVocabularyStoreFactory.Create(path, false);
        var cli = Assert.IsAssignableFrom<Cotabby.Core.Vocabulary.Frozen.ICanonicalVocabularyCliStore>(store);
        await using var server = CanonicalCliMailbox.Create(Path.Combine(_root, "automation"), cli);
        await server.StartAsync();
        var batch = CliInputContract.DecodeCapture(System.Text.Encoding.UTF8.GetBytes("""{"version":1,"idempotencyKey":"e2e","defaults":{"language":"en"},"entries":[{"expression":"bank","senses":[{"meaning":"money"}]}]}"""));
        var write = new CliRequest { Command = "apply", ExpiresAt = CliWire.Now() + 20, Batch = batch.CopyValue() };
        var result = await CliMailboxClient.SendAsync(Path.Combine(_root, "automation"), write);
        Assert.True(result.Ok); Assert.Single((await store.ReadAsync()).Document.CollectedEntries);
        var status = await CliMailboxClient.SendAsync(Path.Combine(_root, "automation"), new CliRequest { ExpiresAt = CliWire.Now() + 20 });
        Assert.True(status.Ok); Assert.Equal(1, status.Result!["entryCount"]!.GetValue<int>());
        var retry = await CliMailboxClient.SendAsync(Path.Combine(_root, "automation"), write with { Id = Guid.NewGuid() });
        Assert.True(retry.Ok); Assert.True(retry.Result!["replayed"]!.GetValue<bool>());
        Assert.Equal(1UL, (await store.ReadAsync()).Revision);
    }
}
