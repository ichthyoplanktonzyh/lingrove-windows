using System.Text;

namespace Cotabby.Core.LanguageLearning;

/// <summary>
/// Adapts Mac LearningExplanationService: explicit generation, saved-first lookup, frozen profile/
/// response-language checks, and a FIFO cache bounded by 48 records and the Mac 4MiB text estimate.
/// The app must own this once with an explanation-only runtime behind its facade factory. This
/// service owns request lifetimes/facades, never the runtime, UI, editor, or notebook storage.
/// </summary>
public sealed class LearningExplanationService : ILearningExplanationGenerating, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<LearningModelSnapshot, ILearningExplanationEngine> _engineFactory;
    private readonly TimeProvider _clock;
    private readonly Dictionary<Key, LearningExplanationRecord> _cache = [];
    private readonly Queue<Key> _cacheOrder = [];
    private readonly Dictionary<LearningRequestLifetime, Task<LearningExplanationRecord>> _work = [];
    private readonly HashSet<Task> _savedWork = [];
    private LearningModelSnapshot _profile;
    private LearningLanguage _language;
    private Task? _disposal;

    public LearningExplanationService(LearningModelSnapshot profile, LearningLanguage language,
        Func<LearningModelSnapshot, ILearningExplanationEngine> engineFactory, TimeProvider? clock = null)
    {
        _profile = (profile ?? throw new ArgumentNullException(nameof(profile))).ForExplanation();
        _language = language;
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _clock = clock ?? TimeProvider.System;
    }

    public event Action? Changed;
    /// <summary>Future composition supplies persistence; generated teaching prose is not an authored meaning.</summary>
    public event Action<LearningExplanationRecord>? Explained;
    /// <summary>Hook for the separate telemetry port; no metrics or raw text are logged here.</summary>
    public event Action<string>? Displayed;
    public Func<LearningExplanationInput, LearningLanguage, Task<LearningExplanationRecord?>>? LoadPersisted { get; set; }
    public Func<LearningExplanationInput, LearningLanguage, LearningExplanationRecord?>? LookupSaved { get; set; }
    public LearningLanguage Language { get { lock (_gate) return _language; } }
    public bool IsConfigured { get { lock (_gate) return Configured(_profile); } }

    /// <summary>
    /// App settings publish one committed snapshot. Retirement sets direct cancellation flags
    /// under the admission lock; foreign callbacks run asynchronously outside it. Cached records
    /// retain their complete profile keys, as on Mac, instead of mixing new settings into old work.
    /// </summary>
    public void ApplyConfiguration(LearningModelSnapshot profile, LearningLanguage language)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile = profile.ForExplanation();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (_profile == profile && _language == language) return;
            _profile = profile; _language = language;
            foreach (var lifetime in _work.Keys) _ = ObserveRetirement(lifetime.Retire());
        }
        Notify(Changed);
    }

    public LearningExplanationRecord? SavedRecord(LearningExplanationInput input)
    {
        var (language, completion) = AdmitSavedLookup();
        try
        {
            var record = AcceptSaved(LookupSaved?.Invoke(input, language), input, language);
            lock (_gate) return _disposal is null && _language == language ? record : null;
        }
        catch (Exception error) { throw FixedFailure(error); }
        finally { FinishSavedLookup(completion); }
    }

    public async Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input)
    {
        var (language, completion) = AdmitSavedLookup();
        try { return await LoadSavedAsync(input, language).ConfigureAwait(false); }
        catch (Exception error) { throw FixedFailure(error); }
        finally { FinishSavedLookup(completion); }
    }

    private async Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input, LearningLanguage language)
    {
        var record = LoadPersisted is { } load
            ? AcceptSaved(await load(input, language).ConfigureAwait(false), input, language) : null;
        record ??= AcceptSaved(LookupSaved?.Invoke(input, language), input, language);
        lock (_gate) return _disposal is null && _language == language ? record : null;
    }

    private (LearningLanguage Language, TaskCompletionSource Completion) AdmitSavedLookup()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _savedWork.Add(completion.Task);
            return (_language, completion);
        }
    }
    private void FinishSavedLookup(TaskCompletionSource completion)
    {
        completion.TrySetResult();
        lock (_gate) _savedWork.Remove(completion.Task);
    }

    // Mac supplies these callbacks from its notebook policy. The Windows store is not ported yet;
    // defend the future foreign boundary so a corrupt or mis-keyed row cannot become a cache hit.
    private static LearningExplanationRecord? AcceptSaved(LearningExplanationRecord? record,
        LearningExplanationInput input, LearningLanguage language) => record is not null
            && record.Input is not null && record.Result is not null && record.Input == input && record.Language == language
            && LearningExplanationPolicy.IsValid(record.Input) && LearningExplanationPolicy.IsValid(record.Result) ? record : null;

    public LearningExplanationRecord? Cached(LearningExplanationInput input)
    {
        lock (_gate) return _cache.GetValueOrDefault(new(input, _language, _profile));
    }

    public Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId,
        bool refresh = false, CancellationToken cancellationToken = default)
    {
        // Preserve the Mac service's invalid-capture precedence over model configuration.
        if (input is null || !LearningExplanationPolicy.IsValid(input))
            return Task.FromException<LearningExplanationRecord>(new LearningExplanationException(LearningExplanationFailure.InvalidContent));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            var lifetime = new LearningRequestLifetime();
            var key = new Key(input, _language, _profile);
            // Clear a WPF SynchronizationContext before saved lookup, factory, prompt and model IO.
            var task = Task.Run(() => RunAsync(key, refresh, lifetime, cancellationToken));
            _work.Add(lifetime, task);
            return task;
        }
    }

    private async Task<LearningExplanationRecord> RunAsync(Key key, bool refresh, LearningRequestLifetime lifetime, CancellationToken caller)
    {
        using var registration = caller.Register(() => _ = ObserveRetirement(lifetime.Retire()));
        void Validate()
        {
            caller.ThrowIfCancellationRequested(); lifetime.Token.ThrowIfCancellationRequested();
            lock (_gate)
                if (_disposal is not null || _profile != key.Profile || _language != key.Language) throw new OperationCanceledException();
        }
        try
        {
            Validate();
            if (!refresh)
            {
                var saved = await LoadSavedAsync(key.Input, key.Language).ConfigureAwait(false);
                Validate();
                lock (_gate) saved ??= _cache.GetValueOrDefault(key);
                if (saved is not null) { Validate(); return saved; }
            }
            Validate();
            if (!Configured(key.Profile)) throw new LearningExplanationException(LearningExplanationFailure.InstructionModelRequired);
            // The factory is an app-owned effect. It must enforce this role's endpoint consent
            // and metadata gate; this service's filename capability check cannot authorize IO.
            LearningExplanationResult result;
            await using (var engine = _engineFactory(key.Profile))
            {
                Validate();
                result = await engine.GenerateAsync(key.Input, key.Language,
                    LearningExplanationModelPolicy.ResolveOutputTokens(key.Profile.MaximumOutputTokens), lifetime.Token).ConfigureAwait(false);
                Validate();
            }
            Validate();
            if (result is null || !LearningExplanationPolicy.IsValid(result))
                throw new LearningExplanationException(LearningExplanationFailure.InvalidContent);
            var record = new LearningExplanationRecord(Guid.NewGuid(), key.Input, key.Language, result,
                key.Profile.Backend == LearningModelBackend.Local ? key.Profile.LocalModel : key.Profile.EndpointModel, _clock.GetUtcNow());
            lock (_gate)
            {
                Validate();
                if (!_cache.ContainsKey(key)) _cacheOrder.Enqueue(key);
                _cache[key] = record;
                while (_cacheOrder.Count > 48 || _cache.Values.Sum(Bytes) > 4 * 1024 * 1024)
                    _cache.Remove(_cacheOrder.Dequeue());
            }
            Notify(Explained, record);
            return record;
        }
        catch (Exception error)
        {
            Validate();
            throw FixedFailure(error);
        }
        finally
        {
            // Native facade disposal above drains its lease before foreign cancellation callbacks
            // are joined here. A callback may itself be waiting for that lease to become available.
            try { await lifetime.DisposeAsync().ConfigureAwait(false); } catch { }
            lock (_gate) _work.Remove(lifetime);
        }
    }

    public void RecordDisplay(string requestId) => Notify(Displayed, requestId);

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            foreach (var lifetime in _work.Keys) _ = ObserveRetirement(lifetime.Retire());
            _cache.Clear(); _cacheOrder.Clear();
            return new(_disposal = DrainAsync(_work.Values.Cast<Task>().Concat(_savedWork).ToArray()));
        }
    }
    private static async Task DrainAsync(Task[] tasks) { try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { } }
    private static bool Configured(LearningModelSnapshot profile)
    {
        string name = profile.Backend == LearningModelBackend.Local ? profile.LocalModel : profile.EndpointModel;
        return Enum.IsDefined(profile.Backend) && !string.IsNullOrEmpty(name)
            && LearningExplanationModelPolicy.SupportsGeneralInstructions(name, profile.Template);
    }
    private static long Bytes(LearningExplanationRecord record) => (long)Encoding.UTF8.GetByteCount(record.Input.Target)
        + Encoding.UTF8.GetByteCount(record.Input.Context) + Encoding.UTF8.GetByteCount(record.Input.DictionaryForm)
        + Encoding.UTF8.GetByteCount(record.Result.Meaning) + Encoding.UTF8.GetByteCount(record.Result.Explanation);
    private static async Task ObserveRetirement(Task task) { try { await task.ConfigureAwait(false); } catch { } }
    private static Exception FixedFailure(Exception error) =>
        error is OperationCanceledException or LearningExplanationException or LearningTranslationException ? error
            : new LearningExplanationException(LearningExplanationFailure.ProviderFailure);
    private static void Notify(Action? handlers)
    {
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch { }
    }
    private static void Notify<T>(Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(value); } catch { }
    }
    private sealed record Key(LearningExplanationInput Input, LearningLanguage Language, LearningModelSnapshot Profile);
}
