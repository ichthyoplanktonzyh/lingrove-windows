using System.IO;

namespace Cotabby.Win32.Dictionary;

/// <summary>Operation-local admission for a serialized MDict owner. Read leases deny ordinary
/// write/delete while parsing; retained parser streams remain shareable between operations.
/// Stamps come from opened objects, and folder resources borrow the same acquired MDX root.</summary>
internal sealed class MDictFileLease : IDisposable
{
    private readonly List<FileStream> _leases = [];
    private readonly List<WindowsDictionaryDirectory> _owned = [];
    private readonly List<(WindowsDictionaryDirectory Root, string Name)> _sources = [];
    internal WindowsDictionaryDirectory Folder { get; }
    internal IReadOnlyList<WindowsDictionaryDirectory.FileStamp> Stamps { get; }
    internal MDictFileLease(IReadOnlyList<string> paths, string folder, CancellationToken token, WindowsDictionaryDirectory? borrowed = null)
    {
        if (paths.Count is < 1 or > 17) throw WindowsDictionaryDirectory.Invalid();
        string normalizedFolder = WindowsDictionaryDirectory.Normalize(folder);
        string main = WindowsDictionaryDirectory.Normalize(paths[0]);
        if (!StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetDirectoryName(main), normalizedFolder)) throw WindowsDictionaryDirectory.Invalid();
        var roots = new Dictionary<string, WindowsDictionaryDirectory>(StringComparer.OrdinalIgnoreCase);
        var stamps = new List<WindowsDictionaryDirectory.FileStamp>(); long total = 0;
        try
        {
            Folder = borrowed ?? WindowsDictionaryDirectory.Open(normalizedFolder);
            if (borrowed is null) _owned.Add(Folder);
            if (!StringComparer.OrdinalIgnoreCase.Equals(normalizedFolder, Folder.Path)) throw WindowsDictionaryDirectory.Invalid();
            roots.Add(normalizedFolder, Folder);
            for (int index = 0; index < paths.Count; index++)
            {
                token.ThrowIfCancellationRequested(); string full = WindowsDictionaryDirectory.Normalize(paths[index]);
                string parent = System.IO.Path.GetDirectoryName(full) ?? throw WindowsDictionaryDirectory.Invalid();
                if (index == 0 && !StringComparer.OrdinalIgnoreCase.Equals(parent, normalizedFolder)) throw WindowsDictionaryDirectory.Invalid();
                if (!roots.TryGetValue(parent, out var root))
                { root = WindowsDictionaryDirectory.Open(parent); _owned.Add(root); roots.Add(parent, root); }
                string name = System.IO.Path.GetFileName(full);
                var stream = root.OpenFile(name) ?? throw WindowsDictionaryDirectory.Invalid(); _leases.Add(stream); _sources.Add((root, name));
                var stamp = WindowsDictionaryDirectory.Stamp(stream);
                if (stamp.Size <= 0 || stamp.Size > 8L * 1024 * 1024 * 1024 || stamp.Size > 16L * 1024 * 1024 * 1024 - total) throw WindowsDictionaryDirectory.Invalid();
                total += stamp.Size; stamps.Add(stamp);
            }
            Stamps = stamps.AsReadOnly();
        }
        catch { Dispose(); throw; }
    }
    internal List<FileStream> OpenCacheStreams(CancellationToken token)
    {
        var streams = new List<FileStream>();
        try
        {
            for (int index = 0; index < _sources.Count; index++)
            {
                token.ThrowIfCancellationRequested(); var source = _sources[index];
                var stream = source.Root.OpenFile(source.Name, cache: true) ?? throw WindowsDictionaryDirectory.Invalid(); streams.Add(stream);
                if (WindowsDictionaryDirectory.Stamp(stream) != Stamps[index]) throw WindowsDictionaryDirectory.Invalid();
            }
            return streams;
        }
        catch { foreach (var stream in streams) stream.Dispose(); throw; }
    }
    internal void Verify(CancellationToken token)
    {
        for (int index = 0; index < _leases.Count; index++)
        { token.ThrowIfCancellationRequested(); if (WindowsDictionaryDirectory.Stamp(_leases[index]) != Stamps[index]) throw WindowsDictionaryDirectory.Invalid(); }
    }
    internal bool HasStylesheet(string name)
    {
        try { using var stream = Folder.OpenFile(name); return stream is not null && stream.Length <= 8_000_000; }
        catch (IOException) { return false; }
    }
    internal byte[]? ReadResource(string relative, CancellationToken token)
    {
        try
        {
            using var stream = Folder.OpenFile(relative); if (stream is null || stream.Length > 8_000_000) return null;
            var before = WindowsDictionaryDirectory.Stamp(stream); using var output = new MemoryStream(); byte[] buffer = new byte[65536];
            while (true)
            {
                token.ThrowIfCancellationRequested(); int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, 8_000_001 - output.Length));
                if (count == 0) break; if (output.Length + count > 8_000_000) return null; output.Write(buffer, 0, count);
            }
            if (WindowsDictionaryDirectory.Stamp(stream) != before) return null;
            token.ThrowIfCancellationRequested(); return output.ToArray();
        }
        catch (IOException) { return null; }
    }
    public void Dispose()
    { foreach (var stream in _leases) stream.Dispose(); _leases.Clear(); foreach (var root in _owned.AsEnumerable().Reverse()) root.Dispose(); _owned.Clear(); }
}
