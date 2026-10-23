using System.IO;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Worker-only counterpart of Mac prepareImage(at:)/boundedRead for an explicitly chosen source.
/// Source images need not have notebook-private ACLs. One regular no-follow file handle owns the
/// read, with a byte ceiling enforced again during IO; paths and source metadata never enter the
/// resulting reference. Unlike media storage this boundary creates or changes no file/directory.
/// </summary>
internal static class WindowsVocabularyImageInput
{
    internal static byte[] ReadFile(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string full = VocabularyWindowsFile.FullPath(path);
            using var handle = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(full),
                VocabularyWindowsFile.Native.Read, 7, IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting,
                VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid) throw Invalid();
            _ = VocabularyWindowsFile.Identity(handle);
            long length = RandomAccess.GetLength(handle); int maximum = WindowsVocabularyImagePreparer.MaximumInputBytes;
            if (length < 0 || length > maximum) throw Invalid();
            using var output = new MemoryStream((int)length); byte[] buffer = new byte[65536]; int total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(handle, buffer.AsSpan(0, Math.Min(buffer.Length, maximum + 1 - total)), total);
                if (read == 0) break;
                total += read;
                if (total > maximum) throw new VocabularyException(VocabularyFailure.OversizedArchive);
                output.Write(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested(); return output.ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (VocabularyException error) when (error.Failure == VocabularyFailure.InvalidArchive) { throw Invalid(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { throw Invalid(); }
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
}
