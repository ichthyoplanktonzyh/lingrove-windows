using Cotabby.Core.Context;

namespace Cotabby.Core.LanguageLearning;

public enum LearningPreviewCopyKind { Source, Translation }

/// <summary>
/// One explicit Copy intent retains the document shown by its button. It is not permission to
/// reread a field or to edit one. A reused WPF window/controller compares this receipt with its
/// current immutable document before passing literal text to the clipboard writer.
/// </summary>
public sealed record LearningPreviewCopyRequest(LearningPreviewState Document, LearningPreviewCopyKind Kind);

/// <summary>
/// Adapts Mac LearningSentenceCard.copyActions: captured source is copyable while translation
/// runs; only a complete nonempty translation is copyable. Error cards hide the details/footer.
/// Provisional prefixes never count.
/// Lexical annotation updates do not change either payload; another request or changed text does.
/// </summary>
public static class LearningPreviewCopyPolicy
{
    public static string? Text(LearningPreviewCopyRequest request, LearningPreviewState? current)
    {
        var shown = request.Document;
        if (current is null || shown.RequestId.Length == 0 || shown.RequestId != current.RequestId
            || !ClipboardTextCopyPolicy.IsValid(shown.Source)
            || shown.Options is null || shown.Options != current.Options
            || !string.Equals(shown.Source, current.Source, StringComparison.Ordinal)
            || !CopyableStatus(shown.Status) || !CopyableStatus(current.Status)) return null;
        string? text = request.Kind switch
        {
            LearningPreviewCopyKind.Source => shown.Source,
            LearningPreviewCopyKind.Translation when shown.Status == LearningPreviewStatus.Ready
                && current.Status == LearningPreviewStatus.Ready
                && string.Equals(shown.Result, current.Result, StringComparison.Ordinal) => shown.Result,
            _ => null,
        };
        return ClipboardTextCopyPolicy.IsValid(text) ? text : null;
    }

    private static bool CopyableStatus(LearningPreviewStatus status) =>
        status is LearningPreviewStatus.Loading or LearningPreviewStatus.Ready;
}
