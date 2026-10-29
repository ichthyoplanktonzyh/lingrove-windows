using Cotabby.Core.Focus;

namespace Cotabby.Core.Insertion;

/// <summary>
/// Serializes and guards accepted text edits for the lifetime of the app. The inserter re-reads
/// focus after entering its transaction gate, then coordinates the platform backend; clipboard
/// preparation and conditional restoration remain owned by the backend's lease.
/// </summary>
/// <remarks>
/// Register one instance per app. A single gate prevents two accepts from interleaving their
/// validation, clipboard lifetime, and native edit batches. This type contains no OS calls itself.
/// </remarks>
public sealed class GuardedTextInserter : ITextInserter
{
    private readonly ITextInsertionBackend _backend;
    private readonly InsertionTransactionGate _transactionGate;

    public GuardedTextInserter(ITextInsertionBackend backend, InsertionTransactionGate? transactionGate = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _transactionGate = transactionGate ?? new InsertionTransactionGate();
    }

    /// <inheritdoc />
    public Task<bool> InsertAsync(FocusedField target, string text, CancellationToken ct) =>
        ExecuteAsync(target, deleteUtf16Count: 0, text, isReplacement: false, ct);

    /// <inheritdoc />
    public Task<bool> ReplaceAsync(FocusedField target, int deleteCount, string text, CancellationToken ct) =>
        ExecuteAsync(target, deleteCount, text, isReplacement: true, ct);

    private async Task<bool> ExecuteAsync(
        FocusedField target,
        int deleteUtf16Count,
        string text,
        bool isReplacement,
        CancellationToken ct)
    {
        try
        {
            return await _transactionGate.RunAsync(
                token => ExecuteUnderGateAsync(target, deleteUtf16Count, text, isReplacement, token), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return false; }
    }

    private async Task<bool> ExecuteUnderGateAsync(
        FocusedField target, int deleteUtf16Count, string text, bool isReplacement, CancellationToken ct)
    {
        bool succeeded = false;
        bool pasteMayHaveBeenDispatched = false;
        IClipboardPasteLease? lease = null;

        try
        {
            bool validEdit = isReplacement
                ? InsertionTargetPolicy.IsValidReplacement(target, deleteUtf16Count, text)
                : InsertionTargetPolicy.IsEligibleField(target) && InsertionTargetPolicy.IsValidInsertionText(text);
            if (!validEdit) return false;

            // The caller's snapshot may have gone stale while this operation waited behind another
            // accept, so always query the OS again after entering the serialized transaction.
            if (ct.IsCancellationRequested) return false;
            if (!InsertionTargetPolicy.IsCurrentTarget(target, _backend.ReadFocusedField())) return false;

            // No native call is needed for a zero-length insert or replacement.
            if (text.Length == 0 && deleteUtf16Count == 0)
            {
                succeeded = true;
            }
            else if (!ct.IsCancellationRequested && _backend.CanType(text))
            {
                // Native input is synchronous and indivisible from the caller's cancellation point.
                // Replacement is passed as one combined batch so a failed insertion cannot leave a
                // separately dispatched deletion behind.
                if (!ct.IsCancellationRequested)
                {
                    succeeded = _backend.SendUnicodeEdit(target, deleteUtf16Count, text);
                }
            }
            else if (!ct.IsCancellationRequested)
            {
                // The clipboard is prepared before any destructive key input. It remains leased
                // through the paste wait and is conditionally restored on every exit path.
                lease = await _backend.PreparePasteAsync(text, ct).ConfigureAwait(false);
                if (lease is null || ct.IsCancellationRequested) return false;

                if (!InsertionTargetPolicy.IsCurrentTarget(target, _backend.ReadFocusedField())) return false;
                if (ct.IsCancellationRequested) return false;

                // Keep the ownership check adjacent to the one native paste batch. If it no longer
                // owns the clipboard, another application has copied data and this edit is refused.
                if (!lease.IsCurrent) return false;
                if (ct.IsCancellationRequested) return false;
                pasteMayHaveBeenDispatched = true;
                succeeded = _backend.SendPasteEdit(target, deleteUtf16Count, lease);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation before dispatch is a normal rejection. If it races native dispatch, the
            // false result remains deliberately ambiguous and callers must not retry automatically.
            succeeded = false;
        }
        catch (Exception)
        {
            // A platform failure can happen after input has partially reached the host. Report only
            // complete dispatch as success and leave retry/rollback decisions to higher-level UX.
            succeeded = false;
        }
        finally
        {
            if (lease is not null)
            {
                // A false return or exception from SendPasteEdit may still mean Ctrl+V reached the
                // host. Wait before restoring the clipboard in that uncertain case as well.
                if (pasteMayHaveBeenDispatched)
                {
                    try
                    {
                        await _backend.WaitForPasteAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        succeeded = false;
                    }
                }

                try
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Cleanup failure means the transaction did not complete with a known result.
                    succeeded = false;
                }
            }

        }

        return succeeded;
    }
}
