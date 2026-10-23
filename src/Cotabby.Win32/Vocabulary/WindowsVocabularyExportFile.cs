using System.IO;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// One synchronous export save owns its resolved destination directory until publication. This
/// adapts Mac destination/inode admission and owner-only stage + fsync + rename, without sharing
/// the notebook's stricter private-parent ACL rule: the user may choose an existing export folder.
/// No bytes reach the destination until the final same-directory rename. The archive worker owns
/// this short-lived lease; no native handle crosses an await or escapes to WPF.
/// </summary>
internal sealed class WindowsVocabularyExportFile : IDisposable
{
    private readonly SafeFileHandle _parent;
    private readonly VocabularyWindowsFile.FileIdentity _parentIdentity;
    private readonly string _requestedParent, _notebook;
    private readonly Func<Guid> _stagingIdentity;
    private readonly Action? _afterFlush;
    internal string Path { get; }

    internal WindowsVocabularyExportFile(string destination, string? notebook, Func<Guid>? stagingIdentity = null, Action? afterFlush = null)
    {
        _stagingIdentity = stagingIdentity ?? Guid.NewGuid; _afterFlush = afterFlush;
        _notebook = notebook is null ? "" : FullPath(notebook);
        string full = FullPath(destination); _requestedParent = System.IO.Path.GetDirectoryName(full)!;
        _parent = OpenDirectory(_requestedParent);
        try
        {
            _parentIdentity = DirectoryIdentity(_parent);
            Path = System.IO.Path.Combine(FinalPath(_parent), System.IO.Path.GetFileName(full)); Verify();
        }
        catch { _parent.Dispose(); throw; }
    }
    internal void Write(ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        Verify();
        string staging = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, ".notebook-" + _stagingIdentity().ToString("D"));
        FileStream? stream = null; bool committed = false;
        try
        {
            // FullControl includes DELETE access. Keeping this non-delete-sharing handle open
            // pins the exact created file, even when the selected directory allows peer mutation.
            stream = new FileInfo(staging).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 65536, FileOptions.None, VocabularyWindowsFile.NewFileSecurity());
            cancellationToken.ThrowIfCancellationRequested();
            stream.Write(data); stream.Flush(flushToDisk: true);
            _afterFlush?.Invoke(); Verify(); cancellationToken.ThrowIfCancellationRequested();
            Rename(stream.SafeFileHandle);
            committed = true; // No fallible operation or cancellation check follows this commit.
        }
        finally
        {
            if (stream is not null)
            {
                // FileDispositionInfo marks THIS open file for deletion on close. A failed
                // CREATE_NEW supplies no stream, so another creator's collision is never touched.
                // Failure may leave a private orphan; never fall back to deleting by pathname.
                if (!committed) _ = SetFileInformationByHandle(stream.SafeFileHandle, 4, [1], 1);
                try { stream.Dispose(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
    private void Rename(SafeFileHandle stage)
    {
        // FILE_RENAME_INFO is a DWORD/BOOLEAN union, aligned HANDLE, DWORD byte count, UTF-16
        // name. RootDirectory is retained through the call; only the admitted leaf is resolved
        // relative to that pinned parent. The OS renames the stage HANDLE, not a re-opened name.
        byte[] name = Encoding.Unicode.GetBytes(System.IO.Path.GetFileName(Path));
        int rootOffset = IntPtr.Size, lengthOffset = rootOffset + IntPtr.Size, nameOffset = lengthOffset + 4;
        byte[] info = new byte[Math.Max(nameOffset + name.Length + 2, IntPtr.Size == 8 ? 24 : 16)];
        BinaryPrimitives.WriteUInt32LittleEndian(info, 1); // ReplaceIfExists, FileRenameInfo (3).
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(lengthOffset), (uint)name.Length);
        name.CopyTo(info, nameOffset); bool retained = false;
        try
        {
            _parent.DangerousAddRef(ref retained);
            if (IntPtr.Size == 8) BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(rootOffset), _parent.DangerousGetHandle().ToInt64());
            else BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(rootOffset), _parent.DangerousGetHandle().ToInt32());
            if (!SetFileInformationByHandle(stage, 3, info, (uint)info.Length)) throw Invalid();
        }
        finally { if (retained) _parent.DangerousRelease(); }
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, [In] byte[] info, uint size);
    private void Verify()
    {
        using var requested = OpenDirectory(_requestedParent);
        if (_parent.IsClosed || DirectoryIdentity(_parent) != _parentIdentity || DirectoryIdentity(requested) != _parentIdentity) throw Invalid();
        using var target = OpenLeaf(Path);
        if (_notebook.Length == 0) return;
        string notebook = CanonicalLeaf(_notebook);
        foreach (string suffix in new[] { "", "-wal", "-shm", "-journal", ".lock" })
        {
            string protectedPath = notebook + suffix;
            if (StringComparer.OrdinalIgnoreCase.Equals(Path, protectedPath)) throw Invalid();
            using var protectedFile = OpenLeaf(protectedPath);
            if (target is not null && protectedFile is not null
                && VocabularyWindowsFile.Identity(target) == VocabularyWindowsFile.Identity(protectedFile)) throw Invalid();
        }
        string media = notebook + ".media";
        if (Directory.Exists(media)) { using var directory = OpenDirectory(media); media = FinalPath(directory); }
        if (StringComparer.OrdinalIgnoreCase.Equals(Path, media) || Path.StartsWith(media.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw Invalid();
    }
    private static string CanonicalLeaf(string path)
    {
        string parent = System.IO.Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) return VocabularyWindowsFile.Extended(path);
        using var directory = OpenDirectory(parent); return System.IO.Path.Combine(FinalPath(directory), System.IO.Path.GetFileName(path));
    }
    private static SafeFileHandle OpenDirectory(string path)
    {
        var handle = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(path), 0,
            VocabularyWindowsFile.Native.ShareReadWrite, IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting,
            VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        try { if (handle.IsInvalid) throw Invalid(); _ = DirectoryIdentity(handle); return handle; }
        catch { handle.Dispose(); throw; }
    }
    private static SafeFileHandle? OpenLeaf(string path)
    {
        var handle = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(path), 0, 7, IntPtr.Zero,
            VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError(); handle.Dispose();
            if (error is 2 or 3) return null;
            throw Invalid();
        }
        try { _ = VocabularyWindowsFile.Identity(handle); return handle; }
        catch { handle.Dispose(); throw Invalid(); }
    }
    private static VocabularyWindowsFile.FileIdentity DirectoryIdentity(SafeFileHandle handle)
    {
        if (VocabularyWindowsFile.Native.GetFileType(handle) != 1
            || !VocabularyWindowsFile.Native.GetAttributes(handle, 9, out var attributes, (uint)Marshal.SizeOf<VocabularyWindowsFile.FileAttributesInfo>())
            || (attributes.Attributes & 0x10u) == 0
            || !VocabularyWindowsFile.Native.GetIdentity(handle, 18, out var identity, (uint)Marshal.SizeOf<VocabularyWindowsFile.FileIdentity>())) throw Invalid();
        return identity;
    }
    private static string FinalPath(SafeFileHandle handle)
    {
        var name = new StringBuilder(32768);
        uint count = VocabularyWindowsFile.Native.GetFinalPathNameByHandleW(handle, name, (uint)name.Capacity, 0);
        if (count == 0 || count >= name.Capacity) throw Invalid();
        return name.ToString();
    }
    private static string FullPath(string path)
    { try { return VocabularyWindowsFile.FullPath(path); } catch (VocabularyException) { throw Invalid(); } }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    public void Dispose() => _parent.Dispose();
}
