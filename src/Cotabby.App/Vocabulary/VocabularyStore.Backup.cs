namespace Cotabby.App.Vocabulary;

/// <summary>
/// AppHost's shared Store owns backup/restore admission and shutdown tracking; the archive owns
/// the actual file/media/SQL transaction. Like Mac, backup preserves the current UI snapshot and
/// even an existing shared error on success. Restore applies its acknowledged graph, then revokes
/// same-hash image caches with a fresh token, including when a newer graph reply already won.
/// </summary>
internal sealed partial class VocabularyStore
{
    internal Task<bool> BackupAsync(string destination) => BackupOperation(async () =>
    {
        await archive.BackupAsync(destination).ConfigureAwait(false);
    });
    internal Task<bool> RestoreAsync(string source) => BackupOperation(async () =>
    {
        var snapshot = await archive.RestoreAsync(source).ConfigureAwait(false);
        Apply(snapshot);
        lock (_gate) _mediaRevision = Guid.NewGuid();
        Notify();
    });
    private Task<bool> BackupOperation(Func<Task> operation)
    {
        lock (_gate) return _disposal is null ? StartLocked(async () =>
        {
            try { await operation().ConfigureAwait(false); return true; }
            catch (Exception error) { Report(error); return false; }
        }) : Task.FromException<bool>(new ObjectDisposedException(nameof(VocabularyStore)));
    }
}
