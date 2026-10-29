using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.Suggestions.Streaming;
using Cotabby.Core.Input;
using System.Diagnostics;
using Cotabby.Core.Support;

namespace Cotabby.Core.Suggestions;

/// <summary>Worker partials are coalesced behind one off-thread focus refresh and one UI post.
/// Final, cancellation and shutdown retire the request before any queued callback may publish.</summary>
public sealed partial class SuggestionCoordinator
{
    private sealed record Partial(string Id, string Text, FocusedField Origin, SuggestionSettings Settings,
        long Sequence, long EditEpoch, int Latency, CancellationToken Token, bool SpacingIsExact);
    private readonly object _streamGate = new();
    private readonly HashSet<Task> _streamTasks = [];
    private string? _streamId, _streamRendered;
    private string _streamClipboard = "";
    private Partial? _streamPending;
    private bool _streamBusy, _streamFinished, _streamClosed;
    private TypingPredictionCandidate? _typing;
    private long _typingSequence, _typingEpoch;
    private CancellationToken _typingToken;
    private string? _typingPollId;
    private bool _streamOfferActive;
    private readonly TypingCadence _cadence = new();

    private void RecordTypingCadence(KeyboardEvent input, FocusedField? field)
    {
        if (field is null || field.FocusChangeSequence <= 0) return;
        lock (_streamGate) _cadence.Record(field.FocusChangeSequence,
            input.Kind == KeyKind.Character ? input.Character.ToString() : "", TypingNow());
    }

