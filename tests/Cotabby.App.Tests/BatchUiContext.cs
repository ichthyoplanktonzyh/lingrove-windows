using System.Collections.Concurrent;

namespace Cotabby.App.Tests;

/// <summary>One portable test owns a serial UI-like context so batch starts/notifications and
/// resumptions obey the original MainActor boundary. This is no Windows dispatcher proof; the
/// compiled STA suite supplies that separately. The test drains accepted work before disposal.</summary>
internal sealed class BatchUiContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Thread _thread;
    private TaskCompletionSource? _active;
    private Exception? _fault;
    internal BatchUiContext()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new(() =>
        {
            SetSynchronizationContext(this); ready.SetResult();
            try { foreach (var call in _queue.GetConsumingEnumerable()) call.Callback(call.State); }
            catch (Exception error) { _fault = error; _active?.TrySetException(error); }
        }) { IsBackground = true, Name = "Review batch portable UI context" };
        _thread.Start(); ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }
    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
    internal async Task RunAsync(Func<CancellationToken, Task> work)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _active = completion;
        Post(async _ => { try { await work(deadline.Token); completion.TrySetResult(); } catch (Exception error) { completion.TrySetException(error); } }, null);
        // The deadline cancels the callback's waits; it does not detach a timed-out waiter.
        // Its finally path stops/drains the batch and Store while this queue remains alive.
        await completion.Task;
    }
    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("Batch UI test context did not drain.");
        _queue.Dispose(); if (_fault is not null) throw new AggregateException("Batch UI test callback failed.", _fault);
    }
}
