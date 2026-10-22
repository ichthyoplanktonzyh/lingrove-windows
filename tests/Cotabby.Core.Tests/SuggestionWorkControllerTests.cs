using Cotabby.Core.Suggestions;

namespace Cotabby.Core.Tests;

public class SuggestionWorkControllerTests
{
    [Fact]
    public async Task Submit_RunsAfterDebounce()
    {
        await using var c = new SuggestionWorkController(TimeSpan.FromMilliseconds(20));
        var fired = false;
        await c.Submit(_ => { fired = true; return Task.CompletedTask; });
        Assert.True(fired);
    }

    [Fact]
    public async Task Submit_CancelsPriorWork()
    {
        // Enter the first body before submitting again, then hold it on its cancellation token.
        // A barrier makes the test independent of how long the thread pool takes to start work.
        await using var c = new SuggestionWorkController(TimeSpan.Zero);
        var firstBodyEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBodyEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool firstWorkObservedCancellation = false;

        var first = c.Submit(async cancellationToken =>
        {
            Task cancellationWait = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            firstBodyEntered.TrySetResult(true);
            try
            {
                await cancellationWait;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                firstWorkObservedCancellation = true;
                throw;
            }
        });

        // This timeout only bounds a broken test; it does not determine whether the
        // first body should have started before the second submit.
        await firstBodyEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = c.Submit(_ =>
        {
            secondBodyEntered.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(firstWorkObservedCancellation);
        Assert.True(secondBodyEntered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Submit_CancelsPendingWork_AndCancelStopsReplacement()
    {
        // Infinite debounce keeps both callbacks pending by construction. The first task must end
        // when replacement cancels it; explicit Cancel then releases the replacement task.
        await using var c = new SuggestionWorkController(Timeout.InfiniteTimeSpan);
        bool firstBodyEntered = false;
        bool replacementBodyEntered = false;

        var first = c.Submit(_ =>
        {
            firstBodyEntered = true;
            return Task.CompletedTask;
        });
        var replacement = c.Submit(_ =>
        {
            replacementBodyEntered = true;
            return Task.CompletedTask;
        });

        await first.WaitAsync(TimeSpan.FromSeconds(10));
        c.Cancel();
        await replacement.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(firstBodyEntered);
        Assert.False(replacementBodyEntered);
    }

    [Fact]
    public async Task Cancel_StopsScheduledWork()
    {
        await using var c = new SuggestionWorkController(Timeout.InfiniteTimeSpan);
        bool fired = false;
        var task = c.Submit(_ => { fired = true; return Task.CompletedTask; });
        c.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(fired);
    }
}
