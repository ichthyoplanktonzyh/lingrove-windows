using System.Text;
using System.Collections.Frozen;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// The bundled-dictionary subset of Mac LearningDictionarySession, owned by one nonactivating
/// card. Word navigation keeps the captured unit/context; new occurrences and Close retire it.
/// Separate revisions reject noncooperative old lookups. Its transient cache retains at most 32
/// queries and the Mac 8MiB headword/definition estimate, not a total managed-memory guarantee.
/// Contextual generation and lazy dictionary pages follow the same target's separate lifetimes.
/// Source routing, durable vocabulary capture and references remain later ports.
/// </summary>
public sealed partial class LearningDictionarySession : IAsyncDisposable
{
    private readonly ILearningDictionaryProviding _provider;
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
    private readonly ILearningDictionaryRouter? _router;
    private DictionaryConfiguration? _configuration;
    private DictionarySource? _sourceOverride;

    public LearningDictionarySession(ILearningDictionaryProviding provider, ILearningExplanationGenerating? explanationService = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _explanationService = explanationService;
        _router = provider as ILearningDictionaryRouter;
        if (_router is not null) { _configuration = _router.Configuration; _router.ConfigurationChanged += ConfigurationChanged; }
    }

    public event Action<LearningDictionaryState>? StateChanged;
    public LearningDictionaryState State { get { lock (_gate) return _state; } }

