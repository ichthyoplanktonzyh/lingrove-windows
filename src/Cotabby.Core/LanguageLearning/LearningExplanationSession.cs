namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Ports Mac LearningExplanationSession. A card owns this lightweight target/presentation state;
/// the borrowed app service owns generation/cache. Navigation restores saved results without
/// generating. Target/settings changes and stop retire work before uncooperative replies return.
/// Locks/revisions replace MainActor sequencing; notifications leave the lock in transition order.
/// This type never reads an editor, constructs a runtime, or adopts generated text into a notebook.
/// </summary>
public sealed class LearningExplanationSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ILearningExplanationGenerating? _service;
    private readonly Queue<Notification> _notifications = [];
    private LearningExplanationState _state = new();
    private LearningRequestLifetime? _lookup, _generation;
    private long _revision;
    private bool _draining;
    private bool _disposed;

    public LearningExplanationSession(ILearningExplanationGenerating? service = null)
    {
        _service = service;
        if (service is not null) service.Changed += OnServiceChanged;
    }
    public event Action<LearningExplanationState>? StateChanged;
    public LearningExplanationState State { get { lock (_gate) return _state; } }
    public bool IsConfigured => _service?.IsConfigured == true;

    public void SetTarget(LearningExplanationInput? input, LearningExplanationRecord? saved = null)
    {
        input = input is not null && LearningExplanationPolicy.IsValid(input) ? input : null;
        long revision; bool drain = false;
        lock (_gate)
        {
            if (_disposed) return;
            if (_state.Input != input)
            {
                RetireLocked();
                _state = _state with { Input = input, Record = null, ErrorMessage = null };
                drain = EnqueueLocked();
            }
            revision = _revision;
        }
        if (drain) Drain();
        // A notebook's synchronous lookup must be a cheap index lookup. Keep foreign callbacks
        // outside the session lock and recheck revision afterwards, including reentrant clear.
        try { saved ??= input is null ? null : _service?.SavedRecord(input); }
        catch { saved = null; } // Saved lookup failure is a miss, never an unsolicited model run.
        lock (_gate)
        {
            if (!CurrentLocked(revision, input)) return;
            if (_state.Record is null && !_state.IsGenerating && Accept(saved, input))
            {
                _state = _state with { Record = saved };
                drain = EnqueueLocked();
            }
            else drain = false;
            if (_state.Record is null && !_state.IsGenerating && _lookup is null) RestoreSavedLocked();
        }
        if (drain) Drain();
    }

    public Task ExplainAsync(bool refresh = false)
    {
        Task task; bool drain;
        lock (_gate)
        {
            if (_disposed || _service is null || _state.Input is not { } input
                || _state.IsGenerating || !refresh && _state.Record is not null) return Task.CompletedTask;
            RetireLocked();
            _state = _state with { IsGenerating = true, ErrorMessage = null };
            var lifetime = new LearningRequestLifetime(); _generation = lifetime;
            long revision = _revision;
            string requestId = $"req_{Guid.NewGuid():N}";
            var owner = new WeakReference<LearningExplanationSession>(this);
            var service = _service;
            drain = EnqueueLocked();
            task = Task.Run(() => GenerateAsync(owner, service, input, revision, requestId, refresh, lifetime));
        }
        if (drain) Drain();
        return task;
    }

    public void Cancel()
    {
        bool drain;
        lock (_gate)
        {
            if (_disposed) return;
            RetireLocked(); drain = EnqueueLocked();
        }
        if (drain) Drain();
    }

    public void Clear()
    {
        bool drain;
        lock (_gate)
        {
            if (_disposed) return;
            RetireLocked(); _state = _state with { Input = null, Record = null, ErrorMessage = null };
            drain = EnqueueLocked();
        }
        if (drain) Drain();
    }

    private void OnServiceChanged()
    {
        bool drain;
        lock (_gate)
        {
            if (_disposed) return;
            RetireLocked(); _state = _state with { Record = null, ErrorMessage = null };
            RestoreSavedLocked(); drain = EnqueueLocked();
        }
        if (drain) Drain();
    }

    private void RestoreSavedLocked()
    {
        if (_service is null || _state.Input is not { } input) return;
        var lifetime = new LearningRequestLifetime(); _lookup = lifetime;
        long revision = _revision;
        var owner = new WeakReference<LearningExplanationSession>(this);
        var service = _service;
        _ = Task.Run(() => RestoreSavedAsync(owner, service, input, revision, lifetime));
    }

    private static async Task RestoreSavedAsync(WeakReference<LearningExplanationSession> owner, ILearningExplanationGenerating service,
        LearningExplanationInput input, long revision, LearningRequestLifetime lifetime)
    {
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var saved = await service.LoadSavedAsync(input).ConfigureAwait(false);
            PublishSaved(owner, input, revision, lifetime, saved);
        }
        catch { /* A failed saved lookup neither publishes foreign text nor starts generation. */ }
        finally { await FinishAsync(owner, lifetime).ConfigureAwait(false); }
    }

    private static async Task GenerateAsync(WeakReference<LearningExplanationSession> owner, ILearningExplanationGenerating service,
        LearningExplanationInput input, long revision, string requestId,
        bool refresh, LearningRequestLifetime lifetime)
    {
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var record = await service.GenerateAsync(input, requestId, refresh, lifetime.Token).ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            if (!PublishGenerated(owner, input, revision, record)) return;
            // This is the Mac session's admitted-record hook, not proof of WPF pixel display.
            try { service.RecordDisplay(requestId); } catch { }
        }
        catch (Exception error)
        {
            PublishFailure(owner, input, revision, lifetime, error);
        }
        finally { await FinishAsync(owner, lifetime).ConfigureAwait(false); }
    }

    // These synchronous helpers acquire self only after the foreign await has completed. Keeping
    // the worker methods static prevents a state machine from rooting the card/session while an
    // uncooperative service hangs; this is the C# counterpart of Mac's [weak self] task closures.
    private static void PublishSaved(WeakReference<LearningExplanationSession> owner, LearningExplanationInput input,
        long revision, LearningRequestLifetime lifetime, LearningExplanationRecord? saved)
    {
        if (!owner.TryGetTarget(out var session)) return;
        bool drain = false;
        lock (session._gate)
        {
            if (!session.CurrentLocked(revision, input) || lifetime.Token.IsCancellationRequested) return;
            if (ReferenceEquals(session._lookup, lifetime)) session._lookup = null;
            if (session._state.Record is null && !session._state.IsGenerating && session.Accept(saved, input))
            { session._state = session._state with { Record = saved }; drain = session.EnqueueLocked(); }
        }
        if (drain) session.Drain();
    }
    private static bool PublishGenerated(WeakReference<LearningExplanationSession> owner, LearningExplanationInput input,
        long revision, LearningExplanationRecord record)
    {
        if (!owner.TryGetTarget(out var session)) return false;
        bool drain;
        lock (session._gate)
        {
            if (!session.CurrentLocked(revision, input)) return false;
            if (!session.Accept(record, input)) throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
            session._state = session._state with { Record = record, IsGenerating = false };
            drain = session.EnqueueLocked();
        }
        if (drain) session.Drain();
        return true;
    }
    private static void PublishFailure(WeakReference<LearningExplanationSession> owner, LearningExplanationInput input,
        long revision, LearningRequestLifetime lifetime, Exception error)
    {
        if (!owner.TryGetTarget(out var session)) return;
        bool drain;
        lock (session._gate)
        {
            if (!session.CurrentLocked(revision, input)) return;
            session._state = session._state with { IsGenerating = false,
                ErrorMessage = error is OperationCanceledException || lifetime.Token.IsCancellationRequested ? session._state.ErrorMessage
                    : error is LearningExplanationException or LearningTranslationException ? error.Message
                    : new LearningExplanationException(LearningExplanationFailure.ProviderFailure).Message };
            drain = session.EnqueueLocked();
        }
        if (drain) session.Drain();
    }

    private bool Accept(LearningExplanationRecord? record, LearningExplanationInput? input) => record is not null && input is not null
        && record.Input is not null && LearningExplanationPolicy.IsValid(record.Input)
        && record.Input == input && record.Result is not null && LearningExplanationPolicy.IsValid(record.Result)
        && Enum.IsDefined(record.Language) && (_service is null || record.Language == _service.Language);
    private bool CurrentLocked(long revision, LearningExplanationInput? input) =>
        !_disposed && _revision == revision && _state.Input == input;
    private void RetireLocked()
    {
        ++_revision;
        if (_lookup is { } lookup) _ = ObserveRetirement(lookup.Retire());
        if (_generation is { } generation) _ = ObserveRetirement(generation.Retire());
        _lookup = null; _generation = null;
        _state = _state with { Revision = Guid.NewGuid(), IsGenerating = false };
    }
    private static async Task FinishAsync(WeakReference<LearningExplanationSession> owner, LearningRequestLifetime lifetime)
    {
        try { await lifetime.DisposeAsync().ConfigureAwait(false); } catch { }
        if (!owner.TryGetTarget(out var session)) return;
        lock (session._gate)
        {
            if (ReferenceEquals(session._lookup, lifetime)) session._lookup = null;
            if (ReferenceEquals(session._generation, lifetime)) session._generation = null;
        }
    }
    private static async Task ObserveRetirement(Task task) { try { await task.ConfigureAwait(false); } catch { } }

    public ValueTask DisposeAsync()
    {
        bool drain;
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            RetireLocked(); _disposed = true;
            _state = _state with { Input = null, Record = null, ErrorMessage = null };
            drain = EnqueueLocked();
            StateChanged = null;
        }
        if (_service is not null) _service.Changed -= OnServiceChanged;
        if (drain) Drain();
        // A card only retires borrowed work, as on Mac. It must not wait for an uncooperative
        // service/saved lookup during teardown. Each task releases its own lifetime when it
        // finishes; the app-owned service/runtime independently drains native resources.
        return ValueTask.CompletedTask;
    }
    private bool EnqueueLocked()
    {
        _notifications.Enqueue(new(_state, StateChanged));
        if (_draining) return false;
        return _draining = true;
    }
    private void Drain()
    {
        while (true)
        {
            Notification next;
            lock (_gate)
            {
                if (_notifications.Count == 0) { _draining = false; return; }
                next = _notifications.Dequeue();
            }
            if (next.Handlers is null) continue;
            foreach (Action<LearningExplanationState> handler in next.Handlers.GetInvocationList())
                try { handler(next.State); } catch { }
        }
    }
    private sealed record Notification(LearningExplanationState State, Action<LearningExplanationState>? Handlers);
}
