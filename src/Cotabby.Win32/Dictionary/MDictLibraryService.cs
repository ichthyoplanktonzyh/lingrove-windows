using System.Runtime.Versioning;
using System.IO;
using System.Windows.Media.Imaging;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.MDict;
using Cotabby.Core.Text;

namespace Cotabby.Win32.Dictionary;

/// <summary>One serialized owner for explicitly selected local MDict files. Original files stay
/// in place; removing a descriptor does not delete anything. Cached readers never escape.</summary>
[SupportedOSPlatform("windows8.1")]
public sealed class MDictLibraryService(Func<string, string> lowercase) : IAsyncDisposable
{
    private readonly Func<string, string> _lowercase = lowercase ?? throw new ArgumentNullException(nameof(lowercase));
    private readonly SemaphoreSlim _gate = new(1, 1); private readonly CancellationTokenSource _stopping = new();
    private readonly List<Book> _books = []; private readonly object _lifetime = new(); private Task? _dispose; private bool _closed;
    private sealed record Book(MDictLibraryDescriptor Descriptor, WindowsDictionaryDirectory.FileIdentity FolderIdentity,
        IReadOnlyList<WindowsDictionaryDirectory.FileStamp> Stamps, FileStream MainStream,
        MDictFileReader Main, IReadOnlyList<FileStream> ResourceStreams, IReadOnlyList<MDictFileReader> Resources) : IDisposable
    {
        public void Dispose() { MainStream.Dispose(); foreach (var stream in ResourceStreams) stream.Dispose(); }
    }
    private sealed record BookAccess(Book Book, MDictFileLease Files) : IDisposable
    { public void Dispose() => Files.Dispose(); }

