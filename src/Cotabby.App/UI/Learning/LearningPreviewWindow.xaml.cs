using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Cotabby.App.UI;
using Cotabby.App.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.App.Vocabulary;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Displays one readonly translation result beside the exact editor caret that supplied it. The
/// WPF window owns presentation only; its controller retains the field snapshot, request identity
/// and cancellation, just as the Mac coordinator owns capture while LearningSentencePanel renders.
/// </summary>
/// <remarks>
/// <c>WS_EX_NOACTIVATE</c> keeps typing in the host field while buttons remain clickable. UIA
/// rectangles are physical pixels, so placement uses SetWindowPos rather than WPF's primary-screen
/// DIPs; the caret is never rediscovered after the request starts.
/// </remarks>
public partial class LearningPreviewWindow : Window
{
    private LearningPreviewState? _displayedPreview;
    private LearningExplanationState? _displayedExplanation;

    public LearningPreviewWindow()
    {
        InitializeComponent();
        FavoriteButton.FailureChanged += message =>
        { VocabularyStatus.Text = message ?? ""; VocabularyStatus.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible; };
        InstallSelection();
        WindowTheme.Follow(this);
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
    }

    public event Action? RetryRequested;
    public event Action? CancelRequested;
    public event Action? DismissRequested;
    public event Action<LearningUnit, string, ScreenRect>? LookupRequested;
    public event Action<string, LearningExplanationState>? ExplanationRequested;

    /// <summary>Shows a coordinator snapshot while leaving the host application active.</summary>
    public void ShowState(LearningPreviewState state, ScreenRect anchor, bool canRetry)
    {
        Dispatcher.VerifyAccess();
        if (!LearningExplanationCardPolicy.SameDocument(_displayedPreview, state)) { ClearExplanation(); FavoriteButton.Clear(); }
        if (state.Status != LearningPreviewStatus.Ready) FavoriteButton.Clear();
        _displayedPreview = state;
        PresentCopyActions(state, anchor);
        // Mac's error branch replaces the details body. Keep the captured source out of that
        // presentation as well as its Copy footer; retry still belongs to the controller capture.
        SourceSection.Visibility = state.Status == LearningPreviewStatus.Failed ? Visibility.Collapsed : Visibility.Visible;
        SourceText.Text = state.Source;
        DirectionText.Text = state.Options is { } options
            ? $"{options.SourceLanguage.Label()}  →  {options.TargetLanguage.Label()}"
            : "";
        DirectionText.Visibility = DirectionText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool loading = state.Status == LearningPreviewStatus.Loading;
        bool ready = state.Status == LearningPreviewStatus.Ready && state.Result is not null;
        string? visibleTranslation = ready ? state.Result : loading ? state.PartialResult : null;
        bool hasProvisionalText = loading && !string.IsNullOrEmpty(visibleTranslation);
        TitleText.Text = "Sentence translation";
        StatusText.Text = loading ? "Translating…" : ready ? "Translation preview" : "Could not translate";
        Progress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ResultSection.Visibility = ready || hasProvisionalText ? Visibility.Visible : Visibility.Collapsed;
        ResultLabel.Text = hasProvisionalText ? "TRANSLATION · PREVIEW" : "TRANSLATION";
        PresentTranslation(state, visibleTranslation ?? string.Empty);
        SourceText.FlowDirection = state.Options?.SourceLanguage == LearningLanguage.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ErrorText.Text = ready ? "" : state.ErrorMessage ?? "";
        ErrorText.Visibility = ErrorText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = !loading && !ready && canRetry ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = loading ? "Cancel" : "Close";

        PresentAt(anchor);
    }

