using System.Threading.Channels;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using VocabularyClassificationCatalog = Cotabby.Core.Vocabulary.Resources.VocabularyClassificationCatalog;
using LiveRecord = Cotabby.Core.LanguageLearning.LearningExplanationRecord;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Isolated candidate for the eventual single app-owned Store. It owns its database,
/// serializes operations and emits only acknowledged revisioned snapshots.</summary>
internal sealed partial class FrozenVocabularyImportStore : ICanonicalVocabularyStore
{
    private FrozenVocabularySQLiteDatabase? _database;
    private readonly Func<FrozenVocabularySQLiteDatabase> _open;
    // Accessed only while the serialized worker owns _gate; a failed open is not cached.
    private FrozenVocabularySQLiteDatabase Database => _database ??= _open();
    private readonly Func<SourceDate> _now;
    private readonly bool _installClassification;
    private readonly Func<VocabularyClassificationCatalog> _loadCatalog;
    private VocabularyClassificationCatalog? _catalog;
    private readonly IVocabularyImageStorage? _images;
    private readonly string? _notebookPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _listenersGate = new();
    private readonly Dictionary<long, Action<FrozenVocabularyImportSnapshot>> _listeners = [];
    private readonly Channel<FrozenVocabularyImportSnapshot> _notifications = Channel.CreateBounded<FrozenVocabularyImportSnapshot>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _notificationWorker;
    private long _nextListener;
    private bool _closing;
    private readonly HashSet<Task> _work = [];
    private Task? _disposal;
    internal FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase database, Func<SourceDate>? now = null)
        : this(() => database, now) { _database = database; }
    internal FrozenVocabularyImportStore(Func<FrozenVocabularySQLiteDatabase> open, Func<SourceDate>? now = null,
        bool installBundledClassification = false, Func<VocabularyClassificationCatalog>? loadCatalog = null, IVocabularyImageStorage? images = null, string? notebookPath = null)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open)); _now = now ?? (() => SourceDate.FromWindows(DateTimeOffset.UtcNow));
        _installClassification = installBundledClassification;
        _loadCatalog = loadCatalog ?? VocabularyClassificationCatalog.CurrentBundled;
        _images = images; _notebookPath = notebookPath;
        _notificationWorker = Task.Run(DispatchAsync);
    }
    private async Task DispatchAsync()
    {
        await foreach (var snapshot in _notifications.Reader.ReadAllAsync())
        {
            Action<FrozenVocabularyImportSnapshot>[] listeners;
            lock (_listenersGate) listeners = _listeners.Values.ToArray();
            foreach (var listener in listeners) { try { listener(snapshot); } catch { /* Observer failures cannot undo a committed write. */ } }
        }
    }
    public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        lock (_listenersGate)
        {
            if (_closing) throw new ObjectDisposedException(nameof(FrozenVocabularyImportStore));
            long id = ++_nextListener; _listeners.Add(id, changed); return new Subscription(this, id);
        }
    }
    private sealed class Subscription(FrozenVocabularyImportStore owner, long id) : IDisposable
    {
        private FrozenVocabularyImportStore? _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null) lock (owner._listenersGate) owner._listeners.Remove(id);
        }
    }
    private Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken)
    {
        lock (_listenersGate)
        {
            if (_closing) return Task.FromException<T>(new ObjectDisposedException(nameof(FrozenVocabularyImportStore)));
            var task = RunAccepted(work, cancellationToken); _work.Add(task);
            _ = task.ContinueWith(done => { lock (_listenersGate) _work.Remove(done); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
    private async Task<T> RunAccepted<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); return work(); }, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    // Installation and revision publication share the same exclusive database span.
    // Cache only after installation admission succeeds, so an invalid catalog can be reloaded.
    private FrozenVocabularySnapshot LoadCurrent(CancellationToken cancellationToken)
    {
        if (!_installClassification) return Database.Read(cancellationToken);
        return Database.Access(true, () =>
        {
            var before = Database.Read(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var catalog = _catalog ?? _loadCatalog() ?? throw new InvalidOperationException("Catalog loader returned null.");
            var (document, installed) = VocabularyClassificationInstaller.Install(before.Document, catalog);
            _catalog = catalog;
            if (!installed) return before;
            cancellationToken.ThrowIfCancellationRequested();
            var committed = CanonicalWritingPolicy.PrepareCommit(before.Document, document);
            var revision = Database.Write(committed, before.Document, before.Revision, cancellationToken: cancellationToken);
            _notifications.Writer.TryWrite(new(revision, committed));
            return new FrozenVocabularySnapshot(revision, committed);
        });
    }
    public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken cancellationToken = default) => Run(() =>
    {
        var snapshot = LoadCurrent(cancellationToken); return new FrozenVocabularyImportSnapshot(snapshot.Revision, snapshot.Document);
    }, cancellationToken);
    public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration configuration, CancellationToken cancellationToken = default) => Run(() =>
    {
        var inputs = VocabularyWordListPolicy.Inputs(table, configuration, cancellationToken);
        var snapshot = LoadCurrent(cancellationToken);
        var plan = VocabularyEntryImportPolicy.Plan(inputs, snapshot.Document, _now(), cancellationToken);
        return new FrozenVocabularyImportPreview(inputs, plan.Result, snapshot.Revision);
    }, cancellationToken);
    public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken cancellationToken = default)
    {
        // Freeze caller-owned list before queuing; each input owns detached tags.
        var retained = inputs.ToArray();
        return Run(() => Database.Access(true, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = LoadCurrent(cancellationToken);
            var plan = VocabularyEntryImportPolicy.Plan(retained, before.Document, _now(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var committed = plan.Result.HasChanges ? CanonicalWritingPolicy.PrepareCommit(before.Document, plan.Document) : before.Document;
            ulong revision = plan.Result.HasChanges ? Database.Write(committed, before.Document, before.Revision, cancellationToken: cancellationToken) : before.Revision;
            // Enqueue only after commit. A successful commit must not be reported as cancelled.
            if (plan.Result.HasChanges) _notifications.Writer.TryWrite(new(revision, committed));
            return plan.Result;
        }), cancellationToken);
    }
    public Task<CanonicalWritingReceipt> SaveCaptureAsync(VocabularyCapture capture, CancellationToken cancellationToken = default)
    {
        var adapted = CanonicalWritingPolicy.FromLive(capture);
        return Run(() => Database.Access(true, () =>
        {
            cancellationToken.ThrowIfCancellationRequested(); var before = LoadCurrent(cancellationToken);
            var draft = CanonicalWritingPolicy.Save(adapted, before.Document, _now());
            cancellationToken.ThrowIfCancellationRequested();
            var committed = CanonicalWritingPolicy.PrepareCommit(before.Document, draft.Document);
            ulong revision = Database.Write(committed, before.Document, before.Revision, cancellationToken: cancellationToken);
            _notifications.Writer.TryWrite(new(revision, committed));
            return new CanonicalWritingReceipt(revision, true, draft.Destination);
        }), cancellationToken);
    }
    public Task<CanonicalWritingReceipt> RemoveCaptureAsync(VocabularyCapture capture, CancellationToken cancellationToken = default)
    {
        var adapted = CanonicalWritingPolicy.FromLive(capture);
        return Mutate(document => (CanonicalWritingPolicy.Remove(adapted, document), (VocabularyExplanationDestination?)null), cancellationToken);
    }
    public Task<LiveRecord?> FindExplanationAsync(LearningExplanationInput input, LearningLanguage language, CancellationToken cancellationToken = default) => Run(() =>
    {
        var snapshot = LoadCurrent(cancellationToken);
        return CanonicalWritingPolicy.FindExplanation(input, language, snapshot.Document);
    }, cancellationToken);
    public Task<CanonicalWritingReceipt> SaveExplanationAsync(LiveRecord record, CancellationToken cancellationToken = default)
    {
        if (!LearningExplanationPolicy.IsValid(record.Input) || !LearningExplanationPolicy.IsValid(record.Result))
            return Mutate(_ => (null, null), cancellationToken);
        var adapted = CanonicalWritingPolicy.FromLive(record);
        return Mutate(document => (CanonicalWritingPolicy.SaveExplanation(adapted, document), null), cancellationToken);
    }
    public Task<CanonicalWritingReceipt> AdoptExplanationAsync(LiveRecord record, VocabularyExplanationDestination destination, CancellationToken cancellationToken = default)
    {
        if (!LearningExplanationPolicy.IsValid(record.Input) || !LearningExplanationPolicy.IsValid(record.Result))
            return Mutate(_ => (null, null), cancellationToken);
        var adapted = CanonicalWritingPolicy.FromLive(record);
        return Mutate(document => (CanonicalWritingPolicy.Adopt(adapted, destination, document), destination), cancellationToken);
    }
    private Task<CanonicalWritingReceipt> Mutate(Func<Cotabby.Core.Vocabulary.Frozen.VocabularyDocument, (Cotabby.Core.Vocabulary.Frozen.VocabularyDocument? Document, VocabularyExplanationDestination? Destination)> mutation,
        CancellationToken cancellationToken) => Run(() => Database.Access(true, () =>
    {
        cancellationToken.ThrowIfCancellationRequested(); var before = LoadCurrent(cancellationToken);
        var next = mutation(before.Document); cancellationToken.ThrowIfCancellationRequested();
        if (next.Document is null) return new CanonicalWritingReceipt(before.Revision, false);
        var committed = CanonicalWritingPolicy.PrepareCommit(before.Document, next.Document);
        ulong revision = Database.Write(committed, before.Document, before.Revision, cancellationToken: cancellationToken);
        _notifications.Writer.TryWrite(new(revision, committed));
        return new CanonicalWritingReceipt(revision, true, next.Destination);
    }), cancellationToken);
    public ValueTask DisposeAsync()
    {
        lock (_listenersGate)
        {
            _closing = true;
            return new(_disposal ??= Drain(_work.ToArray()));
        }
    }
    private async Task Drain(Task[] accepted)
    {
        try { await Task.WhenAll(accepted).ConfigureAwait(false); } catch { /* Individual callers observe their own errors. */ }
        _database?.Dispose(); _notifications.Writer.TryComplete();
        await _notificationWorker.ConfigureAwait(false);
        lock (_listenersGate) _listeners.Clear();
    }
}
