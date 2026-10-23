namespace Cotabby.Core.Context;

/// <summary>
/// An explicit user Copy action, distinct from a temporary paste lease. The app lends this
/// process-owned OS boundary to readonly cards; success replaces the clipboard with plain text
/// and never sends a key, changes editor focus or schedules a later retry.
/// </summary>
public interface IClipboardTextWriter
{
    /// <summary>
    /// Calls <paramref name="tryCommit"/> exactly once after exclusive open and before EmptyClipboard.
    /// False revokes a queued attempt without mutation. True is the admission point: that in-flight
    /// native transaction must finish/close even if the originating card subsequently retires.
    /// The callback must be quick and cannot read cross-process UIA or perform clipboard IO.
    /// </summary>
    bool TryWrite(string text, Func<bool> tryCommit);
}
