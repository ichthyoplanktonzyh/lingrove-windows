using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Cotabby.Core.Vocabulary.EntryInput;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Read-only word-list effect. File type and initial size are checked on the opened
/// handle, not on a path that can be replaced. Links are followed; the target must be a regular
/// disk file. The extra byte detects growth after admission without retaining a file handle.</summary>
public sealed class WindowsVocabularyWordListReader : IVocabularyWordListReader
{
    private readonly Func<CancellationToken, Task>? _afterAdmission;
    public WindowsVocabularyWordListReader() { }
    internal WindowsVocabularyWordListReader(Func<CancellationToken, Task> afterAdmission) => _afterAdmission = afterAdmission;
    public Task<VocabularyWordList> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => ReadCoreAsync(path, cancellationToken), cancellationToken);

    private async Task<VocabularyWordList> ReadCoreAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\?\GLOBALROOT\", StringComparison.OrdinalIgnoreCase))
            throw new VocabularyWordListException(VocabularyWordListError.TooLarge);
        using var handle = CreateFileW(path, 0x80000000, 1 | 2 | 4, IntPtr.Zero, 3,
            0x40000000 | 0x02000000, IntPtr.Zero); // overlapped, backup semantics to inspect directories
        if (handle.IsInvalid) throw new IOException("Could not open the word list.", new Win32Exception(Marshal.GetLastWin32Error()));
        token.ThrowIfCancellationRequested();
        if (GetFileType(handle) != 1 || !GetFileInformationByHandleEx(handle, 1, out var info, (uint)Marshal.SizeOf<FileStandardInfo>())
            || !HasFilePath(handle) || info.Directory != 0 || info.EndOfFile < 0 || info.EndOfFile > VocabularyWordListPolicy.MaximumBytes)
            throw new VocabularyWordListException(VocabularyWordListError.TooLarge);
        if (_afterAdmission is not null) await _afterAdmission(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await using var stream = new FileStream(handle, FileAccess.Read, 65536, isAsync: true);
        using var buffer = new MemoryStream(); var chunk = new byte[65536];
        while (buffer.Length <= VocabularyWordListPolicy.MaximumBytes)
        {
            int allowed = (int)Math.Min(chunk.Length, VocabularyWordListPolicy.MaximumBytes + 1L - buffer.Length);
            int count = await stream.ReadAsync(chunk.AsMemory(0, allowed), token).ConfigureAwait(false);
            if (count == 0) break;
            buffer.Write(chunk, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return VocabularyWordListPolicy.Decode(buffer.ToArray(), Path.GetExtension(path).TrimStart('.'), token);
    }
    private static bool HasFilePath(SafeFileHandle handle)
    {
        var name = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, name, (uint)name.Capacity, 0);
        if (length == 0 || length >= name.Capacity) return false;
        string path = name.ToString();
        // A file must resolve to a DOS drive, UNC share or mounted volume path. Raw device
        // objects can report FILE_TYPE_DISK but are not regular filesystem files.
        return path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
            || (path.Length >= 7 && path.StartsWith(@"\\?\", StringComparison.Ordinal) && char.IsAsciiLetter(path[4]) && path[5] == ':' && path[6] == '\\');
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out FileStandardInfo info, uint size);
}
