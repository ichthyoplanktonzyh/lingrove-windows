using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Connection/lock-owned Windows replacement for Mac regular-leaf + inode checks. The parent is
/// resolved through a directory handle; the leaf is opened as a reparse point and must be a regular
/// disk file. Retained read handles deny delete/rename, while volume plus 128-bit file ID verifies
/// both the canonical name and the caller's original parent alias before a transaction can proceed.
/// </summary>
internal sealed class VocabularyWindowsFile : IDisposable
{
    private readonly SafeFileHandle _parent, _handle;
    private readonly FileIdentity _identity;
    private readonly string _originalPath;
    private readonly bool _isLock;
    internal string Path { get; }
    internal SafeFileHandle Handle => _handle;
    private VocabularyWindowsFile(SafeFileHandle parent, SafeFileHandle handle, FileIdentity identity, string original, string canonical, bool isLock)
    { _parent = parent; _handle = handle; _identity = identity; _originalPath = original; Path = canonical; _isLock = isLock; }

    internal static VocabularyWindowsFile Open(string path, bool createLock = false)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        SafeFileHandle? parent = null, handle = null;
        try
        {
            string full = FullPath(path), leaf = System.IO.Path.GetFileName(full);
            parent = Native.CreateFileW(Extended(System.IO.Path.GetDirectoryName(full)!), Native.Read, Native.ShareReadWrite,
                IntPtr.Zero, Native.OpenExisting, Native.BackupSemantics, IntPtr.Zero);
            if (parent.IsInvalid) throw Invalid();
            var name = new StringBuilder(32768);
            uint count = Native.GetFinalPathNameByHandleW(parent, name, (uint)name.Capacity, 0);
            if (count == 0 || count >= name.Capacity) throw Invalid();
            CheckParentInheritance(name.ToString());
            string canonical = System.IO.Path.Combine(name.ToString(), leaf);
            handle = createLock ? CreateLock(canonical) : Native.CreateFileW(canonical, Native.Read, Native.ShareReadWrite,
                IntPtr.Zero, Native.OpenExisting, Native.OpenReparsePoint | Native.BackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid) throw Invalid();
            var result = new VocabularyWindowsFile(parent, handle, Identity(handle), full, canonical, createLock);
            result.Verify(); return result;
        }
        catch (Exception error) when (error is VocabularyException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { handle?.Dispose(); parent?.Dispose(); throw Invalid(); }
    }
    internal static string FullPath(string path)
    {
        if (string.IsNullOrEmpty(path)) throw Invalid();
        string requestedLeaf = System.IO.Path.GetFileName(path);
        // GetFullPath on Windows can remove a trailing dot/space. Inspect the caller's literal
        // leaf first, or an ambiguous request would silently become a different file name.
        if (requestedLeaf.Length == 0 || requestedLeaf.Contains(':') || requestedLeaf.EndsWith('.') || requestedLeaf.EndsWith(' ')) throw Invalid();
        _ = new UTF8Encoding(false, true).GetByteCount(path);
        string full = System.IO.Path.GetFullPath(path), leaf = System.IO.Path.GetFileName(full);
        // Check before creating a parent/staging file: alternate streams and Win32-trimmed
        // aliases are not standalone database files, and must cause no storage mutation.
        if (leaf.Length == 0 || leaf.Contains(':') || leaf.EndsWith('.') || leaf.EndsWith(' ')) throw Invalid();
        _ = new UTF8Encoding(false, true).GetByteCount(full); return full;
    }
    internal static void CreateParentDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        if (Directory.Exists(path)) return;
        var missing = new List<string>();
        for (string? component = path; component is not null && !Directory.Exists(component); component = System.IO.Path.GetDirectoryName(component))
            missing.Add(component);
        // Mac creates new notebook directories with 0700. The Windows counterpart creates
        // with a protected current-user ACL atomically, so no public inherited-access window
        // precedes a later ACL edit. Existing directories keep their owner's chosen permissions.
        using var identity = WindowsIdentity.GetCurrent(); var user = identity.User ?? throw Invalid();
        var security = new DirectorySecurity(); security.SetOwner(user); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Extended(path)).Create(security);
        // Directory creation treats a concurrent winner as success. Verify every component we
        // observed missing instead of assuming that our descriptor won that race.
        foreach (string component in missing) CheckParentInheritance(component);
    }
    internal static FileSecurity NewFileSecurity()
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        using var identity = WindowsIdentity.GetCurrent(); var user = identity.User ?? throw Invalid();
        var security = new FileSecurity(); security.SetOwner(user); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
    internal static void CheckParentInheritance(string path)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        // SQLite's Windows VFS creates WAL/SHM with the parent ACL, unlike the Unix VFS's
        // database-mode propagation. A shared inheritable data ACL is therefore unsupported:
        // refuse it without changing an existing directory's permissions. The product's private
        // per-user folder passes; this avoids a period where a sidecar exposes notebook text.
        using var identity = WindowsIdentity.GetCurrent(); string user = identity.User?.Value ?? throw Invalid();
        string[] trusted = [user, "S-1-5-18", "S-1-5-32-544", "S-1-3-0", "S-1-3-4"];
        var metadataOnly = FileSystemRights.Synchronize | FileSystemRights.ReadPermissions | FileSystemRights.ReadAttributes;
        var directoryMutation = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        var security = new DirectoryInfo(Extended(path)).GetAccessControl();
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !trusted.Take(3).Contains(owner.Value)
            || new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl is not { Count: > 0 }) throw Invalid();
        bool privateInheritance = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            // A non-inheritable grant can still let another principal change this DACL or
            // plant a future sidecar. Check direct directory mutation separately from child ACLs.
            if (rule.AccessControlType == AccessControlType.Allow && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                && !trusted.Take(3).Contains(rule.IdentityReference.Value, StringComparer.Ordinal)
                && (rule.FileSystemRights & directoryMutation) != 0) throw Invalid();
            if (rule.AccessControlType == AccessControlType.Allow && (rule.InheritanceFlags & InheritanceFlags.ObjectInherit) != 0
                && (rule.IdentityReference.Value == user || rule.IdentityReference.Value is "S-1-3-0" or "S-1-3-4")
                && (rule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.WriteData)) == (FileSystemRights.ReadData | FileSystemRights.WriteData))
                privateInheritance = true;
            if (rule.AccessControlType == AccessControlType.Allow && (rule.InheritanceFlags & InheritanceFlags.ObjectInherit) != 0
                && !trusted.Contains(rule.IdentityReference.Value, StringComparer.Ordinal)
                && (rule.FileSystemRights & ~metadataOnly) != 0) throw Invalid();
        }
        if (!privateInheritance) throw Invalid();
    }
    private static SafeFileHandle CreateLock(string path)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        // Mac opens new lock files with 0600 even in an existing directory. Supply the protected
        // user ACL at CREATE time; OPEN_ALWAYS leaves an existing file's chosen ACL untouched.
        // Pinning lasts only through this synchronous native call: Windows copies the descriptor.
        byte[] descriptor = NewFileSecurity().GetSecurityDescriptorBinaryForm();
        var pin = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pin.AddrOfPinnedObject() };
            return Native.CreateSecureFileW(path, Native.Read | Native.Write, Native.ShareReadWrite, ref attributes,
                Native.OpenAlways, Native.OpenReparsePoint | Native.BackupSemantics, IntPtr.Zero);
        }
        finally { pin.Free(); }
    }
    internal void Verify()
    {
        try
        {
            if (_handle.IsClosed || Identity(_handle) != _identity) throw Invalid();
            CheckParentInheritance(System.IO.Path.GetDirectoryName(Path)!); CheckFileAccess(_handle);
            VerifyName(Path);
            if (!StringComparer.Ordinal.Equals(Path, _originalPath)) VerifyName(_originalPath);
            if (!_isLock) foreach (string suffix in new[] { "-wal", "-shm", "-journal" })
            {
                // Existing sidecars may predate a directory ACL change. Inspect the opened
                // leaf itself before SQLite can recover/map it; never follow a sidecar link.
                using var sidecar = Native.CreateFileW(Extended(Path + suffix), Native.Read, 7, IntPtr.Zero,
                    Native.OpenExisting, Native.OpenReparsePoint | Native.BackupSemantics, IntPtr.Zero);
                if (sidecar.IsInvalid)
                { if (Marshal.GetLastPInvokeError() is 2 or 3) continue; throw Invalid(); }
                _ = Identity(sidecar); CheckFileAccess(sidecar);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { throw Invalid(); }
    }
    internal static void CheckFileAccess(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        // A duplicate lets the framework query the exact opened object's ACL without borrowing
        // ownership of the retained identity handle. Disposing this stream closes only the copy.
        IntPtr process = Native.GetCurrentProcess();
        if (!Native.DuplicateHandle(process, handle, process, out var copy, 0, false, 2)) { copy?.Dispose(); throw Invalid(); }
        using (copy) using (var stream = new FileStream(copy, FileAccess.Read))
        {
            var security = stream.GetAccessControl(); using var identity = WindowsIdentity.GetCurrent();
            string[] trusted = [identity.User?.Value ?? throw Invalid(), "S-1-5-18", "S-1-5-32-544"];
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !trusted.Contains(owner.Value)
                || new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl is not { Count: > 0 }) throw Invalid();
            var metadataOnly = FileSystemRights.Synchronize | FileSystemRights.ReadPermissions | FileSystemRights.ReadAttributes;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                    && !trusted.Contains(rule.IdentityReference.Value, StringComparer.Ordinal)
                    && (rule.FileSystemRights & ~metadataOnly) != 0) throw Invalid();
        }
    }
    private void VerifyName(string path)
    {
        using var named = Native.CreateFileW(Extended(path), Native.Read, Native.ShareReadWrite, IntPtr.Zero,
            Native.OpenExisting, Native.OpenReparsePoint | Native.BackupSemantics, IntPtr.Zero);
        if (named.IsInvalid || Identity(named) != _identity) throw Invalid();
    }
    internal static FileIdentity Identity(SafeFileHandle handle)
    {
        if (Native.GetFileType(handle) != 1
            || !Native.GetAttributes(handle, 9, out var attributes, (uint)Marshal.SizeOf<FileAttributesInfo>())
            || (attributes.Attributes & (0x10u | 0x400u)) != 0
            || !Native.GetIdentity(handle, 18, out var identity, (uint)Marshal.SizeOf<FileIdentity>())) throw Invalid();
        return identity;
    }
    internal static void Publish(string staged, string destination)
    {
        // Same-directory rename, no REPLACE_EXISTING or COPY_ALLOWED: the winner is never
        // overwritten and an initialized closed schema is the only file we can publish.
        if (Native.MoveFileExW(Extended(staged), Extended(destination), 8)) return;
        int error = Marshal.GetLastPInvokeError();
        if (error is not 80 and not 183) throw Invalid();
    }
    internal static string Extended(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    public void Dispose() { _handle.Dispose(); _parent.Dispose(); }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct FileIdentity(ulong Volume, ulong Low, ulong High);
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileAttributesInfo { public uint Attributes, ReparseTag; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct LockOverlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }
    internal static class Native
    {
        internal const uint Read = 0x80000000, Write = 0x40000000, Delete = 0x00010000, ShareReadWrite = 3, OpenExisting = 3, OpenAlways = 4,
            OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern SafeFileHandle CreateSecureFileW(string name, uint access, uint sharing, ref SecurityAttributes security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess,
            out SafeFileHandle target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder name, uint size, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint GetFileType(SafeFileHandle handle);
        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Attributes(SafeFileHandle handle, int type, out FileAttributesInfo value, uint size);
        internal static bool GetAttributes(SafeFileHandle handle, int type, out FileAttributesInfo value, uint size) => Attributes(handle, type, out value, size);
        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetIdentity(SafeFileHandle handle, int type, out FileIdentity value, uint size);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileInformationByHandle(SafeFileHandle handle, int type, ref int value, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MoveFileExW(string source, string destination, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool LockFileEx(SafeFileHandle handle, uint flags, uint reserved, uint low, uint high, ref LockOverlapped position);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnlockFileEx(SafeFileHandle handle, uint reserved, uint low, uint high, ref LockOverlapped position);
    }
}
