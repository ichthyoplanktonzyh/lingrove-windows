using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Mac VocabularyImageStorage's synchronous media owner. The archive will serialize this instance
/// with its database: null notebook means isolated memory, otherwise bytes live beside it in
/// &lt;database filename&gt;.media for the legacy archive, or an explicit canonical media directory.
/// Construction and preparation create no directory or file. A write
/// returns true only for new media, letting a failed metadata transaction remove its own additions
/// while retaining a repaired pre-existing resource. The archive owns this library; future image
/// presentation borrows archive operations. Call off the dispatcher and hold its transaction lock.
/// </summary>
internal sealed class WindowsVocabularyImageStorage : IOwnedVocabularyImageStorage
{
    private readonly Dictionary<string, byte[]> _memory = new(StringComparer.Ordinal);
    private readonly Func<Guid> _stagingIdentity;
    internal Action? BeforeOwnedDelete { get; set; }
    internal Action? BeforeOwnedPublish { get; set; }
    internal Action? AfterOwnedPublish { get; set; }
    internal Action<VocabularyImageRepairPhase>? RepairObserved { get; set; }
    internal string? DirectoryPath { get; }
    internal WindowsVocabularyImageStorage(string? notebook) : this(notebook, Guid.NewGuid) { }
    /// <summary>Tests may force a CREATE_NEW collision without changing actual file operations.</summary>
    internal WindowsVocabularyImageStorage(string? notebook, Func<Guid> stagingIdentity)
    { DirectoryPath = notebook is null ? null : VocabularyWindowsFile.FullPath(notebook) + ".media"; _stagingIdentity = stagingIdentity; }
    internal static WindowsVocabularyImageStorage ForDirectory(string directory) => new(directory, Guid.NewGuid, true);
    internal static WindowsVocabularyImageStorage ForDirectory(string directory, Func<Guid> stagingIdentity) => new(directory, stagingIdentity, true);
    private WindowsVocabularyImageStorage(string directory, Func<Guid> stagingIdentity, bool explicitDirectory)
    { DirectoryPath = VocabularyWindowsFile.FullPath(directory); _stagingIdentity = stagingIdentity; }

    public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken cancellationToken) => WindowsVocabularyImagePreparer.Prepare(data, cancellationToken);
    public VocabularyPreparedImage PrepareFile(string path, CancellationToken cancellationToken) =>
        Prepare(WindowsVocabularyImageInput.ReadFile(path, cancellationToken), cancellationToken);
    public byte[] Read(VocabularyImageReference image) => ContentOperation(() =>
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

    public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image)
    {
        // Copy after the ceiling check so an inadmissible caller cannot force a second unbounded
        // allocation. Validate this detached copy before any filesystem mutation or reuse decision.
        ValidateReference(image);
        if (data.Length != image.ByteCount) throw Invalid();
        byte[] bytes = data.ToArray(); WindowsVocabularyImagePreparer.Validate(bytes, image);
        return ContentOperation(() => WriteValidated(bytes, image));
    }
    public VocabularyWindowsFile.FileIdentity? WriteOwned(ReadOnlySpan<byte> data, VocabularyImageReference image)
    {
        ValidateReference(image);
        if (DirectoryPath is null || data.Length != image.ByteCount) throw Invalid();
        byte[] bytes = data.ToArray(); WindowsVocabularyImagePreparer.Validate(bytes, image);
        return ContentOperation<VocabularyWindowsFile.FileIdentity?>(() => WriteOwnedValidated(bytes, image));
    }
    private VocabularyWindowsFile.FileIdentity? WriteOwnedValidated(byte[] bytes, VocabularyImageReference image)
    {
        using var root = VocabularyMediaDirectory.Open(DirectoryPath ?? throw Invalid(), create: true);
        VocabularyWindowsFile.FileIdentity? repairIdentity = null;
        using (var current = root.OpenFile(image.Filename))
        {
            if (current is not null)
            {
                if (Matches(current, image)) return null;
                repairIdentity = VocabularyWindowsFile.Identity(current);
            }
        }
        if (repairIdentity is { } expected)
        {
            WindowsVocabularyImageRepair.Repair(root, bytes, image, expected, _stagingIdentity(), file => Matches(file, image), RepairObserved);
            AfterOwnedPublish?.Invoke();
            return null; // Pre-existing repair must survive the caller's later SQL rollback.
        }
        string staging = System.IO.Path.Combine(root.Path, ".vocabulary-image-" + _stagingIdentity().ToString("N") + ".tmp");
        string destination = System.IO.Path.Combine(root.Path, image.Filename);
        VocabularyWindowsFile.FileIdentity identity = default;
        bool moved = false;
        try
        {
            using (var stream = new FileInfo(staging).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.None, VocabularyWindowsFile.NewFileSecurity()))
            {
                identity = VocabularyWindowsFile.Identity(stream.SafeFileHandle);
                try
                {
                    stream.Write(bytes); stream.Flush(flushToDisk: true); root.Verify(); BeforeOwnedPublish?.Invoke();
                    if (!VocabularyWindowsFile.Native.MoveFileExW(staging, destination, 8))
                    {
                        if (Marshal.GetLastPInvokeError() is not 80 and not 183) throw Invalid();
                        using var winner = root.OpenFile(image.Filename) ?? throw Invalid();
                        if (!Matches(winner, image)) throw Invalid();
                        root.Verify(); return null;
                    }
                    moved = true;
                }
                finally { if (!moved) DeleteHandle(stream.SafeFileHandle); }
            }
            AfterOwnedPublish?.Invoke();
            using var published = root.OpenFile(image.Filename) ?? throw Invalid();
            if (VocabularyWindowsFile.Identity(published) != identity || !Matches(published, image)) throw Invalid();
            root.Verify(); return identity;
        }
        catch
        {
            // No receipt has escaped yet. Remove only this publication, by identity; a
            // replacement leaf or a cleanup failure must not hide the original error.
            if (moved) { try { RemoveOwned(image, identity); } catch { } }
            throw;
        }
    }
    public VocabularyWindowsFile.FileIdentity? CaptureIdentity(VocabularyImageReference image) => ContentOperation<VocabularyWindowsFile.FileIdentity?>(() =>
    {
        ValidateReference(image); if (DirectoryPath is null) throw Invalid();
        using var root = VocabularyMediaDirectory.Open(DirectoryPath, create: false);
        using var file = root.OpenFile(image.Filename);
        return file is null ? null : VocabularyWindowsFile.Identity(file);
    });
    public void RemoveOwned(VocabularyImageReference image, VocabularyWindowsFile.FileIdentity identity) => ContentOperation(() =>
    {
        ValidateReference(image); if (DirectoryPath is null) throw Invalid();
        using var root = VocabularyMediaDirectory.Open(DirectoryPath, create: false);
        using var file = root.OpenFile(image.Filename, delete: true);
        if (file is not null && VocabularyWindowsFile.Identity(file) == identity) { root.Verify(); BeforeOwnedDelete?.Invoke(); DeleteHandle(file); }
        return true;
    });
    private static void DeleteHandle(SafeFileHandle handle)
    {
        byte disposition = 1; // FILE_DISPOSITION_INFO.DeleteFile is a one-byte BOOLEAN.
        if (!VocabularyWindowsFile.Native.SetFileInformationByHandle(handle, 4, ref disposition, sizeof(byte))) throw Invalid();
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
        return WriteOwnedValidated(bytes, image) is not null;
    }

    public void Remove(VocabularyImageReference image)
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
                using var file = root.OpenFile(image.Filename, delete: true);
                if (file is null) return true;
                root.Verify(); DeleteHandle(file); return true;
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