    private void BeginStreaming(string id)
    {
        lock (_streamGate) { _streamId = id; _streamRendered = null; _streamPending = null; _streamFinished = false; _typing = null; _streamOfferActive = false; _streamClipboard = ""; }
    }
    private void SetStreamClipboard(string id, string value) { lock (_streamGate) if (_streamId == id) _streamClipboard = value; }
    private string StreamClipboard(string id) { lock (_streamGate) return _streamId == id ? _streamClipboard : ""; }
    private void SetStreamOffer(string id, bool active) { lock (_streamGate) if (_streamId == id) _streamOfferActive = active; }
    private void StartTypingPrediction(string id, FocusedField field, SuggestionSettings settings, long sequence, long epoch, CancellationToken token)
    {
        if (!settings.PredictAheadWhileTyping || settings.UsesEndpoint || token.IsCancellationRequested) return;
        lock (_streamGate)
        {
            if (_streamId != id || _streamClosed || !_pendingEdits.IsCurrent(epoch)) return;
            _typing = new TypingPredictionCandidate(field); _typingSequence = sequence; _typingEpoch = epoch; _typingToken = token;
        }
    }
    private bool TryRetainTyping(string characters, FocusedField? beforeKey, long epoch, long sequence)
    {
        lock (_streamGate)
        {
            if (_streamClosed || _typingToken.IsCancellationRequested || _typing is not { } candidate || _streamId is not { } id ||
                !candidate.Append(characters, beforeKey, TypingNow())) return false;
            _typingSequence = sequence; _typingEpoch = epoch; _streamRendered = null;
            if (_typingPollId != id)
            {
                _typingPollId = id;
                var work = Task.Run(() => PollTypingAsync(id)); _streamTasks.Add(work);
                _ = work.ContinueWith(done => { lock (_streamGate) _streamTasks.Remove(done); }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return true;
        }
    }
    private void InvalidateTyping() { lock (_streamGate) _typing = null; }
    private void RestartTypingIfConsumed(string id)
    {
        lock (_streamGate) if (_streamId == id && _typing is { TypedText.Length: > 0 }) RestartTyping(id);
    }
    private async Task PollTypingAsync(string id)
    {
        try
        {
            while (true)
            {
                FocusedField? live;
                try { live = _focus.Refresh(); } catch { RestartTyping(id); return; }
                var check = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Post(() =>
                {
                    Partial? replay = null; bool restart = false, done = false;
                    lock (_streamGate)
                    {
                        if (_streamClosed || _typingToken.IsCancellationRequested || _streamId != id || _typing is not { } candidate || _streamFinished) { check.TrySetResult(true); return; }
                        if (!candidate.Accepts(live) || candidate.Expiration(live) is { } expiry && TypingNow() >= expiry) restart = true;
                        else
                        {
                            if (candidate.IsPublished(live) && candidate.LatestResult is { } text && _settings.StreamSuggestionsWhileGenerating)
                                replay = new(id, text, candidate.Origin, _settings, _typingSequence, _typingEpoch, 0, _typingToken, _engine.SpacingIsExact);
                            done = candidate.Expiration(live) is null;
                        }
                        if (replay is not null) { _streamPending = replay; StartStreamRefreshLocked(); }
                    }
                    if (restart) RestartTyping(id);
                    check.TrySetResult(restart || done);
                });
                if (await check.Task.ConfigureAwait(false)) return;
                await Task.Delay(20).ConfigureAwait(false);
            }
        }
        finally { lock (_streamGate) if (_typingPollId == id) _typingPollId = null; }
    }
    private void RestartTyping(string id)
    {
        Post(() =>
        {
            lock (_streamGate) { if (_streamClosed || _streamId != id || _typing is null) return; _typing = null; }
            if (_disposed) return;
            CancelSession("typing prediction diverged");
            SchedulePrediction(typed: null);
        });
    }
    private async Task<(string Text, FocusedField Field, long Sequence, long Epoch, bool WasRebased, bool HadOffer)?> ResolveTypingFinalAsync(
        string id, string text, FocusedField original, long sequence, long epoch, CancellationToken token)
    {
        // Key delivery is queued on the UI owner. Wait for that queue before judging a final
        // result so already-arrived hook events cannot be mistaken for an unexpected host edit.
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => barrier.TrySetResult());
        await barrier.Task.WaitAsync(token).ConfigureAwait(false);
        lock (_streamGate)
        {
            if (_streamId != id || _typing is not { } candidate) return (text, original, sequence, epoch, false, false);
            if (!candidate.Receive(text, true)) { RestartTyping(id); return null; }
        }
        while (!token.IsCancellationRequested)
        {
            var live = _focus.Refresh();
            lock (_streamGate)
            {
                if (_streamClosed || _streamId != id || _typing is not { } candidate) return null;
                if (!candidate.Accepts(live) || candidate.Expiration(live) is { } deadline && TypingNow() >= deadline)
                { RestartTyping(id); return null; }
                if (candidate.Rebased(text, live) is { } rebased)
                {
                    if (string.IsNullOrWhiteSpace(rebased)) { RestartTyping(id); return null; }
                    if (_cadence.RemainingDelay(live!.FocusChangeSequence, live.PrecedingText, TypingNow()) <= 0)
                    { bool wasRebased = candidate.TypedText.Length > 0, hadOffer = _streamOfferActive; _typing = null; return (rebased, live, _typingSequence, _typingEpoch, wasRebased, hadOffer); }
                }
            }
            await Task.Delay(20, token).ConfigureAwait(false);
        }
        return null;
    }
    private bool RebasedEligible(string text, FocusedField live, bool hasOffer)
    {
        var settings = _settings;
        if (SuggestionAvailability.DisabledReason(settings, _clock.GetUtcNow(), _batterySaverOn()) is not null ||
            SuggestionAvailability.DisabledReason(settings, live) is not null ||
            TrailingDuplicationFilter.DuplicatesTrailingText(text, live.TrailingText)) return false;
        if (hasOffer) return true;
        if (!settings.SuggestWithinWords && CaretWordContext.UnfinishedWord(live.PrecedingText) is not null ||
            CaretTokenPosition.IsInsideToken(live.PrecedingText, live.TrailingText)) return false;
        return true;
    }
    private void QueueStreamingPartial(string id, string text, FocusedField origin, SuggestionSettings settings,
        long sequence, long editEpoch, int latency, CancellationToken token)
    {
        lock (_streamGate)
        {
            if (_streamClosed || _streamFinished || _streamId != id || token.IsCancellationRequested) return;
            if (_typing is not null)
            {
                if (!_typing.Receive(text, false)) { RestartTyping(id); return; }
                sequence = _typingSequence; editEpoch = _typingEpoch;
            }
            if (!settings.StreamSuggestionsWhileGenerating) return;
            _streamPending = new(id, text, origin, settings, sequence, editEpoch, latency, token, _engine.SpacingIsExact);
            StartStreamRefreshLocked();
        }
    }
    private void StartStreamRefreshLocked()
    {
        if (_streamBusy || _streamPending is null || _streamClosed) return;
        _streamBusy = true; var value = _streamPending; _streamPending = null;
        var work = Task.Run(() => RefreshPartialAsync(value)); _streamTasks.Add(work);
        _ = work.ContinueWith(done => { lock (_streamGate) _streamTasks.Remove(done); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private async Task RefreshPartialAsync(Partial partial)
    {
        FocusedField? live = null; CompletionFinalizer.Prepared? prepared = null;
        try
        {
            while (!partial.Token.IsCancellationRequested)
            {
                live = _focus.Refresh();
                string? text = null;
                double delay = 0;
                bool wasRebased = false, hadOffer = false;
                lock (_streamGate)
                {
                    if (_streamClosed || _streamFinished || _streamId != partial.Id) break;
                    if (_streamId == partial.Id && _typing is { } candidate)
                    {
                        text = candidate.Rebased(partial.Text, live);
                        wasRebased = candidate.TypedText.Length > 0; hadOffer = _streamOfferActive;
                        partial = partial with { Sequence = _typingSequence, EditEpoch = _typingEpoch };
                    }
                    else if (InsertionTargetPolicy.IsCurrentTarget(partial.Origin, live)) text = partial.Text;
                    if (live is not null) delay = _cadence.RemainingDelay(live.FocusChangeSequence, live.PrecedingText, TypingNow());
                }
                if (delay > 0) { await Task.Delay(TimeSpan.FromSeconds(delay), partial.Token).ConfigureAwait(false); continue; }
                if (text is not null && live is not null && wasRebased &&
                    (!RebasedEligible(text, live, hadOffer) || !hadOffer && _typos is not null &&
                        _typos.Advise(live.PrecedingText, partial.Settings.SpellingDictionaries, partial.Settings.Typos).Decision.Action != Cotabby.Core.Spelling.TypoGateAction.Proceed)) text = null;
                if (text is not null && live is not null)
                    prepared = CompletionFinalizer.Prepare(text, live, partial.Settings, partial.SpacingIsExact, _typos, isFinal: false);
                break;
            }
        }
        catch { /* Provider and UIA failures cannot publish a partial. */ }
        try { Post(() => CompletePartial(partial, live, prepared)); }
        catch { lock (_streamGate) { _streamBusy = false; StartStreamRefreshLocked(); } }
    }
    private void CompletePartial(Partial partial, FocusedField? live, CompletionFinalizer.Prepared? prepared)
    {
        try
        {
            bool current;
            lock (_streamGate) current = !_streamClosed && !_streamFinished && _streamId == partial.Id;
            if (!current || _disposed || partial.Token.IsCancellationRequested || prepared is null || live is null ||
                !_pendingEdits.IsCurrent(partial.EditEpoch) || _keySequence != partial.Sequence ||
                !InsertionTargetPolicy.IsCurrentTarget(live, _focus.Current)) return;
            string visible = prepared.Value.Visible, hidden = prepared.Value.Hidden;
            bool growsBuffer = _session is { } old && old.RequestId == partial.Id && old.VisibleText == visible &&
                (visible + hidden).Length > (old.VisibleText + old.HiddenText).Length &&
                (visible + hidden).StartsWith(old.VisibleText + old.HiddenText, StringComparison.Ordinal);
            lock (_streamGate) if (!growsBuffer && !StreamedGhostTextPolicy.IsRenderableExtension(visible, _streamRendered)) return;
            if (_session is null)
                Present(partial.Id, visible, live, partial.Latency, partial.Sequence, partial.EditEpoch, partial.Token,
                    hidden: hidden, showFollowingWords: partial.Settings.ShowFollowingWords);
            else if (_session.RequestId == partial.Id && _session.Correction is null && _sessionField is not null &&
                InsertionTargetPolicy.IsSameField(_sessionField, live) && live.PrecedingText == _expectedPreceding &&
                live.TrailingText == _sessionField.TrailingText)
            {
                _sessionField = live; _session = _session with { VisibleText = visible, HiddenText = hidden }; if (!growsBuffer) _overlay.Update(visible);
                PrepareVisibleContinuation(_session, live, null);
            }
            else return;
            if (_session?.RequestId == partial.Id) lock (_streamGate) _streamRendered = visible;
        }
        finally { lock (_streamGate) { _streamBusy = false; StartStreamRefreshLocked(); } }
    }
    private void FinishStreaming(string id)
    {
        lock (_streamGate) if (_streamId == id) { _streamFinished = true; _streamPending = null; }
    }
    private void PresentStreamFinal(string id, CompletionFinalizer.Prepared prepared, FocusedField live, bool showFollowingWords, int latency, long sequence, long epoch, CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested || !_pendingEdits.IsCurrent(epoch) || _keySequence != sequence ||
            !InsertionTargetPolicy.IsCurrentTarget(live, _focus.Current)) return;
        if (_session is { } existing && existing.RequestId == id && existing.Correction is null)
        { _session = existing with { VisibleText = prepared.Visible, HiddenText = prepared.Hidden, ShowFollowingWords = showFollowingWords }; _overlay.Update(prepared.Visible); PrepareVisibleContinuation(_session, live, null); return; }
        Present(id, prepared.Visible, live, latency, sequence, epoch, token, hidden: prepared.Hidden, showFollowingWords: showFollowingWords);
    }
    private void ClearSuppressedStream(string id)
    {
        if (!_disposed && _session?.RequestId == id) ClearSession();
    }
    private void CloseStreaming() { lock (_streamGate) { _streamClosed = true; _streamPending = null; } }
    private async Task DrainStreamingAsync()
    {
        Task[] work; lock (_streamGate) work = _streamTasks.ToArray();
        try { await Task.WhenAll(work).ConfigureAwait(false); } catch { }
    }
    private async Task<FocusedField?> WaitForPresentationCadenceAsync(FocusedField? live, CancellationToken token)
    {
        while (live is not null && !token.IsCancellationRequested)
        {
            double delay;
            lock (_streamGate) delay = _cadence.RemainingDelay(live.FocusChangeSequence, live.PrecedingText, TypingNow());
            if (delay <= 0) break;
            await Task.Delay(TimeSpan.FromSeconds(delay), token).ConfigureAwait(false);
            live = _focus.Refresh();
        }
        return live;
    }
    private static double TypingNow() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