    public Task OpenAsync(LearningUnit unit, string documentId, LearningLanguage? language, string? speechContext = null,
        LearningExplanationInput? explanationInput = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        TargetTransition transition;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cache.Clear(); _cacheOrder.Clear();
            _sourceOverride = null;
            _state = new LearningDictionaryState { Unit = unit, DocumentId = documentId, Language = language,
                SpeechContext = speechContext };
            _unitExplanationInput = language is { } known ? ValidUnitInput(unit, known, explanationInput
                ?? LearningExplanationInputFactory.FromUnscopedUnit(unit, known)) : null;
            transition = BeginTargetLocked(resetExplanation: true);
        }
        return ActivateTarget(transition);
    }

    public Task SelectWordAsync(LearningDictionaryWord word, Guid? targetIdentity = null)
    {
        TargetTransition transition;
        lock (_gate)
        {
            if (_disposed || _state.Unit is not { } unit || !unit.Words.Contains(word)
                || targetIdentity is not null && targetIdentity != _state.TargetIdentity) return Task.CompletedTask;
            _state = _state with { SelectedWord = word, ReferenceQuery = null };
            _sourceOverride = null;
            transition = BeginTargetLocked();
        }
        return ActivateTarget(transition);
    }

    public Task ReturnToExpressionAsync(Guid? targetIdentity = null)
    {
        TargetTransition transition;
        lock (_gate)
        {
            if (_disposed || _state.Unit is null || targetIdentity is not null && targetIdentity != _state.TargetIdentity)
                return Task.CompletedTask;
            _state = _state with { SelectedWord = null, ReferenceQuery = null };
            _sourceOverride = null;
            transition = BeginTargetLocked();
        }
        return ActivateTarget(transition);
    }

    public void Close(Guid? targetIdentity = null)
    {
        bool drain; ContextualTarget? retired;
        lock (_gate)
        {
            if (_disposed || targetIdentity is not null && targetIdentity != _state.TargetIdentity) return;
            RetireLocked();
            retired = _contextual; _contextual = null; _unitExplanationInput = null;
            if (retired is not null) _ = ObserveRetirementAsync(retired.Owner.Lifetime.Retire());
            _state = new LearningDictionaryState();
            _cache.Clear(); _cacheOrder.Clear();
            drain = EnqueueLocked();
        }
        RetireContextualTarget(retired);
        if (drain) Drain();
    }

    public Task SelectSourceAsync(DictionarySource source, Guid? targetIdentity = null)
    {
        Task task; bool drain;
        lock (_gate)
        {
            if (_disposed || _state.Unit is null || _configuration is null || !_configuration.Sources(_state.Language).Contains(source)
                || targetIdentity is not null && targetIdentity != _state.TargetIdentity) return Task.CompletedTask;
            _sourceOverride = source; _state = _state with { Page = LearningDictionaryPage.Dictionary }; (task, drain) = RequestDictionaryLocked();
        }
        if (drain) Drain(); return task;
    }
    public Task FollowReferenceAsync(string query, Guid? targetIdentity = null)
    {
        long revision; LearningLanguage? language; bool drain; Task task;
        lock (_gate)
        {
            if (_disposed || _router is null || _state.Unit is null || !DictionaryContentPolicy.ValidQuery(query)
                || targetIdentity is not null && targetIdentity != _state.TargetIdentity) return Task.CompletedTask;
            RetireLocked(); revision = _revision; language = _state.Language;
            _state = _state with { Status = LearningDictionaryContentStatus.Loading, Result = null, ErrorMessage = null };
            drain = EnqueueLocked(); task = Task.Run(() => FollowReferenceCoreAsync(query, language, revision), CancellationToken.None); _tasks.Add(revision, task);
        }
        if (drain) Drain(); return task;
    }
    private async Task FollowReferenceCoreAsync(string query, LearningLanguage? language, long revision)
    {
        try
        {
            int count = _router!.CountWords(query, language); TargetTransition transition;
            lock (_gate)
            {
                if (_disposed || _revision != revision || _state.Unit is null) return;
                _sourceOverride = _state.SelectedSource;
                _state = _state with { SelectedWord = null, ReferenceQuery = query, ReferenceIsExpression = count > 1 };
                transition = BeginTargetLocked();
            }
            await ActivateTarget(transition).ConfigureAwait(false);
        }
        catch
        {
            bool drain = false;
            lock (_gate) if (!_disposed && _revision == revision && _state.Unit is not null)
            { _state = _state with { Status = LearningDictionaryContentStatus.Failed, ErrorMessage = "This dictionary reference is unavailable." }; drain = EnqueueLocked(); }
            if (drain) Drain();
        }
        finally { lock (_gate) _tasks.Remove(revision); }
    }
    public async Task<DictionaryResource?> LoadResourceAsync(string path, Guid libraryId, Guid requestIdentity, CancellationToken token = default)
    {
        DictionaryConfiguration configuration;
        lock (_gate)
        {
            if (_disposed || _router is null || _configuration is not { } current || _state.RequestIdentity != requestIdentity
                || _state.SelectedSource != DictionarySource.MDict(libraryId) || !current.Enabled.Contains(DictionarySource.MDict(libraryId))) return null;
            configuration = current;
        }
        var result = await _router.ResourceAsync(path, libraryId, configuration, token).ConfigureAwait(false);
        lock (_gate)
            return !_disposed && !token.IsCancellationRequested && ReferenceEquals(_configuration, configuration)
                && _state.RequestIdentity == requestIdentity && _state.SelectedSource == DictionarySource.MDict(libraryId) ? result : null;
    }
    private void ConfigurationChanged(DictionaryConfiguration value)
    {
        bool drain = false;
        lock (_gate)
        {
            if (_disposed) return; _configuration = value; _cache.Clear(); _cacheOrder.Clear();
            if (_sourceOverride is { } source && !value.Sources(_state.Language).Contains(source)) _sourceOverride = null;
            if (_dictionaryRequested && _state.Unit is not null) { var request = RequestDictionaryLocked(); drain = request.Drain; }
        }
        if (drain) Drain();
    }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks; bool drain; ContextualTarget? retired;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_router is not null) _router.ConfigurationChanged -= ConfigurationChanged;
            RetireLocked();
            retired = _contextual; _contextual = null; _unitExplanationInput = null;
            if (retired is not null) _ = ObserveRetirementAsync(retired.Owner.Lifetime.Retire());
            _state = new LearningDictionaryState();
            _cache.Clear(); _cacheOrder.Clear();
            tasks = _tasks.Values.ToArray();
            drain = EnqueueLocked();
        }
        RetireContextualTarget(retired);
        if (drain) Drain();
        var all = Task.WhenAll(tasks);
        if (await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) == all)
            try { await all.ConfigureAwait(false); } catch { }
    }

    private (Task Task, bool Drain) RequestDictionaryLocked()
    {
        RetireLocked();
        long revision = _revision;
        var lifetime = new LearningRequestLifetime(); _lifetime = lifetime;
        _dictionaryRequested = true;
        var configuration = _configuration;
        var available = configuration?.Sources(_state.Language) ?? [];
        var candidates = configuration is null ? [] : _sourceOverride is { } selected ? [selected]
            : configuration.AutomaticSources(_state.Language, configuration.PreferredDefinitionLanguage(System.Globalization.CultureInfo.CurrentUICulture.Name)).ToArray();
        _state = _state with { RequestIdentity = Guid.NewGuid(),
            Status = LearningDictionaryContentStatus.Loading, Result = null, ErrorMessage = null,
            AvailableSources = available, SourceLabels = available.ToFrozenDictionary(x => x, x => configuration!.Name(x)),
            SelectedSource = candidates.FirstOrDefault() is { RawValue: not null } first ? first : null };
        var snapshot = _state;
        bool drain = EnqueueLocked();
        var task = Task.Run(() => RunAsync(revision, snapshot, lifetime, configuration, candidates), CancellationToken.None);
        _tasks.Add(revision, task);
        return (task, drain);
    }

    private async Task RunAsync(long revision, LearningDictionaryState snapshot, LearningRequestLifetime lifetime,
        DictionaryConfiguration? configuration, IReadOnlyList<DictionarySource> candidates)
    {
        bool CurrentLocked() => !_disposed && _revision == revision && !lifetime.Token.IsCancellationRequested
            && _state.Unit is not null && _state.RequestIdentity == snapshot.RequestIdentity;
        try
        {
            if (_router is not null && configuration is not null)
            {
                Exception? failure = null; DictionarySource? failedSource = null;
                foreach (var source in candidates)
                {
                    try
                    {
                        var cacheKey = new CacheKey(snapshot.Language, snapshot.IsExpression, snapshot.Query, source);
                        LearningDictionaryResult? found;
                        lock (_gate) { if (!CurrentLocked()) return; found = _cache.GetValueOrDefault(cacheKey); }
                        if (found is null)
                        {
                            found = await _router.LookupSourceAsync(snapshot.Query, snapshot.Language, snapshot.IsExpression, source, configuration, lifetime.Token).ConfigureAwait(false);
                            if (found is null) throw new InvalidOperationException();
                            lock (_gate)
                            {
                                if (!CurrentLocked()) return;
                                _cache[cacheKey] = found; _cacheOrder.Enqueue(cacheKey);
                                while (_cacheOrder.Count > 32 || _cache.Values.Sum(EstimatedBytes) > 8 * 1024 * 1024) _cache.Remove(_cacheOrder.Dequeue());
                            }
                        }
                        bool notify = false;
                        lock (_gate)
                        {
                            if (!CurrentLocked()) return;
                            if (!ReferenceEquals(found, LearningDictionaryResult.NotFound))
                            { _state = _state with { Status = LearningDictionaryContentStatus.Result, Result = found, SelectedSource = source }; notify = EnqueueLocked(); }
                        }
                        if (notify) Drain(); if (!ReferenceEquals(found, LearningDictionaryResult.NotFound)) return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { failure ??= error; failedSource ??= source; }
                }
                bool finish;
                lock (_gate)
                {
                    if (!CurrentLocked()) return;
                    _state = _state with { Status = failure is null ? LearningDictionaryContentStatus.Result : LearningDictionaryContentStatus.Failed,
                        Result = failure is null ? LearningDictionaryResult.NotFound : null, SelectedSource = failedSource ?? snapshot.SelectedSource,
                        ErrorMessage = failure is null ? null : "The selected dictionary is unavailable." };
                    finish = EnqueueLocked();
                }
                if (finish) Drain(); return;
            }
            var key = new CacheKey(snapshot.Language, snapshot.IsExpression, snapshot.Query, null);
            LearningDictionaryResult? result;
            lock (_gate)
            {
                if (!CurrentLocked()) return;
                result = _cache.GetValueOrDefault(key);
            }
            if (result is null)
            {
                result = snapshot.Language is { } language
                    ? await _provider.LookupAsync(snapshot.Query, language, snapshot.IsExpression, lifetime.Token).ConfigureAwait(false)
                    : _provider is ILearningUnknownDictionaryProviding unknown
                        ? await unknown.LookupUnknownAsync(snapshot.Query, snapshot.IsExpression, lifetime.Token).ConfigureAwait(false)
                        : throw new LearningLexicalException(LearningLexicalFailure.TextProviderUnavailable);
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

    private static long EstimatedBytes(LearningDictionaryResult result) => result switch
    {
        LearningDictionaryResult.Entries entries => entries.Values.Sum(entry => (long)Encoding.UTF8.GetByteCount(entry.Headword)
            + entry.Senses.Sum(sense => sense.Definitions.Sum(definition => (long)Encoding.UTF8.GetByteCount(definition)))),
        LearningDictionaryResult.Article article => (long)Encoding.UTF8.GetByteCount(article.Html) + Encoding.UTF8.GetByteCount(article.Text),
        _ => 0,
    };

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
    private readonly record struct CacheKey(LearningLanguage? Language, bool Expression, string Text, DictionarySource? Source)
    {
        public bool Equals(CacheKey other) => Language == other.Language && Expression == other.Expression && Source == other.Source
            && Cotabby.Core.Text.CanonicalStringComparer.Instance.Equals(Text, other.Text);
        public override int GetHashCode() => HashCode.Combine(Language, Expression, Source, Cotabby.Core.Text.CanonicalStringComparer.Instance.GetHashCode(Text));
    }
    private sealed record Notification(LearningDictionaryState State, Action<LearningDictionaryState>? Handlers);
}
