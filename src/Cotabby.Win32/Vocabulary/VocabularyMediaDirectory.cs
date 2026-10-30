using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// One synchronous media operation owns this directory handle. Unlike a database parent alias,
/// the media root itself must be an actual directory, never a reparse point. Holding it without
/// delete sharing stabilizes the root while files are checked or published. Canonical and requested
/// names must still identify that root; this catches a retargeted ancestor alias. No handle crosses
/// an await. The archive's metadata lock must enclose complete media/SQL transactions, not just IO.
/// </summary>
internal sealed class VocabularyMediaDirectory : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly VocabularyWindowsFile.FileIdentity _identity;
    private readonly string _requested;
    internal string Path { get; }
    // Borrow only within the synchronous directory lease; marshalling retains the native handle.
    internal SafeFileHandle Handle => _handle;

    private VocabularyMediaDirectory(SafeFileHandle handle, string requested)
    {
        _handle = handle; _requested = requested; _identity = Identity(handle);
        var name = new StringBuilder(32768);
        uint count = VocabularyWindowsFile.Native.GetFinalPathNameByHandleW(handle, name, (uint)name.Capacity, 0);
        if (count == 0 || count >= name.Capacity) throw Invalid();
        Path = name.ToString(); Verify();
    }
    internal static VocabularyMediaDirectory Open(string path, bool create)
    {
        string requested = VocabularyWindowsFile.FullPath(path);
        if (create) VocabularyWindowsFile.CreateParentDirectory(requested);
        var handle = OpenRoot(requested);
        try { return new(handle, requested); }
        catch { handle.Dispose(); throw; }
    }
    internal void Verify()
    {
        if (_handle.IsClosed || Identity(_handle) != _identity) throw Invalid();
        VocabularyWindowsFile.CheckParentInheritance(Path);
        using var current = OpenRoot(Path); using var original = OpenRoot(_requested);
        if (Identity(current) != _identity || Identity(original) != _identity) throw Invalid();
    }
    /// <summary>
    /// A missing leaf is distinguishable from an unsafe/corrupt leaf. Repairs may replace only
    /// regular private files, so an access failure or a link is never treated as a missing image.
    /// Returned handles belong to the caller and deny deletion until the verification/read ends.
    /// </summary>
    internal SafeFileHandle? OpenFile(string filename, bool delete = false, bool reportSharingViolation = false)
    {
        Verify();
        var handle = VocabularyWindowsFile.Native.CreateFileW(System.IO.Path.Combine(Path, filename),
            VocabularyWindowsFile.Native.Read | (delete ? VocabularyWindowsFile.Native.Delete : 0), VocabularyWindowsFile.Native.ShareReadWrite, IntPtr.Zero,
            VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError(); handle.Dispose();
            if (error == 2) return null;
            if (reportSharingViolation && error == 32) throw new VocabularyFileSharingException();
            throw Invalid();
        }
        try
        {
            _ = VocabularyWindowsFile.Identity(handle); VocabularyWindowsFile.CheckFileAccess(handle); Verify(); return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    private static SafeFileHandle OpenRoot(string path)
    {
        var handle = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(path), VocabularyWindowsFile.Native.Read,
            VocabularyWindowsFile.Native.ShareReadWrite, IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting,
            VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        try { if (handle.IsInvalid) throw Invalid(); _ = Identity(handle); return handle; }
        catch { handle.Dispose(); throw; }
    }
    private static VocabularyWindowsFile.FileIdentity Identity(SafeFileHandle handle)
    {
        if (VocabularyWindowsFile.Native.GetFileType(handle) != 1
            || !VocabularyWindowsFile.Native.GetAttributes(handle, 9, out var attributes, (uint)Marshal.SizeOf<VocabularyWindowsFile.FileAttributesInfo>())
            || (attributes.Attributes & (0x10u | 0x400u)) != 0x10u
            || !VocabularyWindowsFile.Native.GetIdentity(handle, 18, out var identity, (uint)Marshal.SizeOf<VocabularyWindowsFile.FileIdentity>())) throw Invalid();
        return identity;
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    public void Dispose() => _handle.Dispose();
}

// Only an explicit mailbox caller may retry the native sharing violation. All other
// admission failures retain their existing fail-closed behavior.
internal sealed class VocabularyFileSharingException : IOException;
