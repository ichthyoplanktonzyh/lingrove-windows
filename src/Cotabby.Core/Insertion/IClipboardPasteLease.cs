namespace Cotabby.Core.Insertion;

/// <summary>
/// Owns the temporary clipboard contents prepared for one paste transaction. The platform
/// implementation reports whether it still owns the clipboard before dispatch, and restores saved
/// formats only when that ownership remains current.
/// </summary>
public interface IClipboardPasteLease : IAsyncDisposable
{
    /// <summary>True only while this lease still owns the clipboard contents it prepared.</summary>
    bool IsCurrent { get; }
}