    private void PresentTranslation(LearningPreviewState state, string text)
    {
        ResultText.Inlines.Clear();
        ResultText.FlowDirection = state.Options?.TargetLanguage == LearningLanguage.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ResultText.ToolTip = state.Status == LearningPreviewStatus.Ready && state.UnitStage == LearningUnitStage.Ready
            ? "Double-click a word or expression to look it up" : null;
        if (state.Status != LearningPreviewStatus.Ready || state.UnitStage != LearningUnitStage.Ready)
        {
            ResultText.Inlines.Add(new Run(text));
            return;
        }
        foreach (var part in LearningUnitDisplayPolicy.Runs(text, state.Units))
        {
            var run = new Run(part.Text);
            if (part.Underlined)
                run.TextDecorations = new TextDecorationCollection
                {
                    new TextDecoration { Location = TextDecorationLocation.Underline,
                        Pen = new Pen((Brush)FindResource("TextSecondary"), 0.6) }
                };
            if (part.Target is { } unit && part.Word is not null)
            {
                run.MouseLeftButtonDown += (_, args) =>
                {
                    args.Handled = true;
                    if (args.ClickCount != 2) return;
                    var point = args.GetPosition(ResultText);
                    var hit = ResultText.GetPositionFromPoint(point, snapToText: false);
                    if (hit is null) return;
                    var bounds = hit.GetCharacterRect(LogicalDirection.Forward);
                    if (!bounds.Contains(point)) bounds = hit.GetCharacterRect(LogicalDirection.Backward);
                    if (bounds.IsEmpty || !bounds.Contains(point)) return;
                    var topLeft = ResultText.PointToScreen(bounds.TopLeft);
                    var bottomRight = ResultText.PointToScreen(bounds.BottomRight);
                    LookupRequested?.Invoke(unit, state.RequestId,
                        new ScreenRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y));
                };
            }
            ResultText.Inlines.Add(run);
        }
    }

    /// <summary>Shows a fixed capture-policy error when no sentence request could be created.</summary>
    public void ShowCaptureFailure(string message, ScreenRect anchor)
    {
        Dispatcher.VerifyAccess();
        _displayedPreview = null;
        FavoriteButton.Clear();
        ClearCopyActions();
        ClearExplanation();
        SourceSection.Visibility = Visibility.Collapsed;
        SourceText.Text = string.Empty;
        DirectionText.Visibility = Visibility.Collapsed;
        ResultSection.Visibility = Visibility.Collapsed;
        ResultText.Text = string.Empty;
        Progress.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        TitleText.Text = "Sentence preview unavailable";
        StatusText.Text = "No text was sent.";
        CancelButton.Content = "Close";
        PresentAt(anchor);
    }

    /// <summary>Hides without activating the WPF window or changing the host's input focus.</summary>
    public void HidePreview()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HidePreview);
            return;
        }

        Hide();
        FavoriteButton.Clear();
        _displayedPreview = null;
        ClearCopyActions();
        // This long-lived window is reused. Retiring the capture also drops the literal editor
        // and translation text from its visual tree rather than keeping a hidden old document.
        SourceText.Text = DirectionText.Text = ErrorText.Text = string.Empty;
        ResultText.Inlines.Clear();
        ResultText.ToolTip = null;
        ClearExplanation();
    }

    /// <summary>
    /// The controller supplies only a session snapshot that passed captured-field validation.
    /// This view renders plain model text, retaining the previous complete answer during refresh;
    /// it never parses markup, reads the editor or constructs a model/service.
    /// </summary>
    internal void ShowExplanationState(string documentId, LearningExplanationState state, bool configured, ScreenRect anchor)
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || _displayedPreview?.RequestId != documentId
            || _displayedPreview.Status != LearningPreviewStatus.Ready) return;
        _displayedExplanation = state;
        var action = LearningExplanationCardPolicy.Action(state, configured);
        string label = LearningExplanationCardPolicy.Label(state, configured);
        ExplanationButton.Visibility = action == LearningExplanationCardAction.None ? Visibility.Collapsed : Visibility.Visible;
        ExplanationButton.Content = state.IsGenerating ? "■" : "✦";
        ExplanationButton.ToolTip = state.ErrorMessage is not null && configured && !state.IsGenerating
            ? label + "\n" + state.ErrorMessage : label;
        System.Windows.Automation.AutomationProperties.SetName(ExplanationButton, label);
        ExplanationSection.Visibility = state.Record is not null || state.IsGenerating || state.ErrorMessage is not null
            ? Visibility.Visible : Visibility.Collapsed;
        ExplanationResult.Visibility = state.Record is null ? Visibility.Collapsed : Visibility.Visible;
        ExplanationMeaning.DocumentIdentity = ExplanationText.DocumentIdentity = documentId;
        ExplanationMeaning.SetLiteralText(state.Record?.Result.Meaning ?? string.Empty);
        ExplanationText.SetLiteralText(state.Record?.Result.Explanation ?? string.Empty);
        ExplanationResult.FlowDirection = state.Record?.Language == LearningLanguage.Arabic
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ExplanationProgress.Visibility = state.IsGenerating ? Visibility.Visible : Visibility.Collapsed;
        ExplanationStatus.Text = state.IsGenerating ? "Explaining…" : state.ErrorMessage ?? string.Empty;
        ExplanationStatus.Visibility = ExplanationStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PresentAt(anchor);
    }

    private void ClearExplanation()
    {
        _displayedExplanation = null;
        ExplanationMeaning.DocumentIdentity = ExplanationText.DocumentIdentity = null;
        ExplanationButton.Visibility = Visibility.Collapsed;
        ExplanationSection.Visibility = Visibility.Collapsed;
        ExplanationMeaning.Text = string.Empty;
        ExplanationText.Text = string.Empty;
        ExplanationStatus.Text = string.Empty;
        ExplanationProgress.Visibility = Visibility.Collapsed;
    }

    internal void BindVocabulary(VocabularyStore store, VocabularyCapture capture, string documentId, Func<bool> canAct)
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || _displayedPreview is not { Status: LearningPreviewStatus.Ready } state || state.RequestId != documentId) return;
        FavoriteButton.Bind(store, capture, () => IsVisible && _displayedPreview?.RequestId == documentId
            && _displayedPreview.Status == LearningPreviewStatus.Ready && canAct());
    }

    private void OnExplanationClick(object sender, RoutedEventArgs e)
    {
        if (_displayedPreview is { } preview && _displayedExplanation is { } state)
            ExplanationRequested?.Invoke(preview.RequestId, state);
    }

    private void PresentAt(ScreenRect anchor)
    {
        if (!IsVisible) Show();
        Visibility = Visibility.Visible;
        UpdateLayout();
        LearningPanelPlacement.Position(this, anchor);
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();

    private void OnCancelClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => DismissRequested?.Invoke();

}
