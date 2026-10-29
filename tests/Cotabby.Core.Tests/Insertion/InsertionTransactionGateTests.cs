using Cotabby.Core.Insertion;

namespace Cotabby.Core.Tests.Insertion;

public sealed class InsertionTransactionGateTests
{
    [Fact]
    public async Task ExceptionReleasesGateForAnotherOwner()
    {
        var gate = new InsertionTransactionGate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync<int>(
            _ => Task.FromException<int>(new InvalidOperationException()), CancellationToken.None));
        Assert.Equal(7, await gate.RunAsync(_ => Task.FromResult(7), CancellationToken.None));
    }

    [Fact]
    public async Task SharedOwnersSerializeAndCancellationDoesNotReleaseAnotherOwnersLease()
    {
        var gate = new InsertionTransactionGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.RunAsync(async _ => { entered.SetResult(); await release.Task; return 1; }, CancellationToken.None);
        await entered.Task;
        using var cancel = new CancellationTokenSource();
        bool called = false;
        var second = gate.RunAsync(_ => { called = true; return Task.FromResult(2); }, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(called);
        var third = gate.RunAsync(_ => { called = true; return Task.FromResult(3); }, CancellationToken.None);
        Assert.False(third.IsCompleted);
        release.SetResult();
        Assert.Equal(1, await first);
        Assert.Equal(3, await third);
        Assert.True(called);
    }
}
