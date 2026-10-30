using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Insertion;

/// <summary>Range effects borrow the existing insertion gate and clipboard lifetime. Preparation
/// precedes selection; the native owner verifies the selected range and dispatches without returning
/// a live native range to App. This owner supplies ordering, not host-specific range arithmetic.</summary>
public sealed class GuardedRangeTextInserter(ITextInsertionBackend backend, IRangeSelectionDispatcher selections,
    InsertionTransactionGate transactionGate) : IRangeTextInserter
{
    public async Task<RangeEditDispatch> ReplaceRangeAsync(FocusedField target, LearningTextRange range, string source,
        string replacement, Func<bool> permit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permit);
        try
        {
            return await transactionGate.RunAsync(async token =>
            {
                bool Allowed() { try { return !token.IsCancellationRequested && permit(); } catch { return false; } }
                if (backend is not IConditionalTextInsertionBackend conditional || !Allowed()
                    || !WritingRangeReplacementPolicy.CanReplace(target, range, source, replacement)
                    || !WritingRangeReplacementPolicy.IsCurrent(target, conditional.ReadFocusedField(Allowed))) return RangeEditDispatch.Rejected;
                IClipboardPasteLease? lease = null;
                bool pasteAttempted = false, dispatchAttempted = false, selectionAttempted = false;
                var result = RangeEditDispatch.Rejected;
                try
                {
                    bool typed = backend.CanType(replacement);
                    if (!typed)
                    {
                        lease = await backend.PreparePasteAsync(replacement, token).ConfigureAwait(false);
                        if (lease is null || !Allowed() || !lease.IsCurrent) return RangeEditDispatch.Rejected;
                    }
                    if (!Allowed() || !WritingRangeReplacementPolicy.IsCurrent(target, conditional.ReadFocusedField(Allowed))) return RangeEditDispatch.Rejected;
                    bool Send(Func<bool> selectionPermit)
                    {
                        bool FinalAllowed() => Allowed() && selectionPermit() && (typed || lease?.IsCurrent == true);
                        if (dispatchAttempted || !FinalAllowed()) return false;
                        dispatchAttempted = true;
                        if (typed) return conditional.SendUnicodeEdit(target, 0, replacement, FinalAllowed);
                        pasteAttempted = true;
                        return conditional.SendPasteEdit(target, 0, lease!, FinalAllowed);
                    }
                    selectionAttempted = true;
                    result = await selections.SelectAndDispatchAsync(target, range, source, Allowed, Send).ConfigureAwait(false);
                }
                catch { result = selectionAttempted ? RangeEditDispatch.Uncertain : RangeEditDispatch.Rejected; }
                finally
                {
                    if (lease is not null)
                    {
                        if (pasteAttempted)
                            try { await backend.WaitForPasteAsync().ConfigureAwait(false); } catch { result = RangeEditDispatch.Uncertain; }
                        try { await lease.DisposeAsync().ConfigureAwait(false); } catch { result = RangeEditDispatch.Uncertain; }
                    }
                }
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return RangeEditDispatch.Rejected; }
    }
}

/// <summary>On the shared native UIA owner, revalidate the original field/value/selection, select
/// the exact range, acknowledge it and invoke dispatch at most once while the permit remains live.
/// Implementations must revoke queued effects on timeout and join any already accepted dispatch.</summary>
public interface IRangeSelectionDispatcher
{
    Task<RangeEditDispatch> SelectAndDispatchAsync(FocusedField target, LearningTextRange range, string source,
        Func<bool> permit, Func<Func<bool>, bool> dispatch);
}
