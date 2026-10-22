using Cotabby.Core.Focus;

namespace Cotabby.Core.Insertion;

/// <summary>
/// Dispatches an accepted text edit into the field snapshot supplied by the caller.
/// Implementations must revalidate that snapshot because focus and selection can change while
/// generation is running.
/// </summary>
public interface ITextInserter
{
    /// <summary>
    /// Insert <paramref name="text"/> at the caret of <paramref name="target"/>.
    /// True means a validated empty no-op or one complete native batch dispatch; it does not
    /// acknowledge the editor's final text. False can mean either rejection
    /// or a partial/uncertain dispatch, so callers must not automatically retry or roll back the edit.
    /// Implementations must serialize concurrent calls because accepts can arrive in quick
    /// succession.
    /// </summary>
    Task<bool> InsertAsync(FocusedField target, string text, CancellationToken ct);

    /// <summary>
    /// Delete <paramref name="deleteCount"/> UTF-16 units before the caret and insert
    /// <paramref name="text"/> in their place as one native edit batch. A zero-delete, empty-text
    /// replacement is a successful no-op; otherwise, true means the complete batch was dispatched.
    /// False can mean either rejection or a partial/uncertain dispatch; callers must not
    /// automatically retry or roll back the edit.
    /// </summary>
    Task<bool> ReplaceAsync(FocusedField target, int deleteCount, string text, CancellationToken ct);
}
