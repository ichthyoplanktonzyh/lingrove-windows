using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Mac's explicit source/translation Copy footer, using buttons bound to the immutable document
/// that created them. An equal document keeps its buttons across lexical/streaming updates; a new
/// document replaces them, so an old button can only emit its old receipt and is rejected here.
/// The view emits intents and feedback only; AppHost owns the native clipboard writer.
/// </summary>
public partial class LearningPreviewWindow
{
    private LearningPreviewCopyRequest? _sourceCopy;
    private LearningPreviewCopyRequest? _translationCopy;
    private ScreenRect _copyAnchor;
    public event Action<LearningPreviewCopyRequest>? CopyRequested;

    private void PresentCopyActions(LearningPreviewState state, ScreenRect anchor)
    {
        _copyAnchor = anchor;
        bool sourceRetained = Retains(_sourceCopy, state);
        bool translationRetained = Retains(_translationCopy, state);
        var source = sourceRetained ? _sourceCopy : Request(LearningPreviewCopyKind.Source);
        var translation = translationRetained ? _translationCopy : Request(LearningPreviewCopyKind.Translation);
        if (source == _sourceCopy && translation == _translationCopy) return;
        _sourceCopy = source; _translationCopy = translation;
        CopyActions.Children.Clear();
        if (source is not null) CopyActions.Children.Add(CopyButton(source, "Copy source text"));
        if (translation is not null) CopyActions.Children.Add(CopyButton(translation, "Copy translation"));
        CopyActions.Visibility = CopyActions.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CopyStatus.Text = string.Empty; CopyStatus.Visibility = Visibility.Collapsed;

        LearningPreviewCopyRequest? Request(LearningPreviewCopyKind kind)
        {
            var request = new LearningPreviewCopyRequest(state, kind);
            return LearningPreviewCopyPolicy.Text(request, state) is null ? null : request;
        }
    }

    private static bool Retains(LearningPreviewCopyRequest? request, LearningPreviewState state) =>
        request is not null && LearningPreviewCopyPolicy.Text(request, state) is not null;

    private Button CopyButton(LearningPreviewCopyRequest request, string label)
    {
        var button = new Button { Content = label, Focusable = false, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 0) };
        button.SetResourceReference(Control.BackgroundProperty, "KeycapBg");
        button.SetResourceReference(Control.BorderBrushProperty, "CardBorder");
        button.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            if (IsVisible && LearningPreviewCopyPolicy.Text(request, _displayedPreview) is not null)
                CopyRequested?.Invoke(request);
        };
        return button;
    }

    internal void ShowCopyOutcome(LearningPreviewCopyRequest request, bool copied)
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || LearningPreviewCopyPolicy.Text(request, _displayedPreview) is null) return;
        CopyStatus.Text = copied ? "Copied." : "Could not copy. The clipboard may be busy; try again.";
        CopyStatus.Visibility = Visibility.Visible;
        PresentAt(_copyAnchor);
    }

    private void ClearCopyActions()
    {
        _sourceCopy = _translationCopy = null;
        CopyActions.Children.Clear(); CopyActions.Visibility = Visibility.Collapsed;
        CopyStatus.Text = string.Empty; CopyStatus.Visibility = Visibility.Collapsed;
    }
}
