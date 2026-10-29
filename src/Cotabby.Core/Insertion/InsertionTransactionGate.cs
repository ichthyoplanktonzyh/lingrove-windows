namespace Cotabby.Core.Insertion;

/// <summary>
/// Shared app-lifetime serialization only. A holder gains no editor authority; each operation
/// remains responsible for fresh identity, selection, composition and dispatch validation.
/// </summary>
public sealed class InsertionTransactionGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await operation(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
