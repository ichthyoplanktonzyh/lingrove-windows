using System.Windows.Threading;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Microsoft.Extensions.Logging;

namespace Cotabby.App.Learning;

/// <summary>
/// Owns the sentence card's dictionary child and bridges exact document/occurrence identity to
/// the bundled session. No source is recaptured for the query: UIA is used only to revalidate the
/// already captured editor before a card opens or a late definition becomes visible.
/// </summary>
internal sealed partial class LearningPreviewController
{
    private readonly LearningDictionarySession? _dictionarySession;
    private LearningDictionaryWindow? _dictionaryWindow;
    private ScreenRect _dictionaryAnchor;
    private long _dictionaryIntent;
    private long _dictionaryOpeningRevision;
    private LearningPreviewState? _dictionaryTranslation;

    private void OnDictionaryLookupRequested(LearningUnit unit, string documentId, ScreenRect anchor)
    {
        _dispatcher.VerifyAccess();
        if (_dictionarySession is null) return;
        CapturedSentence? capture; long intent; long openingRevision; LearningPreviewState translation;
        lock (_gate)
        {
            if (!CanQueryDictionaryLocked(documentId, unit)) return;
            capture = _activeCapture; intent = _intentGeneration;
            openingRevision = ++_dictionaryOpeningRevision;
            translation = _coordinator.State;
            _dictionaryTranslation = null;
            _dictionarySession.Close();
        }
        _dictionaryWindow?.HideCard();
        _ = ObserveDictionaryPresentationAsync(OpenDictionaryAfterValidationAsync(unit, documentId, anchor,
            capture!, intent, openingRevision, translation));
    }

