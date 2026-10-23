using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// App-lifetime owner of one local notebook, adapted from the Mac archive actor. A serialized
/// worker runs synchronous SQLite operations away from WPF; immutable cache/revision state advances
/// only after COMMIT succeeds. Captures are already detached from the editor. Construction is inert,
/// null path means an isolated test notebook, and no observer/model/network is started here.
/// </summary>
public sealed class VocabularyArchive : IAsyncDisposable
{
    private readonly object _lifetime = new();
    private readonly SemaphoreSlim _serial = new(1);
    private readonly HashSet<Task> _work = [];
    private readonly Func<VocabularySQLiteDatabase> _open;
    private readonly TimeProvider _clock;
    private VocabularySQLiteDatabase? _database;
    private VocabularyDocument? _document;
    private ulong _revision;
    private Task? _disposal;

    public VocabularyArchive(string? path) : this(() => path is null ? VocabularySQLiteDatabase.CreateMemory() : VocabularySQLiteDatabase.OpenFile(path), TimeProvider.System) { }
    /// <summary>Tests may supply an actual isolated SQLite owner and clock; ownership transfers to this archive.</summary>
    internal VocabularyArchive(Func<VocabularySQLiteDatabase> open, TimeProvider? clock = null)
    { _open = open; _clock = clock ?? TimeProvider.System; }

    public Task<VocabularySnapshot> LoadAsync() => Run(Load);
    public Task<VocabularySnapshot> SaveAsync(VocabularyCapture capture) => Run(() =>
        Commit(VocabularyPolicy.Save(capture, Load().Document, _clock.GetUtcNow()).Document));
    public Task<VocabularySnapshot> RemoveAsync(VocabularyCapture capture) => Run(() =>
    {
        var current = Load();
        // Mac's missing exact favorite is a read-only no-op, unlike explicit entry deletion.
        if (!VocabularyPolicy.Contains(capture, current.Document.Entries)) return current;
        return Commit(VocabularyArchiveDraftPolicy.Remove(capture, current.Document));
    });
    public Task<VocabularySnapshot> RemoveEntryAsync(Guid id) => Run(() => Commit(VocabularyArchiveDraftPolicy.RemoveEntry(id, Load().Document)));
    public Task<VocabularySnapshot> CreateSenseAsync(Guid entryId, Guid senseId) => Run(() =>
        Commit(VocabularyArchiveDraftPolicy.CreateSense(entryId, senseId, Load().Document, _clock.GetUtcNow())));
    public Task<VocabularySnapshot> RemoveSenseAsync(Guid entryId, Guid senseId) => Run(() =>
        Commit(VocabularyArchiveDraftPolicy.RemoveSense(entryId, senseId, Load().Document)));
    public Task<VocabularySnapshot> MergeSensesAsync(Guid entryId, Guid source, Guid target) => Run(() =>
        Commit(ChangeEntry(Load().Document, entryId, entry => VocabularySensePolicy.Merge(source, target, entry, _clock.GetUtcNow()))));
    public Task<VocabularySnapshot> MoveExampleAsync(Guid entryId, Guid exampleId, Guid source, Guid? target) => Run(() =>
        Commit(ChangeEntry(Load().Document, entryId, entry => VocabularySensePolicy.Move(exampleId, source, target, entry, _clock.GetUtcNow()).Entry)));
    public Task<VocabularySnapshot> UpdateAsync(VocabularyEntry edited, VocabularyEntry baseline) => Run(() =>
    {
        var current = Load().Document;
        var next = VocabularyArchiveDraftPolicy.Update(edited, baseline, current, _clock.GetUtcNow());
        // Imported image staging is a separate pending adapter. Existing references survive;
        // a text-only editor cannot acknowledge a new image whose bytes we have never verified.
        var existing = Images(current).ToHashSet();
        if (Images(next).Any(image => !existing.Contains(image))) throw new VocabularyException(VocabularyFailure.InvalidContent);
        return Commit(next);
    });
    public Task<VocabularySnapshot> SaveExplanationAsync(LearningExplanationRecord record) => Run(() =>
    {
        if (record is null || record.Input is null || record.Result is null) throw new VocabularyException(VocabularyFailure.InvalidContent);
        var current = Load();
        var entry = current.Document.Entries.FirstOrDefault(entry => VocabularyPolicy.Accepts(record, entry));
        if (entry is null) return current;
        return Commit(ChangeEntry(current.Document, entry.Id, value => VocabularyPolicy.StoreExplanation(record, value)));
    });
    /// <summary>Internal restore/fixture boundary; it still prunes, validates, compares revision and commits.</summary>
    internal Task<VocabularySnapshot> CommitAsync(VocabularyDocument document) => Run(() => Commit(document));

