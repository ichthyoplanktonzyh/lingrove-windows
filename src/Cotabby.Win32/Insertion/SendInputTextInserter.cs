using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Win32.Clipboard;
using Microsoft.Extensions.Logging;

namespace Cotabby.Win32.Insertion;

/// <summary>
/// Compatibility-facing <see cref="ITextInserter"/> registered by the WPF app. Core's
/// <see cref="GuardedTextInserter"/> owns serialization, fresh-field policy, validation, and
/// cancellation ordering; this facade supplies the shared Windows focus tracker and platform
/// adapter while keeping existing macro, emoji, and suggestion callers on one guarded path.
/// </summary>
public sealed class SendInputTextInserter : ITextInserter
{
    private readonly ILogger<SendInputTextInserter> _logger;
    private readonly GuardedTextInserter _guardedInserter;

    public SendInputTextInserter(
        ILogger<SendInputTextInserter> logger,
        IFocusTracker focus,
        ClipboardTextReader? clipboardContext = null,
        InsertionTransactionGate? transactionGate = null)
    {
        _logger = logger;
        _guardedInserter = new GuardedTextInserter(new Win32InsertionBackend(focus, clipboardContext), transactionGate);
    }

    public async Task<bool> InsertAsync(FocusedField target, string text, CancellationToken ct)
    {
        bool inserted = await _guardedInserter.InsertAsync(target, text, ct).ConfigureAwait(false);
        LogRejectedEdit(inserted, target);
        return inserted;
    }

    public async Task<bool> ReplaceAsync(FocusedField target, int deleteCount, string text, CancellationToken ct)
    {
        bool replaced = await _guardedInserter.ReplaceAsync(target, deleteCount, text, ct).ConfigureAwait(false);
        LogRejectedEdit(replaced, target);
        return replaced;
    }

    private void LogRejectedEdit(bool succeeded, FocusedField target)
    {
        if (!succeeded)
        {
            _logger.LogWarning(
                "Guarded text insertion was rejected or native dispatch was incomplete. target.pid={Pid}",
                target.ProcessId);
        }
    }
}
