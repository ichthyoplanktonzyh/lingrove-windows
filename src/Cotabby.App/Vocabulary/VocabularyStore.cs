using System.Collections.Frozen;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>
/// AppHost owns one UI-facing notebook for the process lifetime. Views borrow immutable snapshots
/// and exact pending keys; the archive owns disk transactions. A revision gate rejects delayed
/// replies. State is protected for worker-side saved lookup; notifications return to the UI context.
/// Closing drains store operations before disposing its archive. No model is started by saving.
/// </summary>
internal sealed partial class VocabularyStore(VocabularyArchive archive, SynchronizationContext ui) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private readonly Dictionary<string, int> _pendingCounts = [];
    private HashSet<string> _savedKeys = [], _standaloneKeys = [];
    private VocabularySnapshot _snapshot = new(0, new());
    private bool _isLoaded;
    private string? _errorMessage;
    private Task? _loadWork, _disposal;

    internal event Action? Changed;
    internal Action<VocabularySelection?>? OpenSelection { get; set; }
    /// <summary>Composition may attach only an already cached/saved record; this callback never generates.</summary>
    internal Func<VocabularyCapture, VocabularyCapture>? EnrichCapture { get; set; }
    internal VocabularySnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    /// <summary>One UI request must capture loaded/error and graph together, like Mac MainActor access.</summary>
    internal (VocabularySnapshot Snapshot, bool IsLoaded, string? ErrorMessage) ReadState()
    { lock (_gate) return (_snapshot, _isLoaded, _errorMessage); }
    internal IReadOnlyList<VocabularyEntry> Entries => Snapshot.Document.Entries;
    internal IReadOnlyList<ReviewLayoutTemplate> ReviewTemplates => Snapshot.Document.ReviewTemplates ?? [];
    internal bool IsLoaded { get { lock (_gate) return _isLoaded; } }
    internal string? ErrorMessage { get { lock (_gate) return _errorMessage; } }
    internal IReadOnlySet<string> Pending { get { lock (_gate) return _pendingCounts.Keys.ToFrozenSet(); } }
    internal bool IsPending(VocabularyCapture capture) { lock (_gate) return _pendingCounts.ContainsKey(VocabularyCapturePolicy.CaptureKey(capture)); }
    internal bool Contains(VocabularyCapture capture)
    {
        lock (_gate)
        {
            if (capture.SenseId is not null) return VocabularyPolicy.Contains(capture, _snapshot.Document.Entries);
            string key = VocabularyCapturePolicy.CaptureKey(capture);
            return (capture.Example is null ? _standaloneKeys : _savedKeys).Contains(key);
        }
    }
    internal Task LoadAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return Task.FromException(new ObjectDisposedException(nameof(VocabularyStore)));
            if (_isLoaded) return Task.CompletedTask;
            return _loadWork ??= StartLocked(async () =>
            {
                try { Apply(await archive.LoadAsync().ConfigureAwait(false)); }
                catch (Exception error) { Report(error); }
                finally { lock (_gate) _loadWork = null; }
                return true;
            });
        }
    }
    internal Task<bool> SaveAsync(VocabularyCapture capture)
    {
        lock (_gate) if (_disposal is not null) return Task.FromException<bool>(new ObjectDisposedException(nameof(VocabularyStore)));
        VocabularyCapture enriched;
        // Like Mac, capture the optional annotation before admitting the disk operation. A
        // queued save must never depend on an explanation service that may close while it waits.
        try { enriched = EnrichCapture?.Invoke(capture) ?? capture; }
        catch (Exception error) { Report(error); return Task.FromResult(false); }
        return Change(capture, () => archive.SaveAsync(enriched));
    }
    internal Task<bool> RemoveAsync(VocabularyCapture capture) => Change(capture, () => archive.RemoveAsync(capture));
    internal Task<bool> UpdateAsync(VocabularyEntry edited, VocabularyEntry baseline, IReadOnlyDictionary<string, VocabularyPreparedImage>? importedImages = null)
    {
        // The editor can close or prune its map as soon as Save is admitted. Prepared values
        // own immutable bytes; detaching the map preserves the exact admitted draft until SQL.
        var supplied = importedImages is null ? null : new Dictionary<string, VocabularyPreparedImage>(importedImages, StringComparer.Ordinal);
        return Mutation(() => archive.UpdateAsync(edited, baseline, supplied));
    }
    internal Task<bool> RemoveEntryAsync(Guid id) => Mutation(() => archive.RemoveEntryAsync(id));
    internal Task<bool> RemoveSenseAsync(Guid entryId, Guid senseId) => Mutation(() => archive.RemoveSenseAsync(entryId, senseId));
    internal Task<bool> MergeSensesAsync(Guid entryId, Guid source, Guid target) => Mutation(() => archive.MergeSensesAsync(entryId, source, target));
    internal Task<bool> MoveExampleAsync(Guid entryId, Guid exampleId, Guid source, Guid? target) => Mutation(() => archive.MoveExampleAsync(entryId, exampleId, source, target));
    internal async Task<Guid?> CreateSenseAsync(Guid entryId)
    {
        var id = Guid.NewGuid(); return await Mutation(() => archive.CreateSenseAsync(entryId, id)).ConfigureAwait(false) ? id : null;
    }
    internal Task<bool> SaveExplanationAsync(LearningExplanationRecord record) => Mutation(() => archive.SaveExplanationAsync(record));
    internal LearningExplanationRecord? Explanation(LearningExplanationInput input, LearningLanguage language)
    { lock (_gate) return _snapshot.Document.Entries.SelectMany(entry => entry.Explanations ?? []).LastOrDefault(record => record.Input == input && record.Language == language); }
    internal VocabularyExplanationDestination? ExplanationDestination(VocabularyCapture capture)
    {
        lock (_gate)
        {
            var entry = _snapshot.Document.Entries.FirstOrDefault(entry => VocabularyCapturePolicy.EntryKey(entry) == VocabularyCapturePolicy.EntryKey(capture));
            return entry is null ? null : VocabularySensePolicy.Destination(capture, entry);
        }
    }
    internal void Show(VocabularyCapture capture)
    {
        var destination = ExplanationDestination(capture);
        OpenSelection?.Invoke(destination is { } value ? new(value.EntryId, value.SenseId) : null);
    }
    internal string? UserMeaning(VocabularyExplanationDestination destination)
    {
        lock (_gate)
        {
            var entry = _snapshot.Document.Entries.FirstOrDefault(entry => entry.Id == destination.EntryId); if (entry is null) return null;
            if (destination.ExampleId is { } id) return entry.Examples.FirstOrDefault(usage => usage.Id == id && usage.MeaningOrigin == VocabularyMeaningOrigin.User)?.Meaning;
            if (VocabularySensePolicy.Index(destination.SenseId, entry) is not { } owner) return null;
            var sense = entry.Senses[owner]; return sense.MeaningOrigin == VocabularyMeaningOrigin.User ? sense.Meaning : null;
        }
    }
    internal Task<bool> AdoptExplanationAsync(LearningExplanationRecord record, VocabularyExplanationDestination destination)
    {
        VocabularyEntry? original;
        lock (_gate) original = _snapshot.Document.Entries.FirstOrDefault(entry => entry.Id == destination.EntryId);
        if (original is null) return Task.FromResult(false);
        var senses = original.Senses.ToArray(); int owner;
        if (destination.ExampleId is { } id)
        {
            // A usage may have moved while generation ran. Its stable identity resolves the live
            // owner; a late result cannot recreate a deleted occurrence or retired sense.
            owner = Array.FindIndex(senses, sense => sense.Examples.Any(usage => usage.Id == id));
            if (owner < 0) return Task.FromResult(false);
            var usages = senses[owner].Examples.ToArray(); int index = Array.FindIndex(usages, usage => usage.Id == id);
            if (LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(original, usages[index], senses[owner])) != record.Input) return Task.FromResult(false);
            usages[index] = usages[index] with { Meaning = record.Result.Meaning, MeaningLanguage = record.Language, MeaningOrigin = VocabularyMeaningOrigin.User };
            senses[owner] = senses[owner] with { Examples = usages };
        }
        else
        {
            if (VocabularySensePolicy.Index(destination.SenseId, original) is not { } index) return Task.FromResult(false);
            owner = index;
            if (!senses[owner].IsSavedWithoutContext || LearningExplanationPolicy.Input(LearningExplanationPolicy.Capture(original, null, senses[owner])) != record.Input) return Task.FromResult(false);
            senses[owner] = senses[owner] with { Meaning = record.Result.Meaning, MeaningLanguage = record.Language, MeaningOrigin = VocabularyMeaningOrigin.User };
        }
        return UpdateAsync(original with { Senses = senses }, original);
    }
    /// <summary>Original change boundary; internal access also permits scheduling probes over real archive operations.</summary>
    internal Task<bool> Change(VocabularyCapture capture, Func<Task<VocabularySnapshot>> operation)
    {
        string key = VocabularyCapturePolicy.CaptureKey(capture); Task<bool> task;
        lock (_gate)
        {
            if (_disposal is not null) return Task.FromException<bool>(new ObjectDisposedException(nameof(VocabularyStore)));
            _pendingCounts[key] = _pendingCounts.GetValueOrDefault(key) + 1;
            task = StartLocked(async () =>
            {
                try { return await Perform(operation).ConfigureAwait(false); }
                finally
                {
                    lock (_gate)
                    {
                        int remaining = _pendingCounts[key] - 1;
                        if (remaining == 0) _pendingCounts.Remove(key); else _pendingCounts[key] = remaining;
                    }
                    Notify();
                }
            });
        }
        Notify(); return task;
    }
    private Task<bool> Mutation(Func<Task<VocabularySnapshot>> operation)
    {
        lock (_gate) return _disposal is null ? StartLocked(() => Perform(operation))
            : Task.FromException<bool>(new ObjectDisposedException(nameof(VocabularyStore)));
    }
    private async Task<bool> Perform(Func<Task<VocabularySnapshot>> operation)
    {
        try { Apply(await operation().ConfigureAwait(false)); return true; }
        catch (Exception error) { Report(error); return false; }
    }
    /// <summary>Only acknowledged snapshots enter here; revision ordering is independent of callback scheduling.</summary>
    internal void Apply(VocabularySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_isLoaded && snapshot.Revision < _snapshot.Revision) return;
            _snapshot = snapshot;
            _standaloneKeys = snapshot.Document.Entries.SelectMany(entry => entry.Senses.Where(sense => sense.IsSavedWithoutContext)
                .Select(_ => VocabularyCapturePolicy.EntryKey(entry) + ":standalone")).ToHashSet();
            _savedKeys = snapshot.Document.Entries.SelectMany(entry => entry.Examples.Select(example =>
                VocabularyCapturePolicy.EntryKey(entry) + ":" + VocabularyCapturePolicy.ExampleKey(example))).ToHashSet();
            _isLoaded = true; _errorMessage = null;
        }
        Notify();
    }
    private void Report(Exception error)
    { lock (_gate) _errorMessage = (error as VocabularyException ?? new VocabularyException(VocabularyFailure.InvalidArchive)).Message; Notify(); }
    private void Notify()
    {
        try
        {
            ui.Post(_ =>
            {
                lock (_gate) if (_disposal is not null) return;
                if (Changed is { } handlers) foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch { /* Presentation cannot undo an acknowledged commit. */ }
            }, null);
        }
        // WPF can retire its dispatcher while an already admitted write is draining. Losing a
        // presentation notification must not turn a successful durable edit into a reported failure.
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { }
    }
    // Called under the admission lock. Worker publication/finally must take the same lock, so
    // even a synchronous failure cannot clear the load task before its owner records it.
    private Task<T> StartLocked<T>(Func<Task<T>> operation)
    {
        var task = Task.Run(operation); _work.Add(task);
        _ = task.ContinueWith(completed => { lock (_gate) _work.Remove(completed); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= Drain(_work.ToArray()));
        async Task Drain(Task[] admitted)
        {
            try { await Task.WhenAll(admitted).ConfigureAwait(false); } catch { }
            await archive.DisposeAsync().ConfigureAwait(false);
        }
    }
}
