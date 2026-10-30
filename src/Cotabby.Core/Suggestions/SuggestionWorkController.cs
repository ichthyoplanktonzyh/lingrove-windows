namespace Cotabby.Core.Suggestions;

/// <summary>
/// Owns the debounce timer and the cancellation token for generation work. Submitting a new
/// request cancels the one before it.
/// </summary>
public sealed class SuggestionWorkController : IAsyncDisposable
{
    private readonly object _gate = new();

    private CancellationTokenSource? _inFlight;
    private readonly HashSet<Task> _tasks = [];
    private bool _disposed;
    private Task? _disposal;

    public SuggestionWorkController(TimeSpan debounce) => Debounce = debounce;

    /// <summary>Quiet time after the last submit before work starts.</summary>
    public TimeSpan Debounce { get; set; }

    /// <summary>
    /// Schedules <paramref name="work"/> to run after the debounce window and cancels the work
    /// scheduled before it. The returned task completes when the work finishes, is cancelled
    /// or throws.
    /// </summary>
    public Task Submit(Func<CancellationToken, Task> work)
    {
        CancellationTokenSource? prior;
        CancellationTokenSource cts;
        Task task;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            prior = _inFlight;
            cts = new CancellationTokenSource();
            _inFlight = cts;
            // Register the task before shutdown can take its drain snapshot.
            task = Task.Run(async () => { await start.Task.ConfigureAwait(false); await RunAsync(work, cts).ConfigureAwait(false); });
            _tasks.Add(task);
        }

        try { Cancel(prior); }
        finally { start.TrySetResult(); }

        // The work always starts on the thread pool. With no debounce it would otherwise run on
        // the caller, which is the UI thread.
        _ = task.ContinueWith(completed => { lock (_gate) _tasks.Remove(completed); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    private async Task RunAsync(Func<CancellationToken, Task> work, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(Debounce, cts.Token).ConfigureAwait(false);
            await work(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* expected */ }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, cts))
                {
                    _inFlight = null;
                }
            }
            cts.Dispose();
        }
    }

    /// <summary>Cancel any pending or in-flight work without scheduling a new task.</summary>
    public void Cancel()
    {
        CancellationTokenSource? prior;
        lock (_gate)
        {
            prior = _inFlight;
            _inFlight = null;
        }
        Cancel(prior);
    }

    /// <summary>The work disposes its own source when it ends, so a late cancel may find it gone.</summary>
    private static void Cancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _disposed = true;
            var prior = _inFlight; _inFlight = null;
            var tasks = _tasks.ToArray();
            _disposal = Task.Run(() => DrainAsync(prior, tasks));
            return new(_disposal);
        }
    }
    private static async Task DrainAsync(CancellationTokenSource? prior, Task[] tasks)
    {
        try { Cancel(prior); } catch { /* Still join work if a foreign cancellation callback fails. */ }
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch { /* already logged at submit site */ }
    }
}
