using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Mac VocabularyImageStorage's synchronous media owner. The archive will serialize this instance
/// with its database: null notebook means isolated memory, otherwise bytes live beside it in
/// &lt;database filename&gt;.media. Construction and preparation create no directory or file. A write
/// returns true only for new media, letting a failed metadata transaction remove its own additions
/// while retaining a repaired pre-existing resource. This library does not yet connect the archive
/// or editor. Call off the dispatcher and hold the archive's cross-process lock around transactions.
/// </summary>
internal sealed class WindowsVocabularyImageStorage
{
    private readonly Dictionary<string, byte[]> _memory = new(StringComparer.Ordinal);
    internal string? DirectoryPath { get; }
    internal WindowsVocabularyImageStorage(string? notebook)
    { DirectoryPath = notebook is null ? null : VocabularyWindowsFile.FullPath(notebook) + ".media"; }

    internal byte[] Read(VocabularyImageReference image) => ContentOperation(() =>
    {
        ValidateReference(image);
        if (DirectoryPath is null)
        {
            if (!_memory.TryGetValue(image.Filename, out var bytes)) throw Invalid();
            WindowsVocabularyImagePreparer.Validate(bytes, image); return (byte[])bytes.Clone();
        }
        using var root = VocabularyMediaDirectory.Open(DirectoryPath, create: false);
        using var file = root.OpenFile(image.Filename) ?? throw Invalid();
        byte[] result = Read(file, image); root.Verify(); return result;
    });

    internal bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image)
    {
        // Copy after the ceiling check so an inadmissible caller cannot force a second unbounded
        // allocation. Validate this detached copy before any filesystem mutation or reuse decision.
        ValidateReference(image);
        if (data.Length != image.ByteCount) throw Invalid();
        byte[] bytes = data.ToArray(); WindowsVocabularyImagePreparer.Validate(bytes, image);
        return ContentOperation(() => WriteValidated(bytes, image));
    }
    private bool WriteValidated(byte[] bytes, VocabularyImageReference image)
    {
        if (DirectoryPath is null)
        {
            if (_memory.TryGetValue(image.Filename, out var current))
            {
                try { WindowsVocabularyImagePreparer.Validate(current, image); return false; }
                catch (VocabularyException) { /* Mac can repair bytes under a previously present hash. */ }
            }
            _memory[image.Filename] = bytes; return true;
        }
        using var root = VocabularyMediaDirectory.Open(DirectoryPath, create: true);
        string destination = System.IO.Path.Combine(root.Path, image.Filename);
        bool existed;
        using (var current = root.OpenFile(image.Filename))
        {
            existed = current is not null;
            if (current is not null && Matches(current, image)) return false;
        }
        string staging = System.IO.Path.Combine(root.Path, ".vocabulary-image-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // CREATE_NEW with a protected ACL precedes the first byte. No inherited public-data
            // window exists, and Flush(true) precedes same-volume atomic publication. Staging is
            // private to this operation and is the only path the finally block may erase.
            using (var stream = new FileInfo(staging).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 65536, FileOptions.None, VocabularyWindowsFile.NewFileSecurity()))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            root.Verify();
            if (!existed)
            {
                if (VocabularyWindowsFile.Native.MoveFileExW(staging, destination, 8)) return true;
                if (Marshal.GetLastPInvokeError() is not 80 and not 183) throw Invalid();
                // Another writer won publication. Verify that actual leaf rather than assuming
                // equal hash names imply equal bytes; a valid winner is reused without rewriting.
                using var winner = root.OpenFile(image.Filename) ?? throw Invalid();
                if (Matches(winner, image)) return false;
            }
            // Re-open immediately before repair: replacing an unsafe leaf is never recovery.
            // Drop our read handle before rename, because it deliberately denies delete sharing.
            using (var repair = root.OpenFile(image.Filename) ?? throw Invalid()) { }
            root.Verify();
            if (!VocabularyWindowsFile.Native.MoveFileExW(staging, destination, 8 | 1)) throw Invalid();
            return false;
        }
        finally { try { File.Delete(staging); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    internal void Remove(VocabularyImageReference image)
    {
        // Cleanup follows a successful SQL commit or rolls back new media. As on Mac, a cleanup
        // failure cannot undo acknowledged metadata; unsafe paths are left untouched, not followed.
        try
        {
            ContentOperation(() =>
            {
                ValidateReference(image);
                if (DirectoryPath is null) { _memory.Remove(image.Filename); return true; }
                using var root = VocabularyMediaDirectory.Open(DirectoryPath, create: false);
                using (var file = root.OpenFile(image.Filename)) { if (file is null) return true; }
                root.Verify(); File.Delete(System.IO.Path.Combine(root.Path, image.Filename)); return true;
            });
        }
        catch (VocabularyException) { }
    }
    private static bool Matches(SafeFileHandle file, VocabularyImageReference image)
    { try { _ = Read(file, image); return true; } catch (VocabularyException) { return false; } }
    private static byte[] Read(SafeFileHandle file, VocabularyImageReference image)
    {
        if (RandomAccess.GetLength(file) != image.ByteCount) throw Invalid();
        // The handle pins identity, not content. Read at most expected+1 even if another process
        // grows the file after GetLength, then validate exact count, digest, PNG and dimensions.
        byte[] bytes = new byte[checked((int)image.ByteCount + 1)]; int count = 0;
        while (count < bytes.Length)
        {
            int read = RandomAccess.Read(file, bytes.AsSpan(count), count);
            if (read == 0) break; count += read;
        }
        if (count != image.ByteCount) throw Invalid();
        byte[] result = bytes.AsSpan(0, count).ToArray(); WindowsVocabularyImagePreparer.Validate(result, image); return result;
    }
    private static void ValidateReference(VocabularyImageReference image)
    { if (!VocabularyContentPolicy.IsValid(image)) throw Invalid(); }
    private static T ContentOperation<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (VocabularyException error) when (error.Failure != VocabularyFailure.InvalidContent) { throw Invalid(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { throw Invalid(); }
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
}
