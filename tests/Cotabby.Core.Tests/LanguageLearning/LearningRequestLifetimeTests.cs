using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningRequestLifetimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RetirementCancelsTheDirectTokenBeforeItsBlockedCallbacksFinish()
    {
        var lifetime = new LearningRequestLifetime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.Token.Register(() =>
        {
            entered.TrySetResult();
            resume.Task.GetAwaiter().GetResult();
        });
        try
        {
            var retirement = lifetime.Retire();
            Assert.True(lifetime.Token.IsCancellationRequested);
            await entered.Task.WaitAsync(Timeout);
            Assert.False(retirement.IsCompleted);
            Assert.Same(retirement, lifetime.Retire());
            var disposal = lifetime.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            resume.TrySetResult();
            await disposal.WaitAsync(Timeout);
            Assert.True(lifetime.Token.IsCancellationRequested);
            await lifetime.Retire().WaitAsync(Timeout);
            await lifetime.DisposeAsync().AsTask().WaitAsync(Timeout);
        }
        finally
        {
            resume.TrySetResult();
            await lifetime.DisposeAsync().AsTask().WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task CoreCancellationRetiresTheSameTokenThatAProviderReceives()
    {
        await using var lifetime = new LearningRequestLifetime();
        using var core = new CancellationTokenSource();
        using var registration = core.Token.Register(() => _ = lifetime.Retire());
        var providerToken = lifetime.Token;

        core.Cancel();

        Assert.True(providerToken.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout, providerToken));
    }
}