    private async Task OpenDictionaryAfterValidationAsync(LearningUnit unit, string documentId, ScreenRect anchor,
        CapturedSentence capture, long intent, long openingRevision, LearningPreviewState translation)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        var prepared = await Task.Run<(LearningExplanationInput? Input, VocabularyCapture? Capture)>(() =>
        {
            try
            {
                var detached = _vocabularyCaptures?.FromUnit(unit, translation);
                return (detached is not null ? LearningExplanationPolicy.Input(detached) : _explanationInputs?.FromUnit(unit, translation), detached);
            }
            catch { return (null, null); } // Mac permits target-only meaning when an example is unavailable.
        }).ConfigureAwait(false);
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (_intentGeneration != intent || _dictionaryOpeningRevision != openingRevision
                    || !CanQueryDictionaryLocked(documentId, unit)
                    || !LearningExplanationCardPolicy.SameDocument(translation, _coordinator.State)
                    || !CachedCaptureCurrent(capture)) return;
                _dictionaryAnchor = anchor; _dictionaryIntent = intent;
                _dictionaryTranslation = translation;
                _dictionaryVocabularyCapture = prepared.Capture;
                _ = _dictionarySession!.OpenAsync(unit, documentId, translation.Options!.TargetLanguage, translation.Result, prepared.Input);
            }
        }, DispatcherPriority.Background);
    }

    // Caller holds the intent gate; double-clicks from an old TextBlock must not navigate a new
    // document even when it happens to contain an equal spelling at the same occurrence offset.
    private bool CanQueryDictionaryLocked(string documentId, LearningUnit? unit = null)
    {
        var state = _coordinator.State;
        return !_disposed && _intentActive && _activeCapture is not null && _activeRequestId == documentId
            && state.RequestId == documentId && state.Status == LearningPreviewStatus.Ready
            && state.UnitStage == LearningUnitStage.Ready && (unit is null || state.Units.Contains(unit));
    }

    private void OnDictionaryStateChanged(LearningDictionaryState state)
    {
        lock (_gate) if (_disposed) return;
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_dictionarySession?.State, state)) return;
            if (state.Unit is null) { _dictionaryWindow?.HideCard(); return; }
            CapturedSentence? capture; long intent;
            lock (_gate)
            {
                if (state.DocumentId is null || !CanQueryDictionaryLocked(state.DocumentId, state.Unit)
                    || _dictionaryIntent != _intentGeneration) return;
                capture = _activeCapture; intent = _intentGeneration;
            }
            _ = ObserveDictionaryPresentationAsync(RenderDictionaryAfterValidationAsync(state, capture!, intent));
        }, DispatcherPriority.Background);
    }

    private async Task RenderDictionaryAfterValidationAsync(LearningDictionaryState state, CapturedSentence capture, long intent)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_dictionarySession?.State, state) || !CanUseDictionaryTargetLocked(state, capture, intent)) return;
                EnsureDictionaryWindow().ShowState(state, _dictionaryAnchor,
                    LearningDictionaryCardPolicy.ExampleTranslation(state.Explanation?.Input, _dictionaryTranslation), _explanationSession?.Profile);
                BindDictionaryVocabulary(state, capture, intent);
            }
        }, DispatcherPriority.Background);
    }

    private LearningDictionaryWindow EnsureDictionaryWindow()
    {
        if (_dictionaryWindow is not null) return _dictionaryWindow;
        _dictionaryWindow = new LearningDictionaryWindow();
        _dictionaryWindow.WordRequested += (word, target) => NavigateDictionary(target, word);
        _dictionaryWindow.BackRequested += target => NavigateDictionary(target);
        _dictionaryWindow.CloseRequested += target => NavigateDictionary(target, close: true);
        _dictionaryWindow.PageRequested += (page, target) => NavigateDictionary(target, page: page);
        _dictionaryWindow.ExplanationRequested += OnDictionaryExplanationRequested;
        _dictionaryWindow.SelectedCopyRequested += OnDictionarySelectedCopy;
        _dictionaryWindow.Closed += (_, _) =>
        {
            _dictionaryWindow = null;
            lock (_gate) RetireDictionaryLocked();
        };
        return _dictionaryWindow;
    }

    private void NavigateDictionary(Guid target, LearningDictionaryWord? word = null, bool close = false,
        LearningDictionaryPage? page = null)
    {
        CapturedSentence capture; LearningDictionaryState state; long intent; long navigation;
        lock (_gate)
        {
            if (_disposed || _dictionarySession?.State is not { Unit: not null } current || current.TargetIdentity != target) return;
            navigation = ++_dictionaryOpeningRevision;
            if (close)
            {
                _dictionaryTranslation = null; _dictionarySession.Close(target); _dictionaryWindow?.HideCard(); return;
            }
            if (_activeCapture is null || !CanUseDictionaryTargetLocked(current, _activeCapture, _intentGeneration)) return;
            capture = _activeCapture; state = current; intent = _intentGeneration;
        }
        _ = ObserveDictionaryPresentationAsync(NavigateDictionaryAfterValidationAsync(state, capture, intent, navigation, word, page));
    }

    private async Task NavigateDictionaryAfterValidationAsync(LearningDictionaryState state, CapturedSentence capture,
        long intent, long navigation, LearningDictionaryWord? word, LearningDictionaryPage? page)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (_dictionaryOpeningRevision != navigation || !CanUseDictionaryTargetLocked(state, capture, intent)) return;
                if (page is not null) _ = _dictionarySession!.SelectPageAsync(page.Value, state.TargetIdentity);
                else if (word is not null) _ = _dictionarySession!.SelectWordAsync(word, state.TargetIdentity);
                else _ = _dictionarySession!.ReturnToExpressionAsync(state.TargetIdentity);
            }
        }, DispatcherPriority.Background);
    }

    private bool CanUseDictionaryTargetLocked(LearningDictionaryState state, CapturedSentence capture, long intent) =>
        !_disposed && _intentGeneration == intent && _dictionaryIntent == intent && ReferenceEquals(_activeCapture, capture)
        && state.DocumentId is not null && state.Unit is not null
        && _dictionarySession?.State.TargetIdentity == state.TargetIdentity
        && CanQueryDictionaryLocked(state.DocumentId, state.Unit)
        && LearningExplanationCardPolicy.SameDocument(_dictionaryTranslation, _coordinator.State)
        && CachedCaptureCurrent(capture);

    private void OnDictionaryExplanationRequested(Guid target, LearningExplanationState displayed)
    {
        CapturedSentence capture; LearningDictionaryState state; long intent; long navigation;
        lock (_gate)
        {
            if (_dictionarySession?.State is not { Unit: not null } current || current.TargetIdentity != target
                || !ReferenceEquals(current.Explanation, displayed) || _activeCapture is null
                || !CanUseDictionaryTargetLocked(current, _activeCapture, _intentGeneration)) return;
            navigation = ++_dictionaryOpeningRevision;
            if (displayed.IsGenerating) { _dictionarySession.CancelExplanation(target, displayed); return; }
            capture = _activeCapture; state = current; intent = _intentGeneration;
        }
        _ = ObserveDictionaryPresentationAsync(ExplainDictionaryAfterValidationAsync(state, displayed, capture, intent, navigation));
    }

    private async Task ExplainDictionaryAfterValidationAsync(LearningDictionaryState state, LearningExplanationState displayed,
        CapturedSentence capture, long intent, long navigation)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            bool configure = false;
            lock (_gate)
            {
                if (navigation != _dictionaryOpeningRevision || !CanUseDictionaryTargetLocked(state, capture, intent)
                    || !ReferenceEquals(_dictionarySession!.State.Explanation, displayed)) return;
                var action = LearningExplanationCardPolicy.Action(displayed, _dictionarySession.State.ExplanationConfigured);
                if (action is LearningExplanationCardAction.Explain or LearningExplanationCardAction.Refresh)
                    _ = _dictionarySession.ExplainAsync(state.TargetIdentity, displayed, refresh: action == LearningExplanationCardAction.Refresh);
                else configure = action == LearningExplanationCardAction.Configure;
            }
            if (configure) { CancelAndHide(); _configureExplanation?.Invoke(); }
        }, DispatcherPriority.Background);
    }

    // The App intent lock covers pending UIA validation as well as an already-open session.
    // Otherwise a slow first double-click could reopen after a newer click or Close.
    private void RetireDictionaryLocked()
    {
        _dictionaryVocabularyCapture = null;
        ++_dictionaryOpeningRevision;
        _dictionaryTranslation = null;
        _dictionarySession?.Close();
    }

    private async Task ObserveDictionaryPresentationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Dispatcher shutdown or an unavailable native window cannot become an unobserved
            // task fault. Do not log exception strings, which could include text or file paths.
            _logger.LogDebug("Dictionary presentation ended after its captured target retired.");
        }
    }
}
