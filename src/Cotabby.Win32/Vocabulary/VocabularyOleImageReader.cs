using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Cotabby.Win32.Clipboard;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Runs only inside the killable drop helper. Foreign COM calls, including release,
/// are never made on the application's dispatcher. The first selected representation owns
/// the operation; invalid or stalled data cannot fall through to a different source.</summary>
internal static class VocabularyOleImageReader
{
    private const int Maximum = VocabularyClipboardImageReader.MaximumBytes;
    internal static bool CanRead(IDataObject source)
    {
        foreach (short format in new short[] { 15, unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("FileGroupDescriptorW")),
            unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("PNG")), unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("image/png")), 6,
            unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("JFIF")), unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("JPEG")),
            unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("image/jpeg")), 17, 8 })
        {
            if (format == 0) continue;
            short descriptors = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("FileGroupDescriptorW"));
            TYMED kinds = format == 15 || format == descriptors || format is 6 or 17 or 8 ? TYMED.TYMED_HGLOBAL : TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTREAM;
            var request = new FORMATETC { cfFormat = format, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = kinds };
            if (source.QueryGetData(ref request) == 0) return true;
        }
        return false;
    }
    internal static byte[]? Read(IDataObject source)
    {
        if (TryRead(source, 15, -1, TYMED.TYMED_HGLOBAL, 1024 * 1024) is { } files) return ReadFirstFile(files);
        if (ReadVirtual(source) is { } virtualFile) return virtualFile;
        // Registered format names are fixed, not taken from a provider or an arbitrary path.
        foreach (string name in new[] { "PNG", "image/png" })
        {
            short format = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat(name));
            if (TryRead(source, format, -1, TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTREAM, Maximum) is { } bytes) return bytes;
        }
        if (TryRead(source, 6, -1, TYMED.TYMED_HGLOBAL, Maximum) is { } tiff) return tiff;
        foreach (string name in new[] { "JFIF", "JPEG", "image/jpeg" })
        {
            short format = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat(name));
            if (TryRead(source, format, -1, TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTREAM, Maximum) is { } bytes) return bytes;
        }
        foreach (short format in new short[] { 17, 8 })
            if (TryRead(source, format, -1, TYMED.TYMED_HGLOBAL, Maximum) is { } bytes)
                return VocabularyClipboardDib.Wrap(bytes, format == 17);
        return null;
    }
    private static byte[] ReadFirstFile(byte[] files)
    {
        if (files.Length < 24) throw Invalid();
        int offset = BinaryPrimitives.ReadInt32LittleEndian(files), wide = BinaryPrimitives.ReadInt32LittleEndian(files.AsSpan(16));
        if (offset < 20 || (offset & 1) != 0 || offset > files.Length - 4 || wide == 0) throw Invalid();
        int end = offset; while (end <= files.Length - 2 && (files[end] != 0 || files[end + 1] != 0)) end += 2;
        if (end == offset || end > files.Length - 2 || end - offset > 65534) throw Invalid();
        string path;
        try { path = new System.Text.UnicodeEncoding(false, false, true).GetString(files, offset, end - offset); }
        catch (ArgumentException) { throw Invalid(); }
        if (!Path.IsPathFullyQualified(path)) throw Invalid();
        path = Path.GetFullPath(path);
        if (!path.StartsWith("\\\\?\\", StringComparison.Ordinal)) path = path.StartsWith("\\\\", StringComparison.Ordinal) ? "\\\\?\\UNC\\" + path[2..] : "\\\\?\\" + path;
        using var handle = CreateFileW(path, 0x80000000, 7, IntPtr.Zero, 3, 0x00200000 | 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid || GetFileType(handle) != 1 || !GetFileInformationByHandleEx(handle, 9, out var attributes, 8)
            || (attributes.Attributes & (0x10u | 0x400u)) != 0) throw Invalid();
        long length = RandomAccess.GetLength(handle); if (length < 0 || length > Maximum) throw Invalid();
        using var output = new MemoryStream((int)length); byte[] buffer = new byte[65536]; int total = 0;
        while (true)
        {
            int count = RandomAccess.Read(handle, buffer.AsSpan(0, Math.Min(buffer.Length, Maximum + 1 - total)), total);
            if (count == 0) return output.ToArray(); total += count; if (total > Maximum) throw Invalid(); output.Write(buffer, 0, count);
        }
    }
    private static byte[]? ReadVirtual(IDataObject source)
    {
        short descriptor = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("FileGroupDescriptorW"));
        byte[]? group = TryRead(source, descriptor, -1, TYMED.TYMED_HGLOBAL, 1024 * 1024);
        if (group is null) return null;
        uint count = group.Length < 4 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(group);
        if (count == 0 || count > (group.Length - 4) / 592) throw Invalid();
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(group.AsSpan(4));
        if ((flags & 4) != 0 && (BinaryPrimitives.ReadUInt32LittleEndian(group.AsSpan(40)) & 0x10) != 0) throw Invalid();
        ulong size = ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(group.AsSpan(68)) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(group.AsSpan(72));
        if ((flags & 0x40) != 0 && size > Maximum) throw Invalid();
        int end = 76; while (end < 596 && (group[end] != 0 || group[end + 1] != 0)) end += 2;
        if (end == 76 || end >= 596) throw Invalid();
        string filename;
        try { filename = new System.Text.UnicodeEncoding(false, false, true).GetString(group, 76, end - 76); }
        catch (ArgumentException) { throw Invalid(); }
        if (!new[] { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp", ".heic", ".heif" }.Contains(Path.GetExtension(filename), StringComparer.OrdinalIgnoreCase)) throw Invalid();
        short contents = unchecked((short)ClipboardNativeMethods.RegisterClipboardFormat("FileContents"));
        return TryRead(source, contents, 0, TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTREAM, Maximum) ?? throw Invalid();
    }
    private static byte[]? TryRead(IDataObject source, short format, int index, TYMED kinds, int maximum)
    {
        if (format == 0) return null;
        var request = new FORMATETC { cfFormat = format, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = index, tymed = kinds };
        if (source.QueryGetData(ref request) != 0) return null;
        source.GetData(ref request, out var medium);
        try
        {
            if (medium.tymed == TYMED.TYMED_HGLOBAL)
            {
                nuint length = ClipboardNativeMethods.GlobalSize(medium.unionmember); if (length > (nuint)maximum) throw Invalid();
                byte[] bytes = new byte[(int)length]; if (bytes.Length == 0) return bytes;
                IntPtr pointer = ClipboardNativeMethods.GlobalLock(medium.unionmember); if (pointer == IntPtr.Zero) throw Invalid();
                try { Marshal.Copy(pointer, bytes, 0, bytes.Length); } finally { ClipboardNativeMethods.GlobalUnlock(medium.unionmember); }
                return bytes;
            }
            if (medium.tymed == TYMED.TYMED_ISTREAM && medium.unionmember != IntPtr.Zero)
            {
                var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                try
                {
                    using var result = new MemoryStream(); byte[] block = new byte[65536]; IntPtr count = Marshal.AllocCoTaskMem(sizeof(int));
                    try
                    {
                        while (true)
                        {
                            int want = Math.Min(block.Length, maximum + 1 - (int)result.Length); Marshal.WriteInt32(count, 0);
                            stream.Read(block, want, count); int read = Marshal.ReadInt32(count);
                            if (read < 0 || read > want) throw Invalid(); if (read == 0) break;
                            result.Write(block, 0, read); if (result.Length > maximum) throw Invalid();
                        }
                        return result.ToArray();
                    }
                    finally { Marshal.FreeCoTaskMem(count); }
                }
                finally { Marshal.ReleaseComObject(stream); }
            }
            throw Invalid();
        }
        finally { ReleaseStgMedium(ref medium); }
    }
    private static InvalidDataException Invalid() => new("The dropped image could not be verified.");
    [StructLayout(LayoutKind.Sequential)] private struct FileAttributesInfo { internal uint Attributes, ReparseTag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetFileType(Microsoft.Win32.SafeHandles.SafeFileHandle handle);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int kind, out FileAttributesInfo value, uint size);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
