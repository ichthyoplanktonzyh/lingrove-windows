using System.Threading.Channels;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Isolated candidate for the eventual single app-owned Store. It owns its database,
/// serializes operations and emits only acknowledged revisioned snapshots. No AppHost creates it.</summary>
internal sealed class FrozenVocabularyImportStore : IVocabularyEntryImportStore, IAsyncDisposable
{
    private readonly FrozenVocabularySQLiteDatabase _database;
    private readonly Func<SourceDate> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _listenersGate = new();
    private readonly Dictionary<long, Action<FrozenVocabularyImportSnapshot>> _listeners = [];
    private readonly Channel<FrozenVocabularyImportSnapshot> _notifications = Channel.CreateBounded<FrozenVocabularyImportSnapshot>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _notificationWorker;
    private long _nextListener;
    private bool _disposed;
    internal FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase database, Func<SourceDate>? now = null)
    {
        _database = database; _now = now ?? (() => SourceDate.FromWindows(DateTimeOffset.UtcNow));
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
            if (_disposed) throw new ObjectDisposedException(nameof(FrozenVocabularyImportStore));
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
    private async Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_listenersGate) if (_disposed) throw new ObjectDisposedException(nameof(FrozenVocabularyImportStore));
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken cancellationToken = default) => Run(() =>
    {
        var snapshot = _database.Read(cancellationToken); return new FrozenVocabularyImportSnapshot(snapshot.Revision, snapshot.Document);
    }, cancellationToken);
    public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration configuration, CancellationToken cancellationToken = default) => Run(() =>
    {
        var inputs = VocabularyWordListPolicy.Inputs(table, configuration, cancellationToken);
        var snapshot = _database.Read(cancellationToken);
        var plan = VocabularyEntryImportPolicy.Plan(inputs, snapshot.Document, _now(), cancellationToken);
        return new FrozenVocabularyImportPreview(inputs, plan.Result, snapshot.Revision);
    }, cancellationToken);
    public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken cancellationToken = default)
    {
        // Freeze caller-owned list before queuing; each input owns detached tags.
        var retained = inputs.ToArray();
        return Run(() => _database.Access(true, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = _database.Read(cancellationToken);
            var plan = VocabularyEntryImportPolicy.Plan(retained, before.Document, _now(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ulong revision = plan.Result.HasChanges ? _database.Write(plan.Document, before.Document, before.Revision, cancellationToken: cancellationToken) : before.Revision;
            var committed = plan.Result.HasChanges ? plan.Document : before.Document;
            // Enqueue only after commit. A successful commit must not be reported as cancelled.
            _notifications.Writer.TryWrite(new(revision, committed));
            return plan.Result;
        }), cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            bool close;
            lock (_listenersGate) { close = !_disposed; _disposed = true; }
            if (close) { _database.Dispose(); _notifications.Writer.TryComplete(); }
        }
        finally { _gate.Release(); }
        await _notificationWorker.ConfigureAwait(false);
        lock (_listenersGate) _listeners.Clear();
    }
}

