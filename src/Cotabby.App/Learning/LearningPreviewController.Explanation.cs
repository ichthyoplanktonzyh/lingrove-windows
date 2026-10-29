using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Microsoft.Extensions.Logging;

namespace Cotabby.App.Learning;

/// <summary>
/// Adapts LearningSentencePanel's explanation ownership. This partial owns one lightweight
/// session beside the readonly writing card; AppHost lends the independent generation service.
/// ICU builds the target from the completed translation on a worker. UIA only revalidates the
/// captured editor, never supplies new prompt material. Intent and preparation revisions protect
/// the dispatcher/worker gaps, while the Core session rejects retired model replies.
/// </summary>
internal sealed partial class LearningPreviewController
{
    private readonly LearningExplanationSession? _explanationSession;
    private readonly LearningExplanationInputFactory? _explanationInputs;
    private readonly Action? _configureExplanation;
    private LearningPreviewState? _explanationPresentation;
    private long _explanationPreparationRevision;

    // Called only after the complete translation has passed the existing UIA/display checks.
    // Opening a card sets a target/restores saved content; it never calls ExplainAsync.
    private void PrepareSentenceExplanation(LearningPreviewState presentation, CapturedSentence capture, long intent)
    {
        long revision;
        lock (_gate)
        {
            if (_explanationSession is null || _explanationInputs is null
                || !CanExplainDocumentLocked(presentation, capture, intent)) return;
            if (LearningExplanationCardPolicy.SameDocument(_explanationPresentation, presentation)) return;
            RetireExplanationLocked();
            _explanationPresentation = presentation;
            revision = _explanationPreparationRevision;
        }
        _ = ObserveExplanationPresentationAsync(PrepareSentenceExplanationAsync(presentation, capture, intent, revision));
    }

    private async Task PrepareSentenceExplanationAsync(LearningPreviewState presentation, CapturedSentence capture,
        long intent, long revision)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        var input = await Task.Run(() =>
        {
            try { return _explanationInputs!.FromWritingSentencePreview(presentation); }
            catch { return null; } // Unavailable/invalid platform tokenization never becomes a prompt.
        }).ConfigureAwait(false);
        // ICU is bounded but can still outlive an editor update before the tracker publishes it.
        // Re-read after construction rather than granting authority from the pre-tokenization read.
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (revision != _explanationPreparationRevision
                    || !CanExplainDocumentLocked(presentation, capture, intent)) return;
                _explanationSession!.SetTarget(input);
            }
        }, DispatcherPriority.Background);
    }

    private bool CanExplainDocumentLocked(LearningPreviewState presentation, CapturedSentence capture, long intent) =>
        !_disposed && _intentActive && _intentGeneration == intent && ReferenceEquals(_activeCapture, capture)
        && _activeRequestId == presentation.RequestId
        && LearningExplanationCardPolicy.SameDocument(presentation, _coordinator.State)
        && CachedCaptureCurrent(capture);

    private void OnExplanationRequested(string documentId, LearningExplanationState displayedState)
    {
        _dispatcher.VerifyAccess();
        CapturedSentence capture; LearningPreviewState presentation; LearningExplanationState state; long intent;
        lock (_gate)
        {
            if (_explanationSession is null || _activeCapture is null || _explanationPresentation is null) return;
            capture = _activeCapture; presentation = _explanationPresentation; intent = _intentGeneration;
            state = _explanationSession.State;
            if (documentId != presentation.RequestId || !ReferenceEquals(state, displayedState)
                || !CanExplainDocumentLocked(presentation, capture, intent)) return;
            // Like the Mac button, Stop does not prepare/recapture/reconfigure anything.
            if (state.IsGenerating) { _explanationSession.Cancel(state); return; }
        }
        _ = ObserveExplanationPresentationAsync(ExplainAfterValidationAsync(presentation, capture, intent, state));
    }

    private async Task ExplainAfterValidationAsync(LearningPreviewState presentation, CapturedSentence capture,
        long intent, LearningExplanationState state)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            bool configure = false;
            lock (_gate)
            {
                if (_explanationSession is null || !ReferenceEquals(_explanationSession.State, state)
                    || !LearningExplanationCardPolicy.SameDocument(_explanationPresentation, presentation)
                    || !CanExplainDocumentLocked(presentation, capture, intent)) return;
                var action = LearningExplanationCardPolicy.Action(state, _explanationSession.IsConfigured);
                if (action is LearningExplanationCardAction.Explain or LearningExplanationCardAction.Refresh)
                    _ = _explanationSession.ExplainAsync(refresh: action == LearningExplanationCardAction.Refresh, expectedState: state);
                else configure = action == LearningExplanationCardAction.Configure;
            }
            if (configure)
            {
                // Settings activation changes focus. Retire the captured card before handing off.
                CancelAndHide();
                _configureExplanation?.Invoke();
            }
        }, DispatcherPriority.Background);
    }

    private void OnExplanationStateChanged(LearningExplanationState state)
    {
        CapturedSentence capture; LearningPreviewState presentation; long intent;
        lock (_gate)
        {
            if (_disposed || _explanationPresentation is null || _activeCapture is null) return;
            capture = _activeCapture; presentation = _explanationPresentation; intent = _intentGeneration;
        }
        _ = ObserveExplanationPresentationAsync(RenderExplanationAfterValidationAsync(state, presentation, capture, intent));
    }

    private async Task RenderExplanationAfterValidationAsync(LearningExplanationState state,
        LearningPreviewState presentation, CapturedSentence capture, long intent)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (_explanationSession is null || !ReferenceEquals(_explanationSession.State, state)
                    || !LearningExplanationCardPolicy.SameDocument(_explanationPresentation, presentation)
                    || !CanExplainDocumentLocked(presentation, capture, intent)) return;
                _window?.ShowExplanationState(presentation.RequestId, state, _explanationSession.IsConfigured, capture.Field.CaretRect, _explanationSession.Profile);
            }
        }, DispatcherPriority.Background);
    }

    // Caller holds the intent gate, so a queued ICU/UIA result cannot reopen a hidden target.
    private void RetireExplanationLocked()
    {
        RetireVocabularyLocked();
        ++_explanationPreparationRevision;
        _explanationPresentation = null;
        _explanationSession?.Clear();
    }

    private async Task ObserveExplanationPresentationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _logger.LogDebug("Explanation presentation ended after its captured target retired.");
        }
    }
}
