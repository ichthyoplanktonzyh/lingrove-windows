namespace Cotabby.Core.Suggestions;

/// <summary>
/// Single admission owner for the borrowed completion engine and its existing runtime. Selection
/// changes withdraw authorization synchronously; native generation and model preparation share one
/// exclusive span so a runtime cannot be disposed while its engine tokenizes or decodes. An epoch
/// rejects old completions even if the same model is selected again later. Endpoint routing is owned
/// separately and never borrows this state.
/// </summary>
public sealed class CompletionModelAdmission : ISuggestionEngine, IAsyncDisposable
{
    public readonly record struct Ticket(long Epoch, string? ModelId);
    private readonly ISuggestionEngine _engine;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    private readonly HashSet<Task> _work = [];
    private string? _modelId;
    private long _epoch;
    private bool _local, _admitted, _closed;
    private int _preparations;
    private CancellationTokenSource _epochCancellation = new();
    private Task? _disposal;

    public CompletionModelAdmission(ISuggestionEngine engine) => _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <summary>Retires old publication and edit authority before the caller schedules a switch.</summary>
    public Ticket Select(string? modelId, bool local)
        => SelectIfEpoch(null, modelId, local) ?? new(Epoch, null);

    /// <summary>Apply a resolved selection only if no concurrent withdrawal occurred while resolving.</summary>
    public Ticket? SelectIfEpoch(long? expectedEpoch, string? modelId, bool local)
    {
        CancellationTokenSource? retired = null;
        TaskCompletionSource? retirement = null;
        Ticket result;
        lock (_sync)
        {
            if (_closed || expectedEpoch is { } expected && expected != _epoch) return null;
            if (_local != local || !string.Equals(_modelId, modelId, StringComparison.Ordinal))
            { retired = _epochCancellation; retirement = RegisterRetirementLocked(); _epochCancellation = new(); _epoch++; _modelId = modelId; _local = local; _admitted = false; }
            result = new(_epoch, _modelId);
        }
        if (retired is not null) Retire(retired, retirement!);
        return result;
    }

