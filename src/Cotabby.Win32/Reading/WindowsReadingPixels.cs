using System.Runtime.InteropServices;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.Win32.Reading;

/// <summary>One explicit physical-pixel crop. HWND visibility is a conservative admission check,
/// not proof against all DWM/GPU overlays or a substitute for the retained field receipt.</summary>
internal static class WindowsReadingPixels
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }
    private delegate bool EnumProc(nint hwnd, nint state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumProc callback, nint state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(in Rect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, in BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    internal static (ReadingBitmap Bitmap, ScreenRect Display)? Capture(nint host, nint fieldWindow, ScreenRect field, ScreenRect region, Func<bool> permit)
    {
        if (!permit() || !IntegralRect(region, out var crop) || DwmFlush() < 0 || !permit()) return null;
        var monitor = MonitorFromRect(in crop, 2); var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info) || !permit()) return null;
        var display = new ScreenRect(info.Monitor.Left, info.Monitor.Top, (double)info.Monitor.Right - info.Monitor.Left, (double)info.Monitor.Bottom - info.Monitor.Top);
        if (!ReadingRegionPolicy.Contains(field, display, region) || !Unobscured(host, fieldWindow, crop, permit)) return null;
        int width = crop.Right - crop.Left, height = crop.Bottom - crop.Top;
        var bitmapInfo = new BitmapInfo { Header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 } };
        nint screen = 0, memory = 0, bitmap = 0, previous = 0; byte[]? pixels = null;
        try
        {
            if (!permit() || (screen = GetDC(0)) == 0 || !permit() || (memory = CreateCompatibleDC(screen)) == 0 || !permit()) return null;
            bitmap = CreateDIBSection(screen, in bitmapInfo, 0, out nint bits, 0, 0);
            if (bitmap == 0 || bits == 0 || !permit() || (previous = SelectObject(memory, bitmap)) == 0 || previous == -1 || !permit()) return null;
            if (!BitBlt(memory, 0, 0, width, height, screen, crop.Left, crop.Top, 0x40CC0020) || !permit()) return null;
            pixels = new byte[checked(width * height * 4)]; Marshal.Copy(bits, pixels, 0, pixels.Length);
            if (!permit() || !Unobscured(host, fieldWindow, crop, permit)) return null;
            var result = new ReadingBitmap(width, height, pixels); pixels = null; return (result, display);
        }
        finally
        {
            if (pixels is not null) Array.Clear(pixels);
            if (previous != 0 && previous != -1 && memory != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap); if (memory != 0) DeleteDC(memory); if (screen != 0) ReleaseDC(0, screen);
        }
    }

    private static bool IntegralRect(ScreenRect region, out Rect result)
    {
        result = default;
        if (!double.IsFinite(region.X) || !double.IsFinite(region.Y) || !double.IsFinite(region.Right) || !double.IsFinite(region.Bottom)
            || region.X != Math.Truncate(region.X) || region.Y != Math.Truncate(region.Y)
            || region.Right != Math.Truncate(region.Right) || region.Bottom != Math.Truncate(region.Bottom)
            || region.X < int.MinValue || region.Y < int.MinValue || region.Right > int.MaxValue || region.Bottom > int.MaxValue
            || region.Width < 4 || region.Height < 4 || region.Width * region.Height > 16_000_000) return false;
        result = new() { Left = (int)region.X, Top = (int)region.Y, Right = (int)region.Right, Bottom = (int)region.Bottom }; return true;
    }
    private static bool Overlap(Rect a, Rect b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
    private static bool Valid(Rect value) => value.Right > value.Left && value.Bottom > value.Top;
    private static bool Unobscured(nint host, nint field, Rect crop, Func<bool> permit)
    {
        if (host == 0 || field == 0 || !IsWindowVisible(host) || !IsWindowVisible(field) || !permit()) return false;
        bool safe = true, foundHost = false;
        EnumProc top = (window, _) =>
        {
            try
            {
                if (!permit()) { safe = false; return false; }
                if (window == host) { foundHost = true; return false; }
                if (!IsWindowVisible(window)) return true;
                if (!GetWindowRect(window, out var rect) || !Valid(rect) || Overlap(rect, crop)) { safe = false; return false; }
                return true;
            }
            catch { safe = false; return false; }
        };
        EnumWindows(top, 0);
        if (!safe || !foundHost || !permit()) return false;
        EnumProc child = (window, _) =>
        {
            try
            {
                if (!permit()) { safe = false; return false; }
                if (window == field || IsChild(window, field) || !IsWindowVisible(window)) return true;
                if (!GetWindowRect(window, out var rect) || !Valid(rect) || Overlap(rect, crop)) { safe = false; return false; }
                return true;
            }
            catch { safe = false; return false; }
        };
        if (!EnumChildWindows(host, child, 0) && safe) return false;
        return safe && permit();
    }
}
