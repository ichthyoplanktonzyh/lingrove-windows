using Cotabby.Core.LanguageLearning;
using LearningExplanationRecord = Cotabby.Core.LanguageLearning.LearningExplanationRecord;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Vocabulary;

/// <summary>UI-facing lease over the app-owned canonical Store. Revisioned snapshots and pending
/// capture keys are local presentation state; the borrowed backend alone owns durable mutations.</summary>
internal sealed class CanonicalWritingViewStore : IWritingVocabularyStore, IAsyncDisposable
{
    private readonly ICanonicalVocabularyStore _backend;
    private readonly SynchronizationContext _ui;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _pending = [];
    private readonly HashSet<Task> _work = [];
    private FrozenVocabularyImportSnapshot? _snapshot;
    private string? _error;
    private bool _loaded, _closing;
    private Task? _load, _disposal;

    internal CanonicalWritingViewStore(ICanonicalVocabularyStore backend, SynchronizationContext ui)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _subscription = _backend.Subscribe(Apply);
    }
    public event Action? Changed;
    public Action<VocabularySelection?>? OpenSelection { get; internal set; }
    internal Func<VocabularyCapture, VocabularyCapture>? EnrichCapture { get; set; }
    public bool IsLoaded { get { lock (_gate) return _loaded; } }
    public string? ErrorMessage { get { lock (_gate) return _error; } }
    internal FrozenVocabularyImportSnapshot? Snapshot { get { lock (_gate) return _snapshot; } }
    public bool Contains(VocabularyCapture capture)
    {
        try { lock (_gate) return _snapshot is { } snapshot && CanonicalWritingPolicy.SavedDestination(capture, snapshot.Document) is not null; }
        catch (Exception error) when (error is VocabularyContentValidationException or VocabularyException or ArgumentException) { return false; }
    }
    public bool IsPending(VocabularyCapture capture)
    {
        try { lock (_gate) return _pending.ContainsKey(VocabularyCapturePolicy.CaptureKey(capture)); }
        catch (Exception error) when (error is VocabularyContentValidationException or ArgumentException) { return false; }
    }
    public void Show(VocabularyCapture capture)
    {
        VocabularyExplanationDestination? destination;
        lock (_gate) destination = _snapshot is { } snapshot ? CanonicalWritingPolicy.SavedDestination(capture, snapshot.Document) : null;
        OpenSelection?.Invoke(destination is { } value ? new(value.EntryId, value.SenseId) : null);
    }
    public Task LoadAsync()
    {
        lock (_gate)
        {
            if (_closing) return Task.FromException(new ObjectDisposedException(nameof(CanonicalWritingViewStore)));
            if (_loaded) return Task.CompletedTask;
            return _load ??= StartLocked(async () =>
            {
                try { await RefreshAsync().ConfigureAwait(false); }
                catch (Exception error) { Report(error, unavailable: true); }
                finally { lock (_gate) _load = null; }
                return true;
            });
        }
    }
    public Task<bool> SaveAsync(VocabularyCapture capture)
    {
        VocabularyCapture enriched;
        try { enriched = EnrichCapture?.Invoke(capture) ?? capture; }
        catch (Exception error) { Report(error); return Task.FromResult(false); }
        return Change(capture, () => _backend.SaveCaptureAsync(enriched));
    }
    public Task<bool> RemoveAsync(VocabularyCapture capture) => Change(capture, () => _backend.RemoveCaptureAsync(capture));
    private Task<bool> Change(VocabularyCapture capture, Func<Task<CanonicalWritingReceipt>> action)
    {
        string key;
        try { key = VocabularyCapturePolicy.CaptureKey(capture); }
        catch (Exception error) when (error is VocabularyContentValidationException or ArgumentException) { Report(error); return Task.FromResult(false); }
        Task<bool> task;
        lock (_gate)
        {
            if (_closing) return Task.FromException<bool>(new ObjectDisposedException(nameof(CanonicalWritingViewStore)));
            _pending[key] = _pending.GetValueOrDefault(key) + 1;
            task = StartLocked(async () =>
            {
                try { return await PerformAsync(action).ConfigureAwait(false); }
                finally
                {
                    lock (_gate) { int left = _pending[key] - 1; if (left == 0) _pending.Remove(key); else _pending[key] = left; }
                    Notify();
                }
            });
        }
        Notify(); return task;
    }
    internal Task<bool> SaveExplanationAsync(LearningExplanationRecord record) => Mutation(() => _backend.SaveExplanationAsync(record));
    internal Task<bool> AdoptExplanationAsync(LearningExplanationRecord record, VocabularyExplanationDestination destination) =>
        Mutation(() => _backend.AdoptExplanationAsync(record, destination));
    internal LearningExplanationRecord? Explanation(LearningExplanationInput input, LearningLanguage language)
    {
        lock (_gate) return _snapshot is { } snapshot ? CanonicalWritingPolicy.FindExplanation(input, language, snapshot.Document) : null;
    }
    private Task<bool> Mutation(Func<Task<CanonicalWritingReceipt>> action)
    {
        lock (_gate) return _closing ? Task.FromException<bool>(new ObjectDisposedException(nameof(CanonicalWritingViewStore)))
            : StartLocked(() => PerformAsync(action));
    }
    private async Task<bool> PerformAsync(Func<Task<CanonicalWritingReceipt>> action)
    {
        CanonicalWritingReceipt receipt;
        try { receipt = await action().ConfigureAwait(false); }
        catch (Exception error) { Report(error); return false; }
        // A committed operation remains successful even if a subsequent read fails. Mark the
        // presentation unavailable until an acknowledged snapshot arrives or a reload succeeds.
        try { await RefreshAsync().ConfigureAwait(false); }
        catch (Exception error) { Report(error, unavailable: true, acknowledgedRevision: receipt.Revision); }
        return receipt.Changed;
    }
    private async Task RefreshAsync() => Apply(await _backend.ReadAsync().ConfigureAwait(false));
    private void Apply(FrozenVocabularyImportSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_snapshot is { } current && snapshot.Revision < current.Revision) return;
            _snapshot = snapshot; _loaded = true; _error = null;
        }
        Notify();
    }
    private void Report(Exception error, bool unavailable = false, ulong acknowledgedRevision = 0)
    {
        lock (_gate)
        {
            // A newer acknowledged observer may have arrived while a read was failing.
            if (unavailable && _loaded && _snapshot is { } current && current.Revision >= acknowledgedRevision) return;
            if (unavailable) _loaded = false;
            _error = (error as VocabularyException ?? new VocabularyException(VocabularyFailure.InvalidArchive)).Message;
        }
        Notify();
    }
    private void Notify()
    {
        try
        {
            _ui.Post(_ =>
            {
                lock (_gate) if (_closing) return;
                if (Changed is { } handlers) foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch { }
            }, null);
        }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { }
    }
    private Task<T> StartLocked<T>(Func<Task<T>> operation)
    {
        var task = Task.Run(operation); _work.Add(task);
        _ = task.ContinueWith(done => { lock (_gate) _work.Remove(done); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate) { _closing = true; return new(_disposal ??= DrainAsync(_work.ToArray())); }
    }
    private async Task DrainAsync(Task[] accepted)
    {
        try { await Task.WhenAll(accepted).ConfigureAwait(false); } catch { }
        _subscription.Dispose();
    }
}
