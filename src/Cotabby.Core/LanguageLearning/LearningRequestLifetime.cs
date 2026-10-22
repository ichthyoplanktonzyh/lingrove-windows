namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// A captured learning request's cancellation authority, separate from the card and focus monitor.
/// The controller retires it atomically with settings/intent changes; its engine facade owns final
/// disposal after native work drains. Token is cached so late checks never read a disposed source.
/// </summary>
public sealed class LearningRequestLifetime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private Task? _retirement;
    private Task? _disposal;

    public LearningRequestLifetime() => Token = _source.Token;

    public CancellationToken Token { get; }

    /// <summary>
    /// CancelAsync sets IsCancellationRequested before returning and runs callbacks asynchronously.
    /// That makes retirement safe inside the controller's admission lock: native preparation sees
    /// the same direct token immediately, while arbitrary consumer callbacks stay outside the lock.
    /// </summary>
    public Task Retire()
    {
        lock (_gate) return _retirement ??= _source.CancelAsync();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_disposal ??= DisposeCoreAsync(Retire()));
    }

    private async Task DisposeCoreAsync(Task retirement)
    {
        try { await retirement.ConfigureAwait(false); }
        finally { _source.Dispose(); }
    }
}
