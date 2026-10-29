using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.Suggestions.Streaming;
using Cotabby.Core.Support;

namespace Cotabby.Core.Suggestions;

/// <summary>After an acknowledged insertion, local-only work may predict against the exact
/// expected field before UIA publishes it. Output remains hidden until that snapshot is proven.</summary>
public sealed partial class SuggestionCoordinator
{
    private sealed record PreparedContinuation(string SessionId, SuggestionContinuationPlan Plan, SuggestionRequest Request,
        SuggestionSettings Settings, Task<string?> Result);
    private readonly SuggestionWorkController _continuationWork = new(TimeSpan.Zero);
    private PreparedContinuation? _preparedContinuation;

    private void PrepareVisibleContinuation(SuggestionSession session, FocusedField field, CorrectionEdit? correction)
    {
        var settings = _settings;
        if (settings.UsesEndpoint || _engine is not ILocalSpeculativeSuggestionEngine local || !local.CanSpeculateLocally) { CancelPreparedContinuation(); return; }
        SuggestionContinuationPlan? plan;
        if (correction is not null)
            plan = SuggestionContinuationPlan.Correcting(correction.Plan.DeletingUtf16Count, correction.Plan.ReplacementText, field);
        else
        {
            string full = session.VisibleText + session.HiddenText;
            if (CaretWordContext.UnfinishedWord(field.PrecedingText) is null || full.Length == 0 || full.Any(char.IsWhiteSpace)) { CancelPreparedContinuation(); return; }
            plan = SuggestionContinuationPlan.Completing(full, field);
        }
        if (plan is null) { CancelPreparedContinuation(); return; }
        if (_preparedContinuation is { } previous && previous.SessionId == session.RequestId && previous.Plan.Target == plan.Target) return;
        CancelPreparedContinuation();
        string id = "req_" + Guid.NewGuid().ToString("N")[..8];
        var request = SuggestionRequestFactory.Build(plan.Request, id, settings) with { ClipboardContext = session.ClipboardContext };
        long preparationEpoch = _pendingEdits.CaptureEpoch();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = _continuationWork.Submit(async token =>
        {
            try
            {
                var current = _focus.Current;
                if (!_pendingEdits.IsCurrent(preparationEpoch) || !(plan.MatchesSource(current) || plan.MatchesTarget(current) || plan.MatchesTargetWithJoiningSeparator(current)))
                { completion.TrySetResult(null); return; }
                completion.TrySetResult(await local.GenerateLocalSpeculationAsync(request, token).ConfigureAwait(false));
            }
            catch { completion.TrySetResult(null); }
        });
        _ = work.ContinueWith(_ => completion.TrySetResult(null), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _preparedContinuation = new(session.RequestId, plan, request, settings, completion.Task);
    }
    private PreparedContinuation? TakePreparedContinuation(string sessionId)
    {
        if (_preparedContinuation is not { } value || value.SessionId != sessionId) return null;
        _preparedContinuation = null; return value;
    }
    private void CancelPreparedContinuation() { _preparedContinuation = null; _continuationWork.Cancel(); }
    private bool StartPreparedContinuation(PreparedContinuation? prepared, FocusedField waitingSource, long epoch)
    {
        if (prepared is null || _settings.UsesEndpoint || !_pendingEdits.IsCurrent(epoch)) return false;
        long sequence = _keySequence;
        BeginStreaming(prepared.Request.RequestId); SetStreamClipboard(prepared.Request.RequestId, prepared.Request.ClipboardContext);
        _ = _work.Submit(token => AcceptedSpeculationAsync(
            async ct => await prepared.Result.WaitAsync(ct).ConfigureAwait(false) ?? "", prepared.Plan, prepared.Request,
            prepared.Settings, waitingSource, sequence, epoch, token));
        return true;
    }
    private bool StartAcceptedSpeculation(FocusedField source, string inserted, string clipboard, long epoch)
    {
        var settings = _settings;
        if (settings.UsesEndpoint || _engine is not ILocalSpeculativeSuggestionEngine local || !local.CanSpeculateLocally ||
            SuggestionContinuationPlan.Completing(inserted, source) is not { } plan) return false;
        string id = "req_" + Guid.NewGuid().ToString("N")[..8];
        var request = SuggestionRequestFactory.Build(plan.Request, id, settings) with { ClipboardContext = clipboard };
        long sequence = _keySequence;
        BeginStreaming(id); SetStreamClipboard(id, clipboard);
        _ = _work.Submit(token => AcceptedSpeculationAsync(ct => local.GenerateLocalSpeculationAsync(request, ct), plan, request, settings, source, sequence, epoch, token));
        return true;
    }
    private async Task AcceptedSpeculationAsync(Func<CancellationToken, Task<string>> generate, SuggestionContinuationPlan plan,
        SuggestionRequest request, SuggestionSettings settings, FocusedField waitingSource, long sequence, long epoch, CancellationToken token)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var watchdog = new CancellationTokenSource(GenerationWatchdog);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, watchdog.Token);
        try
        {
            if (token.IsCancellationRequested || !_pendingEdits.IsCurrent(epoch)) return;
            string prediction = await generate(linked.Token).ConfigureAwait(false);
            if (token.IsCancellationRequested || !_pendingEdits.IsCurrent(epoch)) return;
            if (watchdog.IsCancellationRequested) { Fallback(); return; }
            string continuation = plan.Continuation(prediction);
            if (string.IsNullOrWhiteSpace(continuation)) { Fallback(); return; }
            var deadline = started.Elapsed + TimeSpan.FromMilliseconds(400);
            while (!token.IsCancellationRequested && _pendingEdits.IsCurrent(epoch))
            {
                var live = _focus.Refresh();
                bool target = plan.MatchesTarget(live), withSeparator = plan.MatchesTargetWithJoiningSeparator(live);
                if (target || withSeparator)
                {
                    if (live is null || SuggestionAvailability.DisabledReason(_settings, _clock.GetUtcNow(), _batterySaverOn()) is not null ||
                        SuggestionAvailability.DisabledReason(_settings, live) is not null) return;
                    if (withSeparator && continuation.StartsWith(' ')) continuation = continuation[1..];
                    live = await WaitForPresentationCadenceAsync(live, token).ConfigureAwait(false);
                    if (!(target ? plan.MatchesTarget(live) : plan.MatchesTargetWithJoiningSeparator(live))) { Fallback(); return; }
                    if (live is null || TrailingDuplicationFilter.DuplicatesTrailingText(continuation, live.TrailingText) ||
                        CompletionFinalizer.Prepare(continuation, live, settings, _engine.SpacingIsExact, _typos, true) is not { } prepared)
                    { Fallback(); return; }
                    FinishStreaming(request.RequestId);
                    Post(() =>
                    {
                        if (_keySequence == sequence && _pendingEdits.IsCurrent(epoch)) { _publishBaseline = live; _expectedDelta = 0; }
                        PresentStreamFinal(request.RequestId, prepared, live, settings.ShowFollowingWords,
                            (int)Math.Min(int.MaxValue, started.ElapsedMilliseconds), sequence, epoch, token);
                    });
                    return;
                }
                if (!InsertionTargetPolicy.IsCurrentTarget(waitingSource, live) || started.Elapsed >= deadline) { Fallback(); return; }
                await Task.Delay(20, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException) { Fallback(); }
        catch { Fallback(); }
        finally { FinishStreaming(request.RequestId); }
        void Fallback() => Post(() =>
        {
            if (_disposed || token.IsCancellationRequested || !_pendingEdits.IsCurrent(epoch)) return;
            lock (_streamGate) if (_streamId != request.RequestId) return;
            SchedulePrediction(typed: null);
        });
    }
}
