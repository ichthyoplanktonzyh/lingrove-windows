using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Focus;
using System.Windows.Threading;
using Cotabby.Win32.Focus;

namespace Cotabby.App.Learning;

internal sealed partial class LearningPreviewController
{
    private IWritingSentenceReplacement? _replacement;
    private Task? _replacementWork;
    private long _replacementEpoch;
    private bool _replacementInProgress, _replacementBlocked, _expanded = true;
    private string? _presentedRequestId, _readyReplacementRequestId;
    private FocusedField? _observedField;
    private FocusedField? _recoveryField;
    private readonly HashSet<Task> _automaticWork = [];
    private const string ReplacementUnavailable = "This field cannot safely replace this sentence here. Copy the translation instead.";
    private const string ReplacementUnverified = "Could not verify the replacement. Check the editor; use Copy instead of replacing again.";
    internal Action? BeforeReplacement { get; set; }
    internal bool IsReplacing => Volatile.Read(ref _replacementInProgress);

    /// <summary>AppHost injects the one borrowed replacement owner, which uses the shared insertion gate.</summary>
    internal void AttachReplacement(IWritingSentenceReplacement replacement)
    { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _replacement = replacement; } }

    private void ExpandPreview(string requestId)
    {
        CapturedSentence? capture; long intent;
        lock (_gate)
        {
            if (_disposed || !_intentActive || _activeRequestId != requestId || _activeCapture is null) return;
            capture = _activeCapture; intent = _intentGeneration;
        }
        _ = ExpandAfterValidationAsync(capture, intent, requestId);
    }
    private async Task ExpandAfterValidationAsync(CapturedSentence capture, long intent, string requestId)
    {
        if (!await ValidateLearningCaptureAsync(capture, intent).ConfigureAwait(false)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (_disposed || !_intentActive || _intentGeneration != intent || _activeRequestId != requestId || !ReferenceEquals(capture, _activeCapture)) return;
                _expanded = !_expanded; _window?.ShowExpanded(_expanded);
            }
        });
    }
    private void ReplacePreview(string requestId)
    {
        _dispatcher.VerifyAccess(); CapturedSentence capture; LearningPreviewState state; IWritingSentenceReplacement owner; long epoch;
        lock (_gate)
        {
            if (_disposed || !_intentActive || _replacementInProgress || _replacementBlocked || _activeRequestId != requestId
                || _activeCapture is not { } active || _replacement is not { } replacementOwner
                || _coordinator.State is not { Status: LearningPreviewStatus.Ready, Result: { Length: > 0 } } current
                || current.RequestId != requestId || !MatchesCapture(current, active)
                || !WritingSentenceReplacementPolicy.CanReplace(active.Field, active.Range, active.Source, current.Result)) return;
            capture = active; state = current; owner = replacementOwner; epoch = Interlocked.Read(ref _replacementEpoch);
            // AppHost withdraws any ghost suggestion before the shared insertion owner is used.
            // This application-local callback performs no editor read or native write.
            BeforeReplacement?.Invoke();
            _replacementInProgress = true;
            _replacementWork = ExecuteReplacementAsync(owner, capture, state, epoch);
        }
        UpdateReplacementPresentation(state, capture);
    }
    private void ReplaceFromShortcut(string requestId)
    {
        _dispatcher.VerifyAccess(); CapturedSentence? capture; LearningPreviewState state;
        lock (_gate)
        {
            if (_disposed || !_intentActive || _activeRequestId != requestId || _activeCapture is null) return;
            capture = _activeCapture; state = _coordinator.State;
            if (state.RequestId != requestId || state.Status != LearningPreviewStatus.Ready || state.Result is not { Length: > 0 }) return;
            if (_replacement is null || !WritingSentenceReplacementPolicy.CanReplace(capture.Field, capture.Range, capture.Source, state.Result))
            { _expanded = true; _window?.ShowExpanded(true); UpdateReplacementPresentation(state, capture); return; }
        }
        ReplacePreview(requestId);
    }
    private void EscapePreview(string requestId)
    {
        _dispatcher.VerifyAccess();
        if (!IsCurrentRequest(requestId)) return;
        if (_dictionarySession?.State.Unit is not null) { _dictionarySession.Close(); _dictionaryWindow?.HideCard(); return; }
        CancelAndHide();
    }
    private async Task ExecuteReplacementAsync(IWritingSentenceReplacement owner, CapturedSentence capture, LearningPreviewState state, long epoch)
    {
        bool Permit() => !Volatile.Read(ref _disposed) && Interlocked.Read(ref _replacementEpoch) == epoch;
        WritingReplacementOutcome outcome;
        try { outcome = await owner.ReplaceAsync(capture.Field, capture.Range, capture.Source, state.Result!, Permit, CancellationToken.None).ConfigureAwait(false); }
        catch { outcome = WritingReplacementOutcome.Unverified; }
        FocusedField? recovery = null;
        if (outcome == WritingReplacementOutcome.Unverified && Permit())
        {
            try { recovery = await Task.Run(() => _focus is UiaFocusTracker tracker ? tracker.RefreshGuarded(Permit) : Permit() ? _focus.Refresh() : null).ConfigureAwait(false); }
            catch { }
        }
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                _replacementInProgress = false; _replacementWork = null;
                if (_disposed || !_intentActive || Interlocked.Read(ref _replacementEpoch) != epoch
                    || _activeRequestId != state.RequestId || !ReferenceEquals(_activeCapture, capture)) return;
                if (outcome == WritingReplacementOutcome.Verified) { CancelAndHide(); return; }
                if (outcome == WritingReplacementOutcome.Unverified)
                {
                    if (recovery is null || !Cotabby.Core.Insertion.InsertionTargetPolicy.IsSameField(capture.Field, recovery)
                        || FieldCaptureFailure(recovery, capture.Settings) is not null) { CancelAndHide(); return; }
                    _recoveryField = recovery; _replacementBlocked = true; _expanded = true;
                    Volatile.Write(ref _readyReplacementRequestId, null); _window?.ShowExpanded(true);
                }
                UpdateReplacementPresentation(state, capture);
            }
        });
    }
    private void UpdateReplacementPresentation(LearningPreviewState state, CapturedSentence capture)
    {
        _dispatcher.VerifyAccess();
        bool can = state.Status == LearningPreviewStatus.Ready && state.Result is { Length: > 0 }
            && _replacement is not null && !_replacementBlocked
            && WritingSentenceReplacementPolicy.CanReplace(capture.Field, capture.Range, capture.Source, state.Result);
        _window?.ShowExpanded(_expanded);
        _window?.ShowReplacementState(can, _replacementInProgress, _replacementBlocked ? ReplacementUnverified
            : state.Status == LearningPreviewStatus.Ready && !can ? ReplacementUnavailable : null);
    }
    private bool ReplacementPending(long intent) { lock (_gate) return _replacementInProgress && _intentGeneration == intent; }
    private bool RecoveryPending(long intent) { lock (_gate) return _replacementBlocked && _intentGeneration == intent; }
    private bool RecoveryCaptureCurrent(CapturedSentence capture)
    {
        FocusedField? recovered; long epoch;
        lock (_gate) { if (!_replacementBlocked || !ReferenceEquals(_activeCapture, capture)) return false; recovered = _recoveryField; epoch = Interlocked.Read(ref _replacementEpoch); }
        bool Permit() => !Volatile.Read(ref _disposed) && Interlocked.Read(ref _replacementEpoch) == epoch;
        if (recovered is null || !Permit()) return false;
        try
        {
            var field = _focus is UiaFocusTracker tracker ? tracker.RefreshGuarded(Permit) : Permit() ? _focus.Refresh() : null;
            return Permit() && field is not null && SameCapturedField(recovered, field) && FieldCaptureFailure(field, capture.Settings) is null;
        }
        catch { return false; }
    }
    private bool CanCopyCapture(CapturedSentence capture) => _replacementBlocked && _recoveryField is { } recovered
        ? _settings is { } settings && settings.HasSameTranslationInputs(capture.Settings) && IsLearningAvailable(settings)
            && _focus.Current is { } field && SameCapturedField(recovered, field) && FieldCaptureFailure(field, capture.Settings) is null
        : CachedCaptureCurrent(capture);

    private void ScheduleAutomatic(FocusedField field, LearningRuntimeSettings settings)
    {
        var origin = CaptureOrigin.From(field); if (origin is null) return;
        long intent = BeginIntent(origin); if (intent < 0) return;
        lock (_gate)
        {
            if (_disposed || _intentGeneration != intent) return;
            _expanded = false;
            var task = DelayAutomaticAsync(intent, settings, origin);
            _automaticWork.Add(task);
            _ = task.ContinueWith(t => { _ = t.Exception; lock (_gate) _automaticWork.Remove(t); }, TaskScheduler.Default);
        }
    }
    private async Task DelayAutomaticAsync(long intent, LearningRuntimeSettings settings, CaptureOrigin origin)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
            if (!IsPendingIntent(intent)) return;
            await _dispatcher.InvokeAsync(() =>
            {
                if (IsPendingIntent(intent)) _ = CaptureAndStartAsync(intent, settings, origin, origin.Field.CaretRect, automatic: true);
            }, DispatcherPriority.Background);
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) { }
    }
}
