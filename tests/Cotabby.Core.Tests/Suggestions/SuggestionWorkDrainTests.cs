using Cotabby.Core.Suggestions;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class SuggestionWorkDrainTests
{
    [Fact]
    public async Task ShutdownJoinsSupersededWorkThatIgnoresCancellation()
    {
        var owner = new SuggestionWorkController(TimeSpan.Zero);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = owner.Submit(async _ => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await owner.Submit(_ => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        var first = owner.DisposeAsync().AsTask(); var second = owner.DisposeAsync().AsTask();
        Assert.Same(first, second); Assert.False(first.IsCompleted);
        release.SetResult(); await Task.WhenAll(old, first).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
