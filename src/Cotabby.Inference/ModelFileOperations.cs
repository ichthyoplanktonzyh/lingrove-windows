namespace Cotabby.Inference;

/// <summary>Serializes app-owned transfer and mutation of one shared model destination.
/// Runtime ownership stays independent; this gate only coordinates filesystem publication.</summary>
public static class ModelFileOperations
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<IDisposable> AcquireAsync(string destination, CancellationToken cancellationToken)
    {
        string key = Path.GetFullPath(destination);
        Entry entry;
        lock (Sync)
        {
            if (!Gates.TryGetValue(key, out entry!)) Gates[key] = entry = new Entry();
            entry.Users++;
        }
        try { await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { Release(key, entry, false); throw; }
        return new Lease(key, entry);
    }

    private static void Release(string key, Entry entry, bool held)
    {
        lock (Sync)
        {
            if (held) entry.Gate.Release();
            if (--entry.Users == 0) { Gates.Remove(key); entry.Gate.Dispose(); }
        }
    }
    private sealed class Entry { public readonly SemaphoreSlim Gate = new(1, 1); public int Users; }
    private sealed class Lease(string key, Entry entry) : IDisposable
    {
        private Entry? _entry = entry;
        public void Dispose() { if (Interlocked.Exchange(ref _entry, null) is { } held) Release(key, held, true); }
    }
}
