using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>CF_BITMAP/TYMED_GDI adapter used only inside the killable image-transfer helper.
/// The clipboard or STGMEDIUM owns the HBITMAP throughout this synchronous copy. We never
/// select, delete, dereference its bmBits pointer or pass it to the app. Only our memory DC is
/// owned here. Standard CF_BITMAP has no alpha/profile contract; request opaque 24-bit RGB,
/// then use the existing bounded DIB envelope and WIC/Store normalization path.</summary>
internal static partial class VocabularyClipboardBitmap
{
    internal static unsafe byte[] Read(nint bitmap)
    {
        if (bitmap == 0 || GetObjectW(bitmap, sizeof(Bitmap), out var description) != sizeof(Bitmap)
            || description.Type != 0 || description.Width is <= 0 or > 30000 || description.Height is <= 0 or > 30000
            || (long)description.Width * description.Height > 100000000) throw Invalid();
        long stride = ((description.Width * 24L + 31) / 32) * 4;
        long pixels = stride * description.Height;
        if (pixels > VocabularyClipboardImageReader.MaximumBytes - 54) throw Invalid();
        var info = new BitmapInfo { Header = new Header { Size = 40, Width = description.Width, Height = -description.Height,
            Planes = 1, Bits = 24, ImageBytes = (uint)pixels } };
        byte[] dib = new byte[checked(40 + (int)pixels)];
        using (var dc = CreateCompatibleDC(0))
        {
            if (dc.IsInvalid) throw Invalid();
            fixed (byte* output = dib)
                if (GetDIBits(dc, bitmap, 0, (uint)description.Height, output + 40, ref info, 0) != description.Height) throw Invalid();
        }
        if (info.Header.Size != 40 || info.Header.Width != description.Width || info.Header.Height != -description.Height
            || info.Header.Planes != 1 || info.Header.Bits != 24 || info.Header.Compression != 0 || info.Header.Colors != 0
            || info.Header.ImageBytes != 0 && info.Header.ImageBytes != pixels) throw Invalid();
        // Publish our own fixed header, never driver/provider metadata or native pointers.
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), description.Width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -description.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), (uint)pixels);
        int rowBytes = description.Width * 3, padding = (int)stride - rowBytes;
        if (padding != 0) for (int row = 0; row < description.Height; row++) dib.AsSpan(40 + row * (int)stride + rowBytes, padding).Clear();
        return VocabularyClipboardDib.Wrap(dib, requiresV5: false);
    }
    private static InvalidDataException Invalid() => new("The bitmap image could not be verified.");
    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap { internal int Type, Width, Height, WidthBytes; internal ushort Planes, Bits; internal nint Data; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, Bits;
        internal uint Compression, ImageBytes; internal int XResolution, YResolution; internal uint Colors, ImportantColors;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal Header Header; internal uint Color; }
    private sealed class MemoryDc() : SafeHandleZeroOrMinusOneIsInvalid(true)
    { protected override bool ReleaseHandle() => DeleteDC(handle) != 0; }
    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetObjectW(nint bitmap, int size, out Bitmap value);
    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial MemoryDc CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetDIBits(MemoryDc dc, nint bitmap, uint first, uint count, void* bits, ref BitmapInfo info, uint usage);
}
