using System.IO;
using System.Runtime.InteropServices;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Worker-only bounded read of a backup explicitly chosen by the user. Unlike private notebook
/// storage, an import needs no private ACL. Like Mac boundedRead's O_NOFOLLOW, a leaf reparse
/// point is rejected. The regular disk handle denies writes/deletion throughout the read, so
/// metadata and bytes describe one source.
/// No directory, staging file or database is created here; restore admission owns the next step.
/// </summary>
internal static class WindowsVocabularyBackupInput
{
    internal static byte[] ReadFile(string path)
    {
        try
        {
            string full = VocabularyWindowsFile.FullPath(path);
            using var handle = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(full),
                VocabularyWindowsFile.Native.Read, 1, IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting,
                VocabularyWindowsFile.Native.BackupSemantics | VocabularyWindowsFile.Native.OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid) throw Invalid();
            if (VocabularyWindowsFile.Native.GetFileType(handle) != 1) throw Oversized();
            if (!VocabularyWindowsFile.Native.GetAttributes(handle, 9, out var attributes,
                (uint)Marshal.SizeOf<VocabularyWindowsFile.FileAttributesInfo>())) throw Invalid();
            // Mac URL regular-file admission rejects a symbolic leaf before boundedRead. Reject
            // Windows directory/reparse leaves with that same typed non-regular-source failure.
            if ((attributes.Attributes & (0x10u | 0x400u)) != 0) throw Oversized();
            _ = VocabularyWindowsFile.Identity(handle);
            long length = RandomAccess.GetLength(handle); int maximum = VocabularyRestorePolicy.MaximumBackupBytes;
            if (length < 0 || length > maximum) throw Oversized();
            using var output = new MemoryStream((int)length); byte[] buffer = new byte[65536]; int total = 0;
            while (true)
            {
                int read = RandomAccess.Read(handle, buffer.AsSpan(0, Math.Min(buffer.Length, maximum + 1 - total)), total);
                if (read == 0) break;
                total += read;
                if (total > maximum) throw Oversized();
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException) { throw Invalid(); }
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidArchive);
    private static VocabularyException Oversized() => new(VocabularyFailure.OversizedArchive);
}
