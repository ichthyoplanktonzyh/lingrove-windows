using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Dictionary;

/// <summary>One synchronous dictionary operation owns a local, no-reparse directory capability.
/// DOS mappings are checked before filesystem IO. Subsequent names and enumeration are relative
/// to native handles, never a re-opened pathname. Ordinary dictionary ACLs are accepted; this is
/// not notebook storage. File leases deny write/delete only during an operation, while cached
/// parser streams use sharing and are re-admitted by identity/change stamp on the next operation.</summary>
internal sealed partial class WindowsDictionaryDirectory : IDisposable
{
    private readonly SafeFileHandle _handle;
    internal string Path { get; }
    internal FileIdentity Identity { get; }
    private WindowsDictionaryDirectory(SafeFileHandle handle, string path)
    { _handle = handle; Path = path; Check(handle, directory: true); Identity = Id(handle); }

    internal static string Normalize(string path)
    {
        if (path is null || path.Length > 32760 || path.Any(char.IsControl)) throw Invalid();
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\') throw Invalid();
        if (path.AsSpan(2).Contains(':')) throw Invalid();
        string full = System.IO.Path.GetFullPath(path);
        return full.Length == 3 ? full : full.TrimEnd('\\');
    }
    internal static WindowsDictionaryDirectory Open(string path)
    {
        string full = Normalize(path); string drive = full[..2];
        string device = Device(drive);
        // Refuse redirectors, UNC, SUBST and unknown device aliases before opening a path.
        if (!LocalDevice(device)) throw Invalid();
        var volume = CreateFileW(@"\\?\GLOBALROOT" + device + "\\", 0x100081, 3, 0, 3, 0x02200000, 0);
        bool transferred = false;
        try
        {
            if (volume.IsInvalid) throw Invalid(unchecked((uint)Marshal.GetLastPInvokeError()));
            Check(volume, directory: true);
            if (full.Length == 3) { var result = new WindowsDictionaryDirectory(volume, full); transferred = true; return result; }
            var folder = OpenRelative(volume, full[3..], directory: true, cache: false) ?? throw Invalid();
            try { return new(folder, full); } catch { folder.Dispose(); throw; }
        }
        finally { if (!transferred) volume.Dispose(); }
    }
    internal FileStream? OpenFile(string relative, bool cache = false)
    {
        string key = Relative(relative);
        var handle = OpenRelative(_handle, key, directory: false, cache);
        if (handle is null) return null;
        try { Check(handle, directory: false); return new FileStream(handle, FileAccess.Read, 4096, isAsync: false); }
        catch { handle.Dispose(); throw; }
    }
    internal IEnumerable<(string Name, bool Directory)> Entries(CancellationToken token)
    {
        bool restart = true;
        while (ReadDirectoryBlock(restart) is { } data)
        {
            restart = false; int offset = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (offset > data.Length - 68) throw Invalid();
                uint next = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
                uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 56));
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 60));
                if (count == 0 || (count & 1) != 0 || count > data.Length - offset - 68 || next != 0 && (next < 68 + count || (next & 7) != 0 || next > data.Length - offset)) throw Invalid();
                string name = Encoding.Unicode.GetString(data, offset + 68, (int)count);
                if (name is not ("." or "..")) yield return (name, (attributes & 0x10) != 0);
                if (next == 0) break;
                offset += (int)next;
            }
        }
    }
    private unsafe byte[]? ReadDirectoryBlock(bool restart)
    {
        byte[] data = new byte[65536];
        fixed (byte* output = data)
            if (GetInfo(_handle, restart ? 15 : 14, output, (uint)data.Length) == 0)
            {
                int error = Marshal.GetLastPInvokeError(); if (error == 18) return null;
                throw Invalid(unchecked((uint)error));
            }
        return data;
    }
    internal static FileStamp Stamp(FileStream stream)
    {
        Check(stream.SafeFileHandle, directory: false); var basic = Basic(stream.SafeFileHandle);
        return new(Id(stream.SafeFileHandle), stream.Length, basic.Written, basic.Changed);
    }
    private static string Relative(string name)
    {
        if (name.Length == 0 || name.Length > 32760 || name[0] is '/' or '\\' || name.Contains(':') || name.Any(char.IsControl)) throw Invalid();
        var components = name.Replace('/', '\\').Split('\\').Where(part => part.Length != 0 && part != ".").ToArray();
        if (components.Length == 0 || components.Contains("..", StringComparer.Ordinal)) throw Invalid();
        return string.Join('\\', components);
    }
    private static unsafe SafeFileHandle? OpenRelative(SafeFileHandle root, string name, bool directory, bool cache)
    {
        // Every native name is exactly one component. OPEN_REPARSE_POINT therefore applies
        // to every descendant, and each directory is checked before it can authorize a child.
        // Do not depend on additional OBJECT_ATTRIBUTES flags for multi-component parsing.
        string[] parts = Relative(name).Split('\\'); var owned = new List<SafeFileHandle>();
        try
        {
            var current = root;
            for (int index = 0; index < parts.Length; index++)
            {
                bool childDirectory = index < parts.Length - 1 || directory;
                var next = OpenComponent(current, parts[index], childDirectory, cache && !childDirectory);
                if (next is null) return null;
                owned.Add(next); Check(next, childDirectory); current = next;
            }
            owned.RemoveAt(owned.Count - 1); return current;
        }
        finally { foreach (var file in owned.AsEnumerable().Reverse()) file.Dispose(); }
    }
    private static unsafe SafeFileHandle? OpenComponent(SafeFileHandle root, string name, bool directory, bool cache)
    {
        bool retained = false;
        try
        {
            root.DangerousAddRef(ref retained);
            fixed (char* text = name)
            {
                var unicode = new UnicodeString { Length = checked((ushort)(name.Length * 2)), Maximum = checked((ushort)(name.Length * 2)), Buffer = (nint)text };
                var attributes = new ObjectAttributes { Length = (uint)sizeof(ObjectAttributes), Root = root.DangerousGetHandle(), Name = (nint)(&unicode), Attributes = 0x40 };
                int status = NtCreateFile(out var file, directory ? 0x100081u : 0x100089u, &attributes, out _, 0, 0,
                    directory ? 3u : cache ? 7u : 1u, 1, 0x200020u | (directory ? 1u : 0x40u), 0, 0);
                if (status == 0) return file;
                file?.Dispose();
                if (status is unchecked((int)0xC0000034) or unchecked((int)0xC000003A)) return null;
                throw Invalid(unchecked((uint)status));
            }
        }
        finally { if (retained) root.DangerousRelease(); }
    }
    private static unsafe string Device(string drive)
    {
        char* target = stackalloc char[1024]; uint count = QueryDosDeviceW(drive, target, 1024);
        if (count == 0 || count > 1024) throw Invalid();
        int end = 0; while (end < count && target[end] != 0) end++;
        if (end == 0 || end == count) throw Invalid(); return new string(target, 0, end);
    }
    private static bool LocalDevice(string device)
    {
        foreach (string prefix in new[] { @"\Device\HarddiskVolume", @"\Device\CdRom", @"\Device\Floppy", @"\Device\Ramdisk" })
            if (device.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && device.Length > prefix.Length
                && device[prefix.Length..].All(char.IsAsciiDigit)) return true;
        return false;
    }
    private static void Check(SafeFileHandle file, bool directory)
    {
        uint flags = Basic(file).Attributes;
        if (GetFileType(file) != 1 || (flags & (0x10u | 0x400u)) != (directory ? 0x10u : 0)) throw Invalid();
    }
    private static unsafe BasicInfo Basic(SafeFileHandle file)
    { BasicInfo value; if (GetInfo(file, 0, &value, (uint)sizeof(BasicInfo)) == 0) throw Invalid(unchecked((uint)Marshal.GetLastPInvokeError())); return value; }
    private static unsafe FileIdentity Id(SafeFileHandle file)
    {
        FileIdentity value;
        if (GetInfo(file, 18, &value, (uint)sizeof(FileIdentity)) == 0) throw Invalid(unchecked((uint)Marshal.GetLastPInvokeError()));
        if (value.Low == 0 && value.High == 0) throw Invalid();
        return value;
    }
    internal static DictionaryFileException Invalid(uint error = 0) => new(error);
    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)] internal readonly record struct FileIdentity(ulong Volume, ulong Low, ulong High);
    internal readonly record struct FileStamp(FileIdentity Identity, long Size, long Modified, long Changed);
    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { internal long Created, Accessed, Written, Changed; internal uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { internal ushort Length, Maximum; internal nint Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { internal uint Length; internal nint Root, Name; internal uint Attributes; internal nint Security, Quality; }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { internal nint Status; internal nuint Information; }
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static unsafe partial uint QueryDosDeviceW(string drive, char* target, uint count);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static unsafe partial int GetInfo(SafeFileHandle file, int kind, void* value, uint bytes);
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint GetFileType(SafeFileHandle file);
    [LibraryImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static unsafe partial int NtCreateFile(out SafeFileHandle file, uint access, ObjectAttributes* attributes, out IoStatus status, nint allocation, uint fileAttributes, uint sharing, uint disposition, uint options, nint ea, uint eaBytes);
}

internal sealed class DictionaryFileException(uint error) : IOException("The dictionary file is invalid or unavailable.")
{ internal uint NativeError { get; } = error; }
