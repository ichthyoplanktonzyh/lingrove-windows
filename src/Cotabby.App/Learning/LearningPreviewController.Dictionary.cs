using System.Windows.Threading;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
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

    private void OnDictionaryLookupRequested(LearningUnit unit, string documentId, ScreenRect anchor)
    {
        _dispatcher.VerifyAccess();
        if (_dictionarySession is null) return;
        CapturedSentence? capture; long intent; long openingRevision;
        lock (_gate)
        {
            if (!CanQueryDictionaryLocked(documentId, unit)) return;
            capture = _activeCapture; intent = _intentGeneration;
            openingRevision = ++_dictionaryOpeningRevision;
            _dictionarySession.Close();
        }
        _ = ObserveDictionaryPresentationAsync(OpenDictionaryAfterValidationAsync(unit, documentId, anchor, capture!, intent, openingRevision));
    }

    private async Task OpenDictionaryAfterValidationAsync(LearningUnit unit, string documentId, ScreenRect anchor,
        CapturedSentence capture, long intent, long openingRevision)
    {
        if (!await Task.Run(() => IsCaptureCurrent(capture)).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (_intentGeneration != intent || _dictionaryOpeningRevision != openingRevision
                    || !CanQueryDictionaryLocked(documentId, unit)
                    || !CachedDictionaryCaptureCurrent(capture)) return;
                _dictionaryAnchor = anchor; _dictionaryIntent = intent;
                var state = _coordinator.State;
                _ = _dictionarySession!.OpenAsync(unit, documentId, state.Options!.TargetLanguage, state.Result);
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

    private bool CachedDictionaryCaptureCurrent(CapturedSentence capture) =>
        _settings is { } settings && settings.HasSameTranslationInputs(capture.Settings) && IsLearningAvailable(settings)
        && _focus.Current is { } field && SameCapturedField(capture.Field, field)
        && FieldCaptureFailure(field, capture.Settings) is null;

    private void OnDictionaryStateChanged(LearningDictionaryState state)
    {
        lock (_gate) if (_disposed) return;
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (_dictionarySession?.State != state) return;
            if (state.Unit is null) { _dictionaryWindow?.HideCard(); return; }
            CapturedSentence? capture; long intent;
            lock (_gate)
            {
                if (state.DocumentId is null || !CanQueryDictionaryLocked(state.DocumentId)
                    || _dictionaryIntent != _intentGeneration) return;
                capture = _activeCapture; intent = _intentGeneration;
            }
            _ = ObserveDictionaryPresentationAsync(RenderDictionaryAfterValidationAsync(state, capture!, intent));
        }, DispatcherPriority.Background);
    }

    private async Task RenderDictionaryAfterValidationAsync(LearningDictionaryState state, CapturedSentence capture, long intent)
    {
        if (!await Task.Run(() => IsCaptureCurrent(capture)).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (_dictionarySession?.State != state || state.DocumentId is null || _intentGeneration != intent
                    || !CanQueryDictionaryLocked(state.DocumentId) || !CachedDictionaryCaptureCurrent(capture)) return;
                EnsureDictionaryWindow().ShowState(state, _dictionaryAnchor);
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
        _dictionaryWindow.Closed += (_, _) =>
        {
            _dictionaryWindow = null;
            lock (_gate) RetireDictionaryLocked();
        };
        return _dictionaryWindow;
    }

    private void NavigateDictionary(Guid target, LearningDictionaryWord? word = null, bool close = false)
    {
        lock (_gate)
        {
            if (_disposed || _dictionarySession?.State is not { Unit: not null } state || state.TargetIdentity != target) return;
            ++_dictionaryOpeningRevision;
            if (close) _dictionarySession.Close(target);
            else if (state.DocumentId is not null && CanQueryDictionaryLocked(state.DocumentId)
                && _activeCapture is { } capture && CachedDictionaryCaptureCurrent(capture))
            {
                if (word is not null) _ = _dictionarySession.SelectWordAsync(word, target);
                else _ = _dictionarySession.ReturnToExpressionAsync(target);
            }
        }
    }

    // The App intent lock covers pending UIA validation as well as an already-open session.
    // Otherwise a slow first double-click could reopen after a newer click or Close.
    private void RetireDictionaryLocked()
    {
        ++_dictionaryOpeningRevision;
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
