using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Cotabby.Tests.Shared;

/// <summary>Owned synthetic GDI pixels only. Never captures a screen or accesses a clipboard.</summary>
internal sealed class VocabularyBitmapFixture : IDisposable
{
    internal nint Handle { get; private set; }
    internal static readonly byte[] Pixels = [0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 0, 0,
        255, 255, 255, 0, 0, 0, 0, 255, 255, 0, 0, 0];
    internal VocabularyBitmapFixture(int width = 3, int height = 2, bool transferable = false)
    {
        if (transferable && (width != 3 || height != 2)) throw new ArgumentOutOfRangeException(nameof(width));
        byte[] info = new byte[40]; BinaryPrimitives.WriteUInt32LittleEndian(info, 40);
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(4), width); BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(8), -height);
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(12), 1); BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(14), 24);
        if (transferable)
        {
            // Use a compatible DDB for the cross-process OLE fixture. The DC
            // supplies device format only; every input pixel below is our synthetic array.
            nint dc = GetDC(0);
            try { Handle = CreateDIBitmap(dc, info, 4, Pixels, info, 0); }
            finally { if (dc != 0) _ = ReleaseDC(0, dc); }
            if (Handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            return;
        }
        Handle = CreateDIBSection(0, info, 0, out var pixels, 0, 0);
        if (Handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        if (width == 3 && height == 2) Marshal.Copy(Pixels, 0, pixels, Pixels.Length);
    }
    internal nint Transfer() { nint handle = Handle; Handle = 0; return handle; }
    // A null-output size query is not an object-liveness observation on Windows. Ask GDI
    // to actually read the object; product admission likewise requires a full BITMAP value.
    internal static bool Alive(nint handle) => GetObjectW(handle, 32, new byte[32]) == 32;
    public void Dispose() { if (Handle != 0) { _ = DeleteObject(Handle); Handle = 0; } }
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateDIBSection(nint dc, byte[] info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateDIBitmap(nint dc, byte[] header, uint flags, byte[] pixels, byte[] info, uint usage);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetDC(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int DeleteObject(nint value);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int GetObjectW(nint value, int count, [Out] byte[] output);
}
