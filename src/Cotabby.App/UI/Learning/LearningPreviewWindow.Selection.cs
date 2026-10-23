using System.Windows;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.Learning;

/// <summary>Connects only the embedded explanation's literal text to the card's existing copy owner.</summary>
public partial class LearningPreviewWindow
{
    internal event Action<SelectedTextCopyLease, LearningPreviewState>? SelectedCopyRequested;

    private void InstallSelection()
    {
        AddHandler(NonactivatingSelectableText.CopyRequestedEvent, new EventHandler<SelectedTextCopyEventArgs>((_, args) =>
        {
            args.Handled = true;
            if (IsVisible && _displayedPreview is { Status: LearningPreviewStatus.Ready } document
                && args.OriginalSource is NonactivatingSelectableText text && text.IsVisible
                && (ReferenceEquals(text, ExplanationMeaning) || ReferenceEquals(text, ExplanationText))
                && args.Lease.IsCurrent && Equals(args.Lease.DocumentIdentity, document.RequestId))
                SelectedCopyRequested?.Invoke(args.Lease, document);
        }));
    }

    internal void ShowSelectedCopyOutcome(SelectedTextCopyLease lease, bool copied)
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || !lease.IsCurrent || _displayedPreview is not { Status: LearningPreviewStatus.Ready } document
            || !Equals(lease.DocumentIdentity, document.RequestId)) return;
        CopyStatus.Text = copied ? "Copied." : "Could not copy. The clipboard may be busy; try again.";
        CopyStatus.Visibility = Visibility.Visible;
        PresentAt(_copyAnchor);
    }
}
