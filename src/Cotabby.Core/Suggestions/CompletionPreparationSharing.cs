namespace Cotabby.Core.Suggestions;

/// <summary>Shares one selection-owned preparation per epoch. Request cancellation belongs to
/// each waiter, while the underlying admission owner retires and drains the preparation.</summary>
public sealed class CompletionPreparationSharing
{
    private readonly object _gate = new();
    private long _epoch = -1;
    private Task<bool>? _task;
    public Task<bool> GetOrStart(long epoch, Func<Task<bool>> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        lock (_gate)
        {
            if (_task is null || _epoch != epoch || _task.IsCompleted)
            { var task = start(); _epoch = epoch; _task = task; }
            return _task;
        }
    }
}
