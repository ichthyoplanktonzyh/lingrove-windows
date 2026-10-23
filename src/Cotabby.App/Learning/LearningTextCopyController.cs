using Cotabby.Core.Context;

namespace Cotabby.App.Learning;

/// <summary>
/// Owns at most one explicit clipboard attempt for the app's sentence card. Native clipboard
/// notifications can wait on another process, so IO runs on a worker, never WPF or the hook thread.
/// The caller supplies a quick final captured-document grant; the writer asks for it only after
/// exclusive open and before mutation. Busy requests fail immediately, with no deferred retry.
/// Disposing closes admission without joining an uninterruptible, already-admitted OS transaction.
/// This owner has no window/editor/runtime and borrows the process clipboard writer.
/// </summary>
internal sealed class LearningTextCopyController(IClipboardTextWriter writer) : IDisposable
{
    private readonly object _gate = new();
    private bool _inProgress;
    private bool _disposed;

    public Task<bool> CopyAsync(string text, Func<bool> tryCommit)
    {
        if (!ClipboardTextCopyPolicy.IsValid(text)) return Task.FromResult(false);
        lock (_gate)
        {
            if (_disposed || _inProgress) return Task.FromResult(false);
            _inProgress = true;
        }
        return Task.Run(() =>
        {
            try
            {
                return writer.TryWrite(text, () =>
                {
                    // Only a short authorization callback runs under this lock. Never keep it
                    // held through EmptyClipboard: the previous owner's notifications can block.
                    lock (_gate) return !_disposed && tryCommit();
                });
            }
            catch (Exception) { return false; } // Native/provider messages must not enter the card.
            finally { lock (_gate) _inProgress = false; }
        });
    }

    public void Dispose() { lock (_gate) _disposed = true; }
}