    public Task<MDictLibraryDescriptor> ImportAsync(string path, CancellationToken token = default) => RunAsync(ct =>
    {
        string full = WindowsDictionaryDirectory.Normalize(path); if (!string.Equals(Path.GetExtension(full), ".mdx", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        string folder = Path.GetDirectoryName(full) ?? throw Invalid(); string stem = Path.GetFileNameWithoutExtension(full);
        using var root = WindowsDictionaryDirectory.Open(folder);
        var found = new List<string>();
        foreach (var candidate in root.Entries(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (candidate.Directory || !string.Equals(Path.GetExtension(candidate.Name), ".mdd", StringComparison.OrdinalIgnoreCase)
                || !(string.Equals(Path.GetFileNameWithoutExtension(candidate.Name), stem, StringComparison.Ordinal)
                    || candidate.Name.StartsWith(stem + ".", StringComparison.Ordinal))) continue;
            found.Add(Path.Combine(folder, candidate.Name)); if (found.Count > 16) throw Invalid();
        }
        var companions = found.OrderBy(p => Path.GetFileName(p)!, WindowsDictionaryFileOrder.Instance).ToArray();
        using var lease = new MDictFileLease([full, .. companions], folder, ct, root);
        var streams = lease.OpenCacheStreams(ct);
        try
        {
            var main = new MDictFileReader(streams[0], _lowercase, ct);
            foreach (var part in streams.Skip(1)) { ct.ThrowIfCancellationRequested(); _ = new MDictFileReader(part, _lowercase, ct); }
            lease.Verify(ct); string name = DictionaryContentPolicy.ImportTitle(main.Title, stem);
            return new MDictLibraryDescriptor(Guid.NewGuid(), name, full, Array.AsReadOnly(companions), folder, main.EntryCount, Guid.NewGuid());
        }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }, token);

    public Task<MDictLibraryDescriptor> AttachAsync(MDictLibraryDescriptor library, IReadOnlyList<string> paths, CancellationToken token = default)
    {
        if (paths.Count > 16) return Task.FromException<MDictLibraryDescriptor>(Invalid());
        var selected = paths.ToArray();
        return RunAsync(library, (current, ct) =>
    {
        var full = selected.Select(WindowsDictionaryDirectory.Normalize).ToArray();
        if (full.Any(p => !string.Equals(Path.GetExtension(p), ".mdd", StringComparison.OrdinalIgnoreCase))) throw Invalid();
        using var lease = new MDictFileLease([current.MdxPath, .. full], current.FolderPath, ct); var streams = lease.OpenCacheStreams(ct);
        try { foreach (var stream in streams) { ct.ThrowIfCancellationRequested(); _ = new MDictFileReader(stream, _lowercase, ct); } lease.Verify(ct); }
        finally { foreach (var stream in streams) stream.Dispose(); }
        return current with { ResourcePaths = Array.AsReadOnly(full), Revision = Guid.NewGuid() };
    }, token);
    }

    public Task<LearningDictionaryResult> LookupAsync(string query, MDictLibraryDescriptor library, CancellationToken token = default) => RunAsync<LearningDictionaryResult>(library, (current, ct) =>
    {
        if (!DictionaryContentPolicy.ValidQuery(query)) return LearningDictionaryResult.NotFound;
        using var access = OpenBook(current, ct); var book = access.Book; int remaining = 8_000_000; var visited = new HashSet<string>(CanonicalStringComparer.Instance);
        var records = Definitions(query, 0); if (records.Count == 0) return LearningDictionaryResult.NotFound;
        string html = string.Join("<hr>", records); if (!book.Main.IsHtml) html = "<pre>" + DictionaryContentPolicy.Escaped(html) + "</pre>";
        string stylesheet = Path.GetFileNameWithoutExtension(current.MdxPath) + ".css";
        if (access.Files.HasStylesheet(stylesheet)) html = "<link rel=\"stylesheet\" href=\"" + DictionaryContentPolicy.Escaped(stylesheet) + "\">" + html;
        string text;
        try { text = DictionaryContentPolicy.PlainText(html, ct); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { throw Invalid(); }
        access.Files.Verify(ct); return new LearningDictionaryResult.Article(html, text, current.Name, current.Id, current.Languages);
        List<string> Definitions(string value, int depth)
        {
            ct.ThrowIfCancellationRequested(); if (depth >= 8 || !visited.Add(value)) return [];
            var result = new List<string>();
            foreach (string record in book.Main.Definitions(value, ct))
            {
                int bytes = Encoding.UTF8.GetByteCount(record); if (bytes > remaining) throw Invalid(); remaining -= bytes;
                if (record.StartsWith("@@@LINK=", StringComparison.Ordinal))
                {
                    string target = FoundationText.TrimWhitespaceAndNewlines(record[8..]);
                    if (DictionaryContentPolicy.ValidQuery(target)) result.AddRange(Definitions(target, depth + 1));
                }
                else result.Add(record);
            }
            return result;
        }
    }, token);

    public Task<DictionaryResource?> ResourceAsync(string path, MDictLibraryDescriptor library, CancellationToken token = default) => RunAsync<DictionaryResource?>(library, (current, ct) =>
    {
        string? relative = DictionaryContentPolicy.ResourcePath(path); string? mime = Mime(relative);
        if (relative is null || mime is null) return null;
        using var access = OpenBook(current, ct); var book = access.Book; byte[]? data = null; string key = "\\" + relative.Replace('/', '\\');
        foreach (var resource in book.Resources)
        {
            ct.ThrowIfCancellationRequested(); var values = resource.Records(key, ct); if (values.Count != 0) { data = values[0]; break; }
        }
        data ??= access.Files.ReadResource(relative, ct);
        if (data is null || data.Length > 8_000_000) return null;
        if (mime.StartsWith("image/", StringComparison.Ordinal) && !ImageWithinLimits(data)) return null;
        access.Files.Verify(ct); return new(data, mime);
    }, token);

    private static string? Mime(string? path) => Path.GetExtension(path)?.ToLowerInvariant() switch
    {
        ".css" => "text/css", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
        ".webp" => "image/webp", ".woff" => "font/woff", ".woff2" => "font/woff2", ".ttf" => "font/ttf", ".otf" => "font/otf", _ => null,
    };
    private static bool ImageWithinLimits(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0]; int width = frame.PixelWidth, height = frame.PixelHeight;
            return width > 0 && height > 0 && width <= 8192 && height <= 8192 && width <= 16_000_000 / height;
        }
        catch { return false; }
    }
    private BookAccess OpenBook(MDictLibraryDescriptor descriptor, CancellationToken token)
    {
        if (descriptor.ResourcePaths.Count > 16) throw Invalid(); var paths = new[] { descriptor.MdxPath }.Concat(descriptor.ResourcePaths).ToArray();
        MDictFileLease lease;
        try { lease = new(paths, descriptor.FolderPath, token); }
        catch { var stale = _books.FirstOrDefault(book => book.Descriptor.Id == descriptor.Id); if (stale is not null) { _books.Remove(stale); stale.Dispose(); } throw; }
        var existing = _books.FirstOrDefault(x => SameDescriptor(x.Descriptor, descriptor) && x.FolderIdentity == lease.Folder.Identity && x.Stamps.SequenceEqual(lease.Stamps));
        if (existing is not null) { _books.Remove(existing); _books.Add(existing); return new(existing, lease); }
        var previous = _books.FirstOrDefault(x => x.Descriptor.Id == descriptor.Id); if (previous is not null) { _books.Remove(previous); previous.Dispose(); }
        var streams = new List<FileStream>();
        try
        {
            streams = lease.OpenCacheStreams(token);
            var main = new MDictFileReader(streams[0], _lowercase, token);
            var resources = streams.Skip(1).Select(stream => new MDictFileReader(stream, _lowercase, token)).ToArray();
            lease.Verify(token);
            var result = new Book(descriptor, lease.Folder.Identity, lease.Stamps, streams[0], main, streams.Skip(1).ToArray(), resources); streams.Clear();
            while (_books.Count >= 2) { _books[0].Dispose(); _books.RemoveAt(0); } _books.Add(result); return new(result, lease);
        }
        catch { lease.Dispose(); throw; }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }
    private static bool SameDescriptor(MDictLibraryDescriptor a, MDictLibraryDescriptor b) => a.Id == b.Id && a.Name == b.Name
        && a.MdxPath == b.MdxPath && a.FolderPath == b.FolderPath && a.EntryCount == b.EntryCount && a.Revision == b.Revision
        && a.Languages == b.Languages && a.ResourcePaths.SequenceEqual(b.ResourcePaths, StringComparer.Ordinal);
    private Task<T> RunAsync<T>(MDictLibraryDescriptor library, Func<MDictLibraryDescriptor, CancellationToken, T> action, CancellationToken token)
    {
        if (library.ResourcePaths.Count > 16) return Task.FromException<T>(Invalid());
        var captured = library with { ResourcePaths = Array.AsReadOnly(library.ResourcePaths.ToArray()) };
        return RunAsync(ct => action(captured, ct), token);
    }
    private async Task<T> RunAsync<T>(Func<CancellationToken, T> action, CancellationToken token)
    {
        CancellationTokenSource linked;
        lock (_lifetime) { ObjectDisposedException.ThrowIf(_closed, this); linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopping.Token); }
        using (linked)
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested(); var result = await Task.Run(() => action(linked.Token), CancellationToken.None).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested(); return result;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { throw new InvalidDataException("The dictionary file is invalid or unavailable.", error); }
            finally { _gate.Release(); }
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifetime) { if (_dispose is null) { _closed = true; var cancellation = _stopping.CancelAsync(); _dispose = Task.Run(() => DrainAsync(cancellation)); } return new(_dispose); }
    }
    private async Task DrainAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); } catch { }
        await _gate.WaitAsync().ConfigureAwait(false); try { foreach (var book in _books) book.Dispose(); _books.Clear(); } finally { _gate.Release(); _stopping.Dispose(); }
    }
    private static InvalidDataException Invalid() => new("The dictionary file is invalid or unavailable.");
}
