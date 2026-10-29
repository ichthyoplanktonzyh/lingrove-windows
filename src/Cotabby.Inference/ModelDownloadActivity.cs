namespace Cotabby.Inference;

/// <summary>Read-only observation of admitted file transfers; no model or network ownership.</summary>
public sealed class ModelDownloadActivity
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _active = new(StringComparer.OrdinalIgnoreCase);
    public event Action? Changed;
    public bool IsDownloading(string filename) { lock (_gate) return _active.ContainsKey(filename); }
    public IDisposable Begin(string filename)
    {
        lock (_gate) _active[filename] = _active.GetValueOrDefault(filename) + 1;
        Notify(); return new Lease(this, filename);
    }
    private void End(string filename)
    {
        lock (_gate) { if (_active[filename] == 1) _active.Remove(filename); else _active[filename]--; }
        Notify();
    }
    private void Notify() { foreach (Action listener in Changed?.GetInvocationList() ?? []) try { listener(); } catch { } }
    private sealed class Lease(ModelDownloadActivity owner, string filename) : IDisposable
    {
        private ModelDownloadActivity? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End(filename);
    }
}
