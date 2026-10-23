using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Learning;

/// <summary>
/// Lends the app's explicit clipboard writer to the current readonly sentence card. The button
/// supplies a frozen document receipt; the controller checks its still-active capture and current
/// coordinator projection before copying. No new UIA text is read and no editor edit is authorized.
/// </summary>
internal sealed partial class LearningPreviewController
{
    private readonly LearningTextCopyController? _textCopy;
    private long _copyRevision;

    private void OnCopyRequested(LearningPreviewCopyRequest request)
    {
        _dispatcher.VerifyAccess();
        CapturedSentence capture;
        long intent, revision;
        string text;
        lock (_gate)
        {
            if (_disposed || !_intentActive || _activeCapture is null || _activeRequestId != request.Document.RequestId) return;
            capture = _activeCapture;
            if (!MatchesCapture(request.Document, capture) || !CachedCaptureCurrent(capture)
                || LearningPreviewCopyPolicy.Text(request, _coordinator.State) is not { } payload) return;
            text = payload;
            intent = _intentGeneration;
            revision = ++_copyRevision;
        }
        _ = CopyAsync(request, capture, intent, revision, text);
    }

    private bool TryAdmitCopy(LearningPreviewCopyRequest request, CapturedSentence capture, long intent, long revision)
    {
        lock (_gate)
            return !_disposed && _intentActive && _intentGeneration == intent && _copyRevision == revision
                && ReferenceEquals(_activeCapture, capture) && _activeRequestId == request.Document.RequestId
                && capture.Lifetime is { Token.IsCancellationRequested: false }
                && CachedCaptureCurrent(capture) && LearningPreviewCopyPolicy.Text(request, _coordinator.State) is not null;
    }

    private async Task CopyAsync(LearningPreviewCopyRequest request, CapturedSentence capture, long intent, long revision, string text)
    {
        bool copied = _textCopy is not null && await _textCopy.CopyAsync(text,
            () => TryAdmitCopy(request, capture, intent, revision)).ConfigureAwait(false);
        if (_dispatcher.HasShutdownStarted) return;
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                // A newer click, retirement or reused window cannot receive the old attempt's feedback.
                if (TryAdmitCopy(request, capture, intent, revision)) _window?.ShowCopyOutcome(request, copied);
            });
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted) { }
    }
}
