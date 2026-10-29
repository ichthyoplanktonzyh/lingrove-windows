using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Models;

/// <summary>Unlinks one regular app-owned model through its opened handle. Every directory
/// component is pinned against rename and must not be a reparse point; a path substitution
/// cannot redirect the final unlink into an external folder.</summary>
public static class WindowsModelFile
{
    public static void Delete(string directory, string filename)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(filename) || filename.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || !string.Equals(Path.GetFileName(filename), filename, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(filename), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A model filename is required.", nameof(filename));
        string fullDirectory = Path.GetFullPath(directory);
        var ancestors = new Stack<string>();
        for (string? current = fullDirectory; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        var pins = new List<SafeFileHandle>();
        try
        {
            foreach (var path in ancestors)
            {
                var handle = Open(path, ReadAttributes, BackupSemantics | OpenReparsePoint);
                pins.Add(handle);
                var flags = Attributes(handle);
                if ((flags & (DirectoryFlag | ReparseFlag)) != DirectoryFlag) throw new IOException("Model directory is not a regular directory.");
            }
            using var leaf = Open(Path.Combine(fullDirectory, filename), DeleteAccess | ReadAttributes, OpenReparsePoint);
            if ((Attributes(leaf) & (DirectoryFlag | ReparseFlag)) != 0) throw new IOException("Model is not a regular file.");
            var disposition = new FileDisposition { DeleteFile = 1 };
            if (!SetFileInformationByHandle(leaf, 4, ref disposition, (uint)Marshal.SizeOf<FileDisposition>())) ThrowNative();
        }
        finally { foreach (var pin in pins.AsEnumerable().Reverse()) pin.Dispose(); }
    }

    private static SafeFileHandle Open(string path, uint access, uint flags)
    {
        var handle = CreateFileW(path, access, 3, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose(); ThrowNative(); throw new InvalidOperationException();
    }
    private static uint Attributes(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) ThrowNative();
        return info.Attributes;
    }
    private static void ThrowNative() => throw new Win32Exception(Marshal.GetLastWin32Error());
    private const uint ReadAttributes = 0x80, DeleteAccess = 0x10000, DirectoryFlag = 0x10, ReparseFlag = 0x400;
    private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { public byte DeleteFile; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDisposition information, uint size);
}
