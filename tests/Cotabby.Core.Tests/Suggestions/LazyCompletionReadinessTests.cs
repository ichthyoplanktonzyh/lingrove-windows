using Cotabby.Core.Suggestions;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class LazyCompletionReadinessTests : CoordinatorHarness
{
    [Fact]
    public async Task FirstEligibleLocalRequestWaitsForReadyThenGenerates()
    {
        Engine.IsReady = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Coordinator.EnsureLocalReady = async token => { started.TrySetResult(); await release.Task.WaitAsync(token); Engine.IsReady = true; return true; };
        Type("hello"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Empty(Engine.Requests);
        release.SetResult(); await Shown(" world again"); Assert.Single(Engine.Requests);
    }
    [Fact]
    public async Task FocusChangeDuringPreparationCannotGenerateAgainstOldCapture()
    {
        Engine.IsReady = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Coordinator.EnsureLocalReady = async _ => { started.SetResult(); await release.Task; Engine.IsReady = true; return true; };
        Type("hello"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Focus.Focus(Fields.Text("other", pid: 101)); await RunOnUiAsync(() => { }); release.SetResult();
        await StaysHidden(); Assert.Empty(Engine.Requests);
    }
    [Fact]
    public async Task EndpointNotReadyDoesNotInvokeLocalPreparation()
    {
        Engine.IsReady = false; int calls = 0;
        Coordinator.Settings = Coordinator.Settings with { UsesEndpoint = true };
        Coordinator.EnsureLocalReady = _ => { Interlocked.Increment(ref calls); return Task.FromResult(true); };
        await RunOnUiAsync(() => { }); Type("hello"); await Task.Delay(200);
        Assert.Equal(0, calls); Assert.Empty(Engine.Requests);
    }
    [Fact]
    public async Task SecureFieldCannotStartPreparation()
    {
        Engine.IsReady = false; int calls = 0;
        Coordinator.EnsureLocalReady = _ => { Interlocked.Increment(ref calls); return Task.FromResult(true); };
        Focus.Focus(Fields.Text("") with { IsSecure = true }); await RunOnUiAsync(() => { }); Type("hello"); await Task.Delay(200);
        Assert.Equal(0, calls); Assert.Empty(Engine.Requests);
    }
}