    public long Epoch { get { lock (_sync) return _epoch; } }
    /// <summary>Commit a short in-memory selection mutation only while the resolution epoch is current.
    /// The callback must not perform IO or reenter this owner.</summary>
    public bool CommitIfEpoch(long epoch, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (_sync) { if (_closed || epoch != _epoch) return false; commit(); return true; }
    }
    public Ticket? Capture(string modelId)
    {
        lock (_sync) return !_closed && _local && string.Equals(_modelId, modelId, StringComparison.Ordinal)
            ? new Ticket(_epoch, _modelId) : null;
    }
    public bool IsCurrent(Ticket ticket) { lock (_sync) return Current(ticket); }
    /// <summary>Immediately retire local authorization before a caller resolves updated settings.</summary>
    public void Withdraw()
    {
        CancellationTokenSource? retired = null;
        TaskCompletionSource? retirement = null;
        lock (_sync) { if (!_closed) { retired = _epochCancellation; retirement = RegisterRetirementLocked(); _epochCancellation = new(); _epoch++; _local = false; _modelId = null; _admitted = false; } }
        if (retired is not null) Retire(retired, retirement!);
    }
    public bool IsReady { get { lock (_sync) return !_closed && _local && _admitted && _engine.IsReady; } }
    public T? ReadAdmitted<T>(Func<T?> read) where T : class
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (_sync) return !_closed && _local && _admitted && _engine.IsReady ? read() : null;
    }
    public bool IsPreparing { get { lock (_sync) return _preparations > 0; } }
    public bool SpacingIsExact => _engine.SpacingIsExact;

    /// <summary>Download and load share the same serialized span as inference. An obsolete transfer
    /// may finish, but is rechecked before loading, and an obsolete load never becomes admitted.</summary>
    public Task<bool> PrepareAsync(Ticket ticket, Func<CancellationToken, Task> acquire,
        Func<Task> load, Func<bool> loadedMatches, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquire); ArgumentNullException.ThrowIfNull(load); ArgumentNullException.ThrowIfNull(loadedMatches);
        return Track(async token =>
        {
            try
            {
                await _exclusive.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!IsCurrent(ticket)) return false;
                    if (IsReady && loadedMatches()) return true;
                    lock (_sync) { if (!Current(ticket)) return false; _admitted = false; }
                    token.ThrowIfCancellationRequested();
                    await acquire(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!IsCurrent(ticket)) return false;
                    await load().ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (!Current(ticket) || !loadedMatches() || !_engine.IsReady) return false;
                        _admitted = true;
                        return true;
                    }
                }
                finally { _exclusive.Release(); }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        }, false, cancellationToken, ticket, preparation: true);
    }

    public Task<string> GenerateAsync(SuggestionRequest request, CancellationToken cancellationToken)
    {
        Ticket ticket;
        lock (_sync)
        {
            if (_closed || !_local || !_admitted || !_engine.IsReady) return Task.FromResult("");
            ticket = new(_epoch, _modelId);
        }
        return Track(async token =>
        {
            try
            {
                await _exclusive.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!IsCurrent(ticket) || !IsReady) return "";
                    var result = await _engine.GenerateAsync(request, token).ConfigureAwait(false);
                    return IsCurrent(ticket) && IsReady && !token.IsCancellationRequested ? result : "";
                }
                finally { _exclusive.Release(); }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ""; }
        }, "", cancellationToken, ticket);
    }

    private bool Current(Ticket ticket) => !_closed && _local && ticket.ModelId is not null
        && ticket.Epoch == _epoch && string.Equals(ticket.ModelId, _modelId, StringComparison.Ordinal);

    private Task<T> Track<T>(Func<CancellationToken, Task<T>> action, T closedValue, CancellationToken cancellationToken, Ticket ticket, bool preparation = false)
    {
        lock (_sync)
        {
            if (!Current(ticket)) return Task.FromResult(closedValue);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _epochCancellation.Token);
            var task = Task.Run(async () => { using (linked) return await action(linked.Token).ConfigureAwait(false); });
            _work.Add(task);
            if (preparation) _preparations++;
            _ = RemoveWhenFinishedAsync(task, preparation);
            return task;
        }
    }
    private async Task RemoveWhenFinishedAsync(Task task, bool preparation)
    {
        try { await task.ConfigureAwait(false); } catch { }
        finally { lock (_sync) { _work.Remove(task); if (preparation) _preparations--; } }
    }

    /// <summary>Close admission first, then join all accepted work before the runtime is freed.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        TaskCompletionSource retirement;
        CancellationTokenSource retired;
        Task[] work;
        lock (_sync)
        {
            if (_disposal is not null) return new(_disposal);
            _closed = true; _epoch++; _admitted = false;
            retired = _epochCancellation; retirement = RegisterRetirementLocked(); work = _work.ToArray();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = completion.Task;
        }
        Retire(retired, retirement);
        _ = DrainAsync(work, completion);
        return new(completion.Task);
    }
    private async Task DrainAsync(Task[] work, TaskCompletionSource completion)
    {
        try { try { await Task.WhenAll(work).ConfigureAwait(false); } catch { } _exclusive.Dispose(); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private TaskCompletionSource RegisterRetirementLocked()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(completion.Task); _ = RemoveWhenFinishedAsync(completion.Task, preparation: false);
        return completion;
    }
    private static void Retire(CancellationTokenSource source, TaskCompletionSource completion) => _ = CancelAndDisposeAsync(source, completion);
    private static async Task CancelAndDisposeAsync(CancellationTokenSource source, TaskCompletionSource completion)
    {
        try { await source.CancelAsync().ConfigureAwait(false); } catch (Exception) { }
        finally { source.Dispose(); completion.TrySetResult(); }
    }
}
