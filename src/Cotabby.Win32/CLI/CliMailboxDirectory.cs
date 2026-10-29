using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.CLI;

/// <summary>Private mailbox directory pinned by its real Windows identity. A stage is published
/// without replacement through its own handle; reads, claims and deletes use verified handles.
/// Neither a reparse leaf nor a replaced file may be followed or deleted by path.</summary>
internal sealed class CliMailboxDirectory : IDisposable
{
    private readonly VocabularyMediaDirectory _directory;
    internal string Path => _directory.Path;
    internal CliMailboxDirectory(string path, bool create = true) => _directory = VocabularyMediaDirectory.Open(path, create);
    internal void Verify() => _directory.Verify();
    internal IReadOnlyList<string> Names(IReadOnlySet<string>? excluded = null)
    {
        Verify(); var names = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path, "*.json", SearchOption.TopDirectoryOnly))
        {
            string name = System.IO.Path.GetFileName(path); if (name.Length != 41 || !Guid.TryParseExact(name[..36], "D", out _) || excluded?.Contains(name) == true) continue;
            names.Add(name); if (names.Count == 128) break;
        }
        Verify(); return names;
    }
    internal byte[]? Read(string name, int maximum = CliInputContract.MaximumBytes, VocabularyWindowsFile.FileIdentity? expected = null)
    {
        ValidateName(name); using var handle = _directory.OpenFile(name); if (handle is null) return null;
        if (expected is not null && VocabularyWindowsFile.Identity(handle) != expected) throw Invalid();
        using var stream = new FileStream(handle, FileAccess.Read); if (stream.Length < 0 || stream.Length > maximum) throw Invalid();
        using var output = new MemoryStream((int)stream.Length); var buffer = new byte[65536];
        while (true) { int read = stream.Read(buffer, 0, Math.Min(buffer.Length, maximum + 1 - (int)output.Length)); if (read == 0) break; output.Write(buffer, 0, read); if (output.Length > maximum) throw Invalid(); }
        Verify(); return output.ToArray();
    }
    internal bool Exists(string name) { ValidateName(name); using var handle = _directory.OpenFile(name); return handle is not null; }
    internal VocabularyWindowsFile.FileIdentity? CaptureIdentity(string name)
    { ValidateName(name); using var handle = _directory.OpenFile(name); return handle is null ? null : VocabularyWindowsFile.Identity(handle); }
    internal void RemoveIfOlderThan(string name, DateTime cutoff)
    {
        ValidateName(name); using var handle = _directory.OpenFile(name, delete: true); if (handle is null) return;
        if (File.GetLastWriteTimeUtc(handle) < cutoff) { Verify(); if (!SetFileInformationByHandle(handle, 4, [1], 1)) throw Invalid(); }
    }
    internal void Publish(string name, ReadOnlySpan<byte> bytes)
        => _ = PublishOwned(name, bytes);
    internal VocabularyWindowsFile.FileIdentity PublishOwned(string name, ReadOnlySpan<byte> bytes)
    {
        ValidateName(name); if (bytes.Length > CliInputContract.MaximumBytes) throw Invalid(); Verify();
        string staging = System.IO.Path.Combine(Path, ".cli-" + Guid.NewGuid().ToString("D")); FileStream? stream = null; bool published = false;
        try
        {
            stream = new FileInfo(staging).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 65536, FileOptions.None, VocabularyWindowsFile.NewFileSecurity());
            stream.Write(bytes); stream.Flush(true); var identity = VocabularyWindowsFile.Identity(stream.SafeFileHandle);
            Verify(); Rename(stream.SafeFileHandle, System.IO.Path.Combine(Path, name)); published = true; return identity;
        }
        finally { if (stream is not null) { if (!published) _ = SetFileInformationByHandle(stream.SafeFileHandle, 4, [1], 1); stream.Dispose(); } }
    }
    internal byte[]? Consume(string name, Func<byte[], bool>? admit = null)
    {
        ValidateName(name); using var handle = _directory.OpenFile(name, delete: true, reportSharingViolation: true); if (handle is null) return null;
        using var stream = new FileStream(handle, FileAccess.Read);
        if (stream.Length < 0 || stream.Length > CliInputContract.MaximumBytes) throw Invalid();
        using var output = new MemoryStream((int)stream.Length); var buffer = new byte[65536];
        while (true)
        {
            int count = stream.Read(buffer, 0, Math.Min(buffer.Length, CliInputContract.MaximumBytes + 1 - (int)output.Length));
            if (count == 0) break; output.Write(buffer, 0, count);
            if (output.Length > CliInputContract.MaximumBytes) throw Invalid();
        }
        var result = output.ToArray();
        if (admit is not null && !admit(result)) throw Invalid();
        Verify(); if (!SetFileInformationByHandle(handle, 4, [1], 1)) throw Invalid();
        return result;
    }
    internal bool Claim(string name, CliMailboxDirectory target)
        => ClaimOwned(name, target) is not null;
    internal VocabularyWindowsFile.FileIdentity? ClaimOwned(string name, CliMailboxDirectory target, VocabularyWindowsFile.FileIdentity? expected = null)
    {
        ValidateName(name); using var handle = _directory.OpenFile(name, delete: true, reportSharingViolation: true); if (handle is null) return null;
        var identity = VocabularyWindowsFile.Identity(handle);
        if (expected is not null && identity != expected) return null;
        Verify(); target.Verify(); Rename(handle, System.IO.Path.Combine(target.Path, name)); return identity;
    }
    internal bool RemoveOwned(string name, VocabularyWindowsFile.FileIdentity identity)
    {
        ValidateName(name); using var handle = _directory.OpenFile(name, delete: true); if (handle is null) return false;
        if (VocabularyWindowsFile.Identity(handle) != identity) return false;
        Verify(); if (!SetFileInformationByHandle(handle, 4, [1], 1)) throw Invalid(); return true;
    }
    internal void Remove(string name)
    {
        ValidateName(name); using var handle = _directory.OpenFile(name, delete: true); if (handle is null) return;
        Verify(); if (!SetFileInformationByHandle(handle, 4, [1], 1)) throw Invalid();
    }
    internal static void Rename(SafeFileHandle handle, string destination)
    {
        byte[] name = Encoding.Unicode.GetBytes(destination); int rootOffset = IntPtr.Size, lengthOffset = rootOffset + IntPtr.Size, nameOffset = lengthOffset + 4;
        byte[] info = new byte[Math.Max(nameOffset + name.Length + 2, IntPtr.Size == 8 ? 24 : 16)];
        // Zero ReplaceIfExists: an existing message is never overwritten by another request.
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(lengthOffset), (uint)name.Length); name.CopyTo(info, nameOffset);
        if (!SetFileInformationByHandle(handle, 3, info, (uint)info.Length)) throw new CliException(CliErrorCode.Io, "Cannot publish a CLI message.");
    }
    private static void ValidateName(string name) { if (name.Length != 41 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(name[..36], "D", out _)) throw Invalid(); }
    private static CliException Invalid() => new(CliErrorCode.Io, "Invalid private CLI mailbox file.");
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, [In] byte[] info, uint size);
    public void Dispose() => _directory.Dispose();
}
