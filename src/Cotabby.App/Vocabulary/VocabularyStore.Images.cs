using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

internal sealed partial class VocabularyStore
{
    // Mac changes this only after restore, not on every ordinary notebook snapshot. Keeping
    // the token separate prevents unrelated saves from re-reading visible same-hash images.
    private readonly Guid _mediaRevision = Guid.NewGuid();
    internal Guid MediaRevision => _mediaRevision;
    internal Task<VocabularyPreparedImage> PrepareImageAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        MediaOperation(() => archive.PrepareImageAsync(data, cancellationToken));
    internal Task<VocabularyPreparedImage> PrepareImageFileAsync(string path, CancellationToken cancellationToken = default) =>
        MediaOperation(() => archive.PrepareImageFileAsync(path, cancellationToken));
    internal Task<byte[]> ReadImageAsync(VocabularyImageReference reference) => MediaOperation(() => archive.ReadImageAsync(reference));

    /// <summary>
    /// The archive already schedules IO off WPF. Admit its task synchronously so byte detachment
    /// happens before a caller can change its buffer, then track it for Store shutdown. Views may
    /// cancel their wait without abandoning this owned operation; read/import errors remain local
    /// to that view and cannot replace the shared notebook's acknowledged error/snapshot state.
    /// </summary>
    private Task<T> MediaOperation<T>(Func<Task<T>> operation)
    {
        lock (_gate)
        {
            if (_disposal is not null) return Task.FromException<T>(new ObjectDisposedException(nameof(VocabularyStore)));
            Task<T> task;
            try { task = operation(); } catch (Exception error) { return Task.FromException<T>(error); }
            _work.Add(task);
            _ = task.ContinueWith(completed => { lock (_gate) _work.Remove(completed); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
}
