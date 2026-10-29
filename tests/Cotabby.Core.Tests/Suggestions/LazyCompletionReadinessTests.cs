using Cotabby.Core.Suggestions;
using Cotabby.Core.Focus;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class LazyCompletionReadinessTests : CoordinatorHarness
{
    [Theory]
    [InlineData(InputCompositionState.Unknown, false, false)]
    [InlineData(InputCompositionState.Composing, false, false)]
    [InlineData(InputCompositionState.Unknown, true, false)]
    [InlineData(InputCompositionState.Composing, true, true)]
    public async Task UncommittedOrUnknownEvidenceNeverStartsWarmColdOrEndpointWork(InputCompositionState state, bool ready, bool endpoint)
    {
        Engine.IsReady = ready; int prepares = 0;
        Coordinator.Settings = Coordinator.Settings with { UsesEndpoint = endpoint };
        Coordinator.EnsureLocalReady = _ => { Interlocked.Increment(ref prepares); return Task.FromResult(true); };
        Focus.Focus(Fields.Text("") with { Composition = state }); await RunOnUiAsync(() => { }); Type("hello");
        await Task.Delay(200); Assert.Equal(0, prepares); Assert.Empty(Engine.Requests); await StaysHidden();
    }
    [Theory]
    [InlineData(InputCompositionState.Composing)]
    [InlineData(InputCompositionState.Clear)]
    [InlineData(InputCompositionState.Unknown)]
    public async Task CompositionChangesDuringColdPreparationRejectEvenUnchangedText(InputCompositionState state)
    {
        Engine.IsReady = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Coordinator.EnsureLocalReady = async _ => { started.SetResult(); await release.Task; Engine.IsReady = true; return true; };
        Type("hello"); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Focus.Replace(Focus.Current! with { Composition = state, CompositionSequence = 2 });
        release.SetResult(); await StaysHidden(); Assert.Empty(Engine.Requests);
    }
    [Fact]
    public async Task CompositionChangesAfterOfferNeverConsumeAcceptKey()
    {
        Type("hello"); await Shown(" world again");
        Focus.Replace(Focus.Current! with { Composition = InputCompositionState.Composing, CompositionSequence = 2 });
        Assert.False(Hook.Press(Keys.Tab())); await Task.Delay(200); Assert.Empty(Focus.InsertionSnapshot);
    }
    [Fact]
    public async Task CompositionEventCancelsWarmEndpointGenerationWithoutTextOrCaretChange()
    {
        Coordinator.Settings = Coordinator.Settings with { UsesEndpoint = true }; await RunOnUiAsync(() => { });
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.GenerateOverride = (_, token) => { started.TrySetResult(token); return release.Task; };
        Type("hello"); var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = Focus.Current!; Focus.PublishComposition(InputCompositionState.Composing, before.CompositionSequence + 1);
        Assert.True(token.IsCancellationRequested); Assert.Equal(before.Text, Focus.Current!.Text); Assert.Equal(before.CaretOffset, Focus.Current!.CaretOffset);
        release.SetResult(" stale"); await StaysHidden();
    }
    [Fact]
    public async Task CompositionEventRevokesAcceptBeforeUiProcessesTheNotification()
    {
        Type("hello"); await Shown(" world again");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockUiUntil(release.Task, entered); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var before = Focus.Current!; Focus.PublishComposition(InputCompositionState.Composing, before.CompositionSequence + 1);
            Assert.False(Hook.Press(Keys.Tab())); Assert.Empty(Focus.InsertionSnapshot);
        }
        finally { release.TrySetResult(); }
        await StaysHidden(); Assert.Empty(Focus.InsertionSnapshot);
    }
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
