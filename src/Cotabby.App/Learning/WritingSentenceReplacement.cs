using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Learning;

internal enum WritingReplacementOutcome { Rejected, Verified, Unverified }

/// <summary>One bounded caret-ending sentence edit. Implementations must not interpret native
/// dispatch as acknowledgement or retry an ambiguous edit.</summary>
internal interface IWritingSentenceReplacement : IAsyncDisposable
{
    Task<WritingReplacementOutcome> ReplaceAsync(FocusedField field, LearningTextRange range, string source,
        string replacement, Func<bool> permit, CancellationToken cancellationToken);
}

/// <summary>App-owned transaction coordinator borrowing the existing shared inserter and focus
/// owner. The predicate is caller-owned, atomic and nonblocking; guarded reads run off the UI thread.</summary>
internal sealed class WritingSentenceReplacement(IConditionalTextInserter inserter, Func<Func<bool>, FocusedField?> refresh,
    Func<TimeSpan, CancellationToken, Task>? wait = null) : IWritingSentenceReplacement
{
    private readonly object _gate = new(); private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<Task> _work = []; private bool _closed; private Task? _dispose;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait = wait ?? Task.Delay;
    public Task<WritingReplacementOutcome> ReplaceAsync(FocusedField field, LearningTextRange range, string source,
        string replacement, Func<bool> permit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permit);
        var completion = new TaskCompletionSource<WritingReplacementOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) { if (_closed) return Task.FromResult(WritingReplacementOutcome.Rejected); _work.Add(completion.Task); }
        _ = CompleteAsync(); return completion.Task;
        async Task CompleteAsync()
        {
            try { completion.TrySetResult(await RunAsync(field, range, source, replacement, permit, cancellationToken).ConfigureAwait(false)); }
            catch { completion.TrySetResult(WritingReplacementOutcome.Unverified); }
            finally { lock (_gate) _work.Remove(completion.Task); }
        }
    }
    private async Task<WritingReplacementOutcome> RunAsync(FocusedField field, LearningTextRange range, string source,
        string replacement, Func<bool> permit, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        bool Allowed() { try { return !linked.IsCancellationRequested && permit(); } catch { return false; } }
        if (!Allowed() || !WritingSentenceReplacementPolicy.CanReplace(field, range, source, replacement)) return WritingReplacementOutcome.Rejected;
        bool dispatched;
        try { dispatched = await inserter.ReplaceWithPermitAsync(field, range.Length, replacement, Allowed, linked.Token).ConfigureAwait(false); }
        catch { return WritingReplacementOutcome.Unverified; }
        // False may follow a partial native batch. It is not safe to retry or report rejection.
        if (!dispatched) return WritingReplacementOutcome.Unverified;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!Allowed()) return WritingReplacementOutcome.Unverified;
            try { await _wait(TimeSpan.FromMilliseconds(60), linked.Token).ConfigureAwait(false); }
            catch { return WritingReplacementOutcome.Unverified; }
            if (!Allowed()) return WritingReplacementOutcome.Unverified;
            FocusedField? current;
            try { current = await Task.Run(() => refresh(Allowed), CancellationToken.None).ConfigureAwait(false); }
            catch { return WritingReplacementOutcome.Unverified; }
            if (!Allowed()) return WritingReplacementOutcome.Unverified;
            if (WritingSentenceReplacementPolicy.IsAcknowledged(field, range, source, replacement, current)) return WritingReplacementOutcome.Verified;
        }
        return WritingReplacementOutcome.Unverified;
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_dispose is not null) return new(_dispose);
            _closed = true; var cancel = _stop.CancelAsync(); return new(_dispose = DrainAsync(cancel, _work.ToArray()));
        }
    }
    private async Task DrainAsync(Task cancel, Task[] work)
    {
        try { try { await cancel.ConfigureAwait(false); } catch { } await Task.WhenAll(work).ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
