using Cotabby.Core.Focus;

namespace Cotabby.Core.Insertion;

/// <summary>
/// The narrow platform boundary used by <see cref="GuardedTextInserter"/> to observe a focused
/// field and dispatch an edit. Implementations own every operating-system effect; Core owns the
/// ordering and safety checks around those effects.
/// </summary>
public interface ITextInsertionBackend
{
    /// <summary>Read a new immutable snapshot of whichever editable field currently has focus.</summary>
    FocusedField? ReadFocusedField();

    /// <summary>Return whether <paramref name="text"/> can be dispatched as Unicode input.</summary>
    bool CanType(string text);

    /// <summary>
    /// Dispatch deletion and insertion together as one native input batch. A false result or
    /// exception may follow a partial dispatch and must not be treated as proof that nothing changed.
    /// </summary>
    bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text);

    /// <summary>
    /// Build and dispatch the prepared clipboard paste and any deletion together as one native
    /// input batch. The backend must check <paramref name="lease"/>.IsCurrent again immediately
    /// before native dispatch, after any batch construction that could race a user copy. A false
    /// result or exception may follow a partial dispatch.
    /// </summary>
    bool SendPasteEdit(FocusedField target, int deleteUtf16Count, IClipboardPasteLease lease);

    /// <summary>
    /// Prepare the clipboard for a paste without changing the focused field. The returned lease
    /// owns conditional restoration of the prior clipboard and must always be disposed.
    /// </summary>
    Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct);

    /// <summary>
    /// Give the host time to consume the clipboard after a paste batch may have been dispatched.
    /// This wait is intentionally independent of the caller's cancellation token.
    /// </summary>
    Task WaitForPasteAsync();
}

/// <summary>Opt-in final-admission boundary. The predicate must be atomic and nonblocking;
/// implementations carry it through native reads and the final dispatch.</summary>
public interface IConditionalTextInsertionBackend
{
    FocusedField? ReadFocusedField(Func<bool> permit);
    bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text, Func<bool> permit);
    bool SendPasteEdit(FocusedField target, int deleteUtf16Count, IClipboardPasteLease lease, Func<bool> permit);
}
