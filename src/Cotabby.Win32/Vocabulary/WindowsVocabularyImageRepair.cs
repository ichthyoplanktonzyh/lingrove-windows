using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

internal enum VocabularyImageRepairPhase { BeforeTargetOpen, TargetClosed, BeforeCommit, Committed }

/// <summary>Optional local-NTFS adapter for Mac's atomic repair of a pre-existing digest leaf.
/// The caller retains the one Store's exclusive metadata lock and media-directory lease throughout.
/// A transaction-bound writer pins the verified old object; its disposition and the held stage's
/// no-replace rename become visible together. SQLite is deliberately not enlisted: a committed
/// repair is retained even if the later notebook commit fails. Unsupported capability fails closed.
/// New staging bytes receive the existing protected ACL at creation; no pathname deletion occurs.</summary>
internal static partial class WindowsVocabularyImageRepair
{
    private const uint Read = 0x80000000, Write = 0x40000000, Delete = 0x00010000,
        WriteAttributes = 0x100, ShareRead = 1, OpenReparsePoint = 0x00200000,
        SupportsTransactions = 0x00200000;

    internal static void Repair(VocabularyMediaDirectory root, byte[] bytes, VocabularyImageReference image,
        VocabularyWindowsFile.FileIdentity expected, Guid stagingId, Func<SafeFileHandle, bool> matches,
        Action<VocabularyImageRepairPhase>? observed = null)
    {
        if (!OperatingSystem.IsWindows()) throw Invalid();
        try { RepairCore(root, bytes, image, expected, stagingId, matches, observed); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { throw Invalid(); }
    }

    private static void RepairCore(VocabularyMediaDirectory root, byte[] bytes, VocabularyImageReference image,
        VocabularyWindowsFile.FileIdentity expected, Guid stagingId, Func<SafeFileHandle, bool> matches,
        Action<VocabularyImageRepairPhase>? observed)
    {
        root.Verify();
        if (Native.GetVolumeInformationByHandleW(root.Handle, 0, 0, 0, 0, out uint flags, 0, 0) == 0
            || (flags & SupportsTransactions) == 0) throw Invalid();
        observed?.Invoke(VocabularyImageRepairPhase.BeforeTargetOpen);
        // Match the source's acknowledged local file write: do not invent a transaction deadline
        // that could abort an otherwise valid repair on a slow disk.
        using var transaction = Native.CreateTransaction(0, 0, 0, 0, 0, 0, "Lingrove image repair");
        if (transaction.IsInvalid) throw Invalid();
        bool committed = false;
        try
        {
            string destination = Path.Combine(root.Path, image.Filename);
            using (var target = Native.CreateFileTransactedW(destination, Read | Delete | WriteAttributes, ShareRead,
                0, 3, OpenReparsePoint, 0, transaction, 0, 0))
            {
                if (target.IsInvalid) throw Invalid();
                var identity = VocabularyWindowsFile.Identity(target);
                VocabularyWindowsFile.CheckFileAccess(target); root.Verify();
                // A concurrently published valid winner needs no mutation. A different corrupt
                // object must never be overwritten under the earlier object's authorization.
                if (matches(target)) return;
                if (identity != expected) throw Invalid();
                Disposition(target);
            }
            // TxF requires deletion handles closed before commit. Its writer/namespace locks,
            // not a dropped ordinary read handle, protect this interval from external writers.
            observed?.Invoke(VocabularyImageRepairPhase.TargetClosed);
            string staging = Path.Combine(root.Path, ".vocabulary-image-" + stagingId.ToString("N") + ".tmp");
            VocabularyWindowsFile.FileIdentity stageIdentity;
            using (var stage = CreatePrivateStage(staging, transaction))
            {
                if (stage.IsInvalid) throw Invalid();
                stageIdentity = VocabularyWindowsFile.Identity(stage);
                using var stream = new FileStream(stage, FileAccess.ReadWrite, 65536, isAsync: false);
                stream.Write(bytes); stream.Flush(flushToDisk: true); root.Verify();
                Rename(stage, destination);
            }
            observed?.Invoke(VocabularyImageRepairPhase.BeforeCommit);
            root.Verify();
            if (Native.CommitTransaction(transaction) == 0) throw Invalid();
            committed = true;
            observed?.Invoke(VocabularyImageRepairPhase.Committed);
            using var published = root.OpenFile(image.Filename) ?? throw Invalid();
            if (VocabularyWindowsFile.Identity(published) != stageIdentity || !matches(published)) throw Invalid();
            root.Verify();
        }
        finally
        {
            // All file handles close before rollback/commit. Closing the final uncommitted
            // transaction handle is also a kernel rollback path, including process termination.
            if (!committed) _ = Native.RollbackTransaction(transaction);
        }
    }

    private static unsafe SafeFileHandle CreatePrivateStage(string path, TransactionHandle transaction)
    {
        byte[] descriptor = VocabularyWindowsFile.NewFileSecurity().GetSecurityDescriptorBinaryForm();
        fixed (byte* security = descriptor)
        {
            var attributes = new SecurityAttributes { Length = sizeof(SecurityAttributes), Descriptor = (nint)security };
            return Native.CreatePrivateFileTransactedW(path, Read | Write | Delete, ShareRead, ref attributes,
                1, OpenReparsePoint, 0, transaction, 0, 0);
        }
    }
    private static unsafe void Disposition(SafeFileHandle file)
    {
        byte delete = 1; // FILE_DISPOSITION_INFO.DeleteFile is BOOLEAN, not Win32 BOOL.
        if (Native.SetFileInformationByHandle(file, 4, &delete, sizeof(byte)) == 0) throw Invalid();
    }
    private static unsafe void Rename(SafeFileHandle stage, string destination)
    {
        byte[] name = Encoding.Unicode.GetBytes(destination);
        int lengthOffset = IntPtr.Size * 2, nameOffset = lengthOffset + sizeof(uint);
        byte[] info = new byte[Math.Max(checked(nameOffset + name.Length + 2), IntPtr.Size == 8 ? 24 : 16)];
        // FILE_RENAME_INFO: zero ReplaceIfExists/NULL root, byte count, exact UTF-16 name.
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(lengthOffset), (uint)name.Length);
        name.CopyTo(info, nameOffset);
        fixed (byte* value = info)
            if (Native.SetFileInformationByHandle(stage, 3, value, (uint)info.Length) == 0) throw Invalid();
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { internal int Length; internal nint Descriptor; internal int InheritHandle; }
    private sealed class TransactionHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public TransactionHandle() : base(true) { }
        protected override bool ReleaseHandle() => Native.CloseHandle(handle) != 0;
    }
    private static partial class Native
    {
        [LibraryImport("Ktmw32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial TransactionHandle CreateTransaction(nint attributes, nint uow, uint options, uint isolation, uint flags, uint timeout, string description);
        [LibraryImport("Ktmw32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int CommitTransaction(TransactionHandle transaction);
        [LibraryImport("Ktmw32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int RollbackTransaction(TransactionHandle transaction);
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial SafeFileHandle CreateFileTransactedW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template, TransactionHandle transaction, nint miniVersion, nint extended);
        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileTransactedW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial SafeFileHandle CreatePrivateFileTransactedW(string path, uint access, uint share, ref SecurityAttributes security, uint creation, uint flags, nint template, TransactionHandle transaction, nint miniVersion, nint extended);
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe partial int SetFileInformationByHandle(SafeFileHandle file, int kind, byte* value, uint length);
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int GetVolumeInformationByHandleW(SafeFileHandle root, nint name, uint nameSize, nint serial, nint maximumName, out uint flags, nint filesystem, uint filesystemSize);
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int CloseHandle(nint handle);
    }
}
