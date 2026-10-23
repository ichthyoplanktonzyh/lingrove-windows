using Cotabby.App.UI.Controls;
using Cotabby.Core.Context;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Learning;

/// <summary>
/// Frozen selected prose/example plus its parent translation and optional dictionary target.
/// The view supplies only captured values, not a TextPointer or permission to edit an editor.
/// </summary>
internal sealed record LearningSelectedCopyRequest(SelectedTextCopyLease Lease, LearningPreviewState Document, Guid? DictionaryTarget = null);

/// <summary>
/// Portable identity projection shared by App's initial and final clipboard grants. The parent
/// translation must remain exact and complete; dictionary selections also stay on their original
/// contextual page/target. Focus/intent/lifetime authority remains in the App controller.
/// </summary>
internal static class LearningSelectedCopyPolicy
{
    public static bool IsCurrent(LearningSelectedCopyRequest request, LearningPreviewState? current, LearningDictionaryState? dictionary = null)
    {
        if (!request.Lease.IsCurrent || !ClipboardTextCopyPolicy.IsValid(request.Lease.Text)
            || !LearningExplanationCardPolicy.SameDocument(request.Document, current)) return false;
        if (request.DictionaryTarget is not { } target)
            return request.Lease.DocumentIdentity is string documentId && documentId == request.Document.RequestId;
        return target != Guid.Empty && Equals(request.Lease.DocumentIdentity, target)
            && dictionary is { Unit: not null, HasContextualExplanation: true, Page: LearningDictionaryPage.Contextual }
            && dictionary.TargetIdentity == target && dictionary.DocumentId == request.Document.RequestId;
    }
}
