namespace Cotabby.Core.Tests.Suggestions;

public sealed class PumpContextLifetimeTests
{
    [Fact]
    public async Task ClosingDrainsAdmittedCallbacksJoinsTheThreadAndRejectsLateInvocation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); using var pump = new PumpContext();
        pump.Post(_ => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); completed.TrySetResult(); }, null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task closing = Task.Run(pump.Dispose);
        try
        {
            Assert.True(await Task.Run(() => SpinWait.SpinUntil(() => pump.IsStopping, TimeSpan.FromSeconds(5))));
            Assert.False(completed.Task.IsCompleted); Assert.False(closing.IsCompleted);
        }
        finally { release.Set(); await closing.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.True(completed.Task.IsCompletedSuccessfully); Assert.False(pump.IsAlive);
        bool late = false; pump.Post(_ => late = true, null); Assert.False(late);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pump.InvokeAsync(() => throw new InvalidOperationException("Closed pump ran a late callback.")));
    }
}