    private VocabularySQLiteDatabase Persistence() => _database ??= _open();
    private VocabularySnapshot Load()
    {
        var database = Persistence();
        if (_document is { } cached && database.Revision() == _revision) return new(_revision, cached);
        var snapshot = database.Read(); VocabularyArchiveValidation.Validate(snapshot.Document);
        _document = snapshot.Document; _revision = snapshot.Revision; return snapshot;
    }
    private VocabularySnapshot Commit(VocabularyDocument document) => Persistence().Access(true, () =>
    {
        // The candidate was built from this revision. Refreshing here after another writer won
        // would disguise its commit as our baseline and silently overwrite its changes.
        var baseline = _document is { } cached ? new VocabularySnapshot(_revision, cached) : Load();
        var next = VocabularyArchiveDraftPolicy.PruneExplanations(document); VocabularyArchiveValidation.Validate(next);
        ulong revision = Persistence().Write(next, baseline.Document, baseline.Revision);
        _document = next; _revision = revision;
        return new VocabularySnapshot(revision, next);
    });
    private static VocabularyDocument ChangeEntry(VocabularyDocument document, Guid id, Func<VocabularyEntry, VocabularyEntry> change)
    {
        var entries = document.Entries.ToArray(); int index = Array.FindIndex(entries, entry => entry.Id == id);
        if (index < 0) throw new VocabularyException(VocabularyFailure.InvalidContent);
        entries[index] = change(entries[index]); return document with { Entries = entries };
    }
    private static IEnumerable<VocabularyImageReference> Images(VocabularyDocument document) => document.Entries.SelectMany(entry => entry.Senses)
        .SelectMany(sense => VocabularyContentPolicy.Images(sense.Content ?? []).Concat(sense.ReviewLayouts.SelectMany(layout => VocabularyContentPolicy.Images(layout.CustomContent))))
        .Concat((document.ReviewTemplates ?? []).SelectMany(template => VocabularyContentPolicy.Images(template.Layout.CustomContent)));

    private Task<T> Run<T>(Func<T> operation)
    {
        lock (_lifetime)
        {
            if (_disposal is not null) return Task.FromException<T>(new VocabularyException(VocabularyFailure.InvalidArchive));
            var task = Execute(); _work.Add(task);
            _ = task.ContinueWith(completed => { lock (_lifetime) _work.Remove(completed); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
        async Task<T> Execute()
        {
            await _serial.WaitAsync().ConfigureAwait(false);
            try { return await Task.Run(operation).ConfigureAwait(false); }
            finally { _serial.Release(); }
        }
    }
    /// <summary>
    /// Close admission, drain every already admitted operation, then close SQLite off the UI
    /// thread. View dismissal never cancels an edit halfway through its durable commit.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_lifetime) return new(_disposal ??= Drain(_work.ToArray()));
        async Task Drain(Task[] work)
        {
            try { await Task.WhenAll(work).ConfigureAwait(false); } catch { /* Failed edits also release the serial lease. */ }
            await Task.Run(() => { _database?.Dispose(); _database = null; _document = null; }).ConfigureAwait(false);
            _serial.Dispose();
        }
    }
}
