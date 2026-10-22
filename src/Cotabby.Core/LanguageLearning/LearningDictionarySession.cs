using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// The bundled-dictionary subset of Mac LearningDictionarySession, owned by one nonactivating
/// card. Word navigation keeps the captured unit/context; new occurrences and Close retire it.
/// Separate revisions reject noncooperative old lookups. Its transient cache retains at most 32
/// queries and the Mac 8MiB headword/definition estimate, not a total managed-memory guarantee.
/// Source routing, contextual explanations, vocabulary capture and references remain later ports.
/// </summary>
public sealed class LearningDictionarySession(ILearningDictionaryProviding provider) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Task> _tasks = [];
    private readonly Dictionary<CacheKey, LearningDictionaryResult> _cache = [];
    private readonly Queue<CacheKey> _cacheOrder = [];
    private readonly Queue<Notification> _notifications = [];
    private LearningDictionaryState _state = new();
    private LearningRequestLifetime? _lifetime;
    private long _revision;
    private bool _disposed;
    private bool _draining;

    public event Action<LearningDictionaryState>? StateChanged;
    public LearningDictionaryState State { get { lock (_gate) return _state; } }

    public Task OpenAsync(LearningUnit unit, string documentId, LearningLanguage language, string? speechContext = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        Task task; bool drain;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cache.Clear(); _cacheOrder.Clear();
            _state = new LearningDictionaryState { Unit = unit, DocumentId = documentId, Language = language,
                SpeechContext = speechContext };
            (task, drain) = BeginQueryLocked();
        }
        if (drain) Drain();
        return task;
    }

    public Task SelectWordAsync(LearningDictionaryWord word, Guid? targetIdentity = null)
    {
        Task task; bool drain;
        lock (_gate)
        {
            if (_disposed || _state.Unit is not { } unit || !unit.Words.Contains(word)
                || targetIdentity is not null && targetIdentity != _state.TargetIdentity) return Task.CompletedTask;
            _state = _state with { SelectedWord = word };
            (task, drain) = BeginQueryLocked();
        }
        if (drain) Drain();
        return task;
    }

    public Task ReturnToExpressionAsync(Guid? targetIdentity = null)
    {
        Task task; bool drain;
        lock (_gate)
        {
            if (_disposed || _state.Unit is null || targetIdentity is not null && targetIdentity != _state.TargetIdentity)
                return Task.CompletedTask;
            _state = _state with { SelectedWord = null };
            (task, drain) = BeginQueryLocked();
        }
        if (drain) Drain();
        return task;
    }

    public void Close(Guid? targetIdentity = null)
    {
        bool drain;
        lock (_gate)
        {
            if (_disposed || targetIdentity is not null && targetIdentity != _state.TargetIdentity) return;
            RetireLocked();
            _state = new LearningDictionaryState();
            _cache.Clear(); _cacheOrder.Clear();
            drain = EnqueueLocked();
        }
        if (drain) Drain();
    }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks; bool drain;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            RetireLocked();
            _state = new LearningDictionaryState();
            _cache.Clear(); _cacheOrder.Clear();
            tasks = _tasks.Values.ToArray();
            drain = EnqueueLocked();
        }
        if (drain) Drain();
        var all = Task.WhenAll(tasks);
        if (await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) == all)
            try { await all.ConfigureAwait(false); } catch { }
    }

    private (Task Task, bool Drain) BeginQueryLocked()
    {
        RetireLocked();
        long revision = _revision;
        var lifetime = new LearningRequestLifetime(); _lifetime = lifetime;
        _state = _state with { TargetIdentity = Guid.NewGuid(), RequestIdentity = Guid.NewGuid(),
            Status = LearningDictionaryContentStatus.Loading, Result = null, ErrorMessage = null };
        var snapshot = _state;
        bool drain = EnqueueLocked();
        var task = Task.Run(() => RunAsync(revision, snapshot, lifetime), CancellationToken.None);
        _tasks.Add(revision, task);
        return (task, drain);
    }

    private async Task RunAsync(long revision, LearningDictionaryState snapshot, LearningRequestLifetime lifetime)
    {
        bool CurrentLocked() => !_disposed && _revision == revision && !lifetime.Token.IsCancellationRequested
            && _state.Unit is not null && _state.RequestIdentity == snapshot.RequestIdentity;
        try
        {
            var key = new CacheKey(snapshot.Language, snapshot.IsExpression, snapshot.Query);
            LearningDictionaryResult? result;
            lock (_gate)
            {
                if (!CurrentLocked()) return;
                result = _cache.GetValueOrDefault(key);
            }
            if (result is null)
            {
                result = await provider.LookupAsync(snapshot.Query, snapshot.Language, snapshot.IsExpression, lifetime.Token).ConfigureAwait(false);
                if (result is null) throw new InvalidOperationException();
                lock (_gate)
                {
                    if (!CurrentLocked()) return;
                    _cache[key] = result; _cacheOrder.Enqueue(key);
                    while (_cacheOrder.Count > 32 || _cache.Values.Sum(EstimatedBytes) > 8 * 1024 * 1024)
                        _cache.Remove(_cacheOrder.Dequeue());
                }
            }
            bool drain;
            lock (_gate)
            {
                if (!CurrentLocked()) return;
                _state = _state with { Status = LearningDictionaryContentStatus.Result, Result = result };
                drain = EnqueueLocked();
            }
            if (drain) Drain();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            bool drain;
            lock (_gate)
            {
                if (!CurrentLocked()) return;
                _state = _state with { Status = LearningDictionaryContentStatus.Failed, Result = null,
                    ErrorMessage = error is LearningLexiconException fixedFailure ? fixedFailure.Failure switch
                    {
                        // Mac can offer a separate system-word dictionary after a resource error.
                        // The bundled-only Windows card must not promise that unported fallback.
                        LearningLexiconFailure.MissingResource => "The local expression dictionary is unavailable.",
                        LearningLexiconFailure.UnreadableResource => "The local expression dictionary could not be read.",
                        _ => "This lookup text is unavailable.",
                    }
                        : "The dictionary could not complete this lookup." };
                drain = EnqueueLocked();
            }
            if (drain) Drain();
        }
        finally
        {
            try { await lifetime.DisposeAsync().ConfigureAwait(false); } catch { }
            lock (_gate) { _tasks.Remove(revision); if (ReferenceEquals(_lifetime, lifetime)) _lifetime = null; }
        }
    }

    private static long EstimatedBytes(LearningDictionaryResult result) => result is LearningDictionaryResult.Entries entries
        ? entries.Values.Sum(entry => (long)Encoding.UTF8.GetByteCount(entry.Headword)
            + entry.Senses.Sum(sense => sense.Definitions.Sum(definition => (long)Encoding.UTF8.GetByteCount(definition)))) : 0;

    private void RetireLocked()
    {
        ++_revision;
        if (_lifetime is { } lifetime) _ = ObserveRetirementAsync(lifetime.Retire());
        _lifetime = null;
    }
    private static async Task ObserveRetirementAsync(Task task) { try { await task.ConfigureAwait(false); } catch { } }
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
            foreach (Action<LearningDictionaryState> handler in next.Handlers.GetInvocationList())
                try { handler(next.State); } catch { /* Presentation callbacks do not own session state. */ }
        }
    }
    private readonly record struct CacheKey(LearningLanguage Language, bool Expression, string Text);
    private sealed record Notification(LearningDictionaryState State, Action<LearningDictionaryState>? Handlers);
}
