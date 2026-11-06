using Cotabby.App.UI.Controls;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.Learning;

/// <summary>
/// Bridges readonly explanation selections to the same single-worker clipboard owner as the
/// sentence Copy footer. Worker grants read only detached receipts and cached Core snapshots;
/// no TextPointer, WPF control or live editor text crosses the dispatcher boundary.
/// </summary>
internal sealed partial class LearningPreviewController
{
    private void OnPreviewSelectedCopy(SelectedTextCopyLease lease, LearningPreviewState document) =>
        BeginSelectedCopy(new(lease, document));

    private void OnDictionarySelectedCopy(SelectedTextCopyLease lease, LearningDictionaryState shown)
    {
        _dispatcher.VerifyAccess();
        LearningSelectedCopyRequest request;
        lock (_gate)
        {
            if (_dictionaryTranslation is not { } document || _dictionaryIntent != _intentGeneration
                || shown.TargetIdentity != _dictionarySession?.State.TargetIdentity) return;
            request = new(lease, document, shown.TargetIdentity);
        }
        BeginSelectedCopy(request);
    }

    private void BeginSelectedCopy(LearningSelectedCopyRequest request)
    {
        _dispatcher.VerifyAccess();
        CapturedSentence capture; long intent, revision, navigation;
        lock (_gate)
        {
            if (_disposed || !_intentActive || _activeCapture is null || _activeRequestId != request.Document.RequestId
                || !LearningSelectedCopyPolicy.IsCurrent(request, _coordinator.State, _dictionarySession?.State)) return;
            capture = _activeCapture;
            if (!MatchesCapture(request.Document, capture) || !CanCopyCapture(capture)) return;
            intent = _intentGeneration; revision = ++_copyRevision;
            navigation = _dictionaryOpeningRevision;
        }
        _ = CopySelectedAsync(request, capture, intent, revision, navigation);
    }

    private bool TryAdmitSelectedCopy(LearningSelectedCopyRequest request, CapturedSentence capture, long intent, long revision, long navigation)
    {
        lock (_gate)
            return !_disposed && _intentActive && _intentGeneration == intent && _copyRevision == revision
                && ReferenceEquals(_activeCapture, capture) && _activeRequestId == request.Document.RequestId
                && CanCopyCapture(capture)
                // Clicking Back/word/page immediately advances navigation, before UIA validation
                // commits the new Core target. A pending old copy must not cross that intent gap.
                && (request.DictionaryTarget is null || _dictionaryIntent == intent && _dictionaryOpeningRevision == navigation)
                && LearningSelectedCopyPolicy.IsCurrent(request, _coordinator.State, _dictionarySession?.State);
    }

    private async Task CopySelectedAsync(LearningSelectedCopyRequest request, CapturedSentence capture, long intent, long revision, long navigation)
    {
        bool copied = _textCopy is not null && await _textCopy.CopyAsync(request.Lease.Text,
            () => TryAdmitSelectedCopy(request, capture, intent, revision, navigation)).ConfigureAwait(false);
        if (_dispatcher.HasShutdownStarted) return;
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (!TryAdmitSelectedCopy(request, capture, intent, revision, navigation)) return;
                if (request.DictionaryTarget is null) _window?.ShowSelectedCopyOutcome(request.Lease, copied);
                else _dictionaryWindow?.ShowSelectedCopyOutcome(request.Lease, copied);
            });
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted) { }
    }
}
