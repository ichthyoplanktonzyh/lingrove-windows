using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Review;

/// <summary>Presentation queue state from the Mac review sheet. Stable notebook/card IDs and
/// durable bindings remain the authority; status never identifies a note or authorizes a write.</summary>
public enum ReviewBatchStatus { Ready, VerifyAudio, Sending, Synced, Attention, Failed, Cancelled, Unavailable, Skipped }

/// <summary>Original source labels, kept separate from future Windows localization resources.</summary>
public static class ReviewBatchStatuses
{
    public static string Label(this ReviewBatchStatus status) => status switch
    {
        ReviewBatchStatus.Ready => "Ready to send", ReviewBatchStatus.VerifyAudio => "Verify audio",
        ReviewBatchStatus.Sending => "Sending…", ReviewBatchStatus.Synced => "Synced",
        ReviewBatchStatus.Attention => "Needs preparation", ReviewBatchStatus.Failed => "Send failed",
        ReviewBatchStatus.Cancelled => "Stopped", ReviewBatchStatus.Unavailable => "No longer in the notebook",
        ReviewBatchStatus.Skipped => "Merged into another selected sense", _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

/// <summary>One frozen-selection row owns a window-local ID, display snapshot and progress.
/// Immutable replacements retain that ID; resolving its Selection against Store follows merged
/// aliases. A batch owns the row for its window lifetime, independently of a single-sense draft.</summary>
public sealed record ReviewBatchItem(VocabularySelection Selection, string Expression, string Meaning)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? CardId { get; init; }
    public ReviewBatchStatus Status { get; init; } = ReviewBatchStatus.Ready;
    public string? Message { get; init; }
}
