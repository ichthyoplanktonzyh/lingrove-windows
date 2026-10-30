using System.Windows;
using Cotabby.App.Localization;
using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Contextual text emits a target-bound selected-copy intent. The view keeps clipboard effects
/// and editor authority in App; dictionary definitions/links retain their existing interaction.
/// </summary>
public partial class LearningDictionaryWindow
{
    internal event Action<SelectedTextCopyLease, LearningDictionaryState>? SelectedCopyRequested;

    private void InstallSelection()
    {
        AddHandler(NonactivatingSelectableText.CopyRequestedEvent, new EventHandler<SelectedTextCopyEventArgs>((_, args) =>
        {
            args.Handled = true;
            if (IsVisible && _state is { Page: LearningDictionaryPage.Contextual } state
                && args.OriginalSource is NonactivatingSelectableText text && text.IsVisible
                && (ReferenceEquals(text, ContextualMeaning) || ReferenceEquals(text, ContextualExampleText)
                    || ReferenceEquals(text, ContextualExampleTranslation) || ReferenceEquals(text, ExplanationProse))
                && args.Lease.IsCurrent && Equals(args.Lease.DocumentIdentity, state.TargetIdentity))
                SelectedCopyRequested?.Invoke(args.Lease, state);
        }));
    }

    private void SetSelectionTarget(Guid target, LearningDictionaryPage page)
    {
        ContextualMeaning.DocumentIdentity = ContextualExampleText.DocumentIdentity =
            ContextualExampleTranslation.DocumentIdentity = ExplanationProse.DocumentIdentity = target;
        if (_state?.TargetIdentity != target || _state?.Page != page)
        { CopyStatus.Text = string.Empty; CopyStatus.Visibility = Visibility.Collapsed; }
    }

    internal void ShowSelectedCopyOutcome(SelectedTextCopyLease lease, bool copied)
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || !lease.IsCurrent || _state is not { Page: LearningDictionaryPage.Contextual } state
            || !Equals(lease.DocumentIdentity, state.TargetIdentity)) return;
        CopyStatus.Text = InterfaceText.Get(copied ? "Copied" : "Could not copy. The clipboard may be busy; try again.");
        CopyStatus.Visibility = Visibility.Visible;
        UpdateLayout(); LearningPanelPlacement.Position(this, _anchor);
    }
}
