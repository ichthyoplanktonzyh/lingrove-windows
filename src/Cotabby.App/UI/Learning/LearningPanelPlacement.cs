using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Cotabby.Core.Focus;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Shared native no-activation/physical-pixel placement for the sentence and dictionary panels.
/// Each WPF window owns its handle/lifetime; this helper owns no state and never reads UIA or
/// activates the source editor. Its work-area/DPI rules preserve the existing preview placement.
/// </summary>
internal static partial class LearningPanelPlacement
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly IntPtr HwndTopmost = new(-1);

    public static void Install(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    public static void Position(Window window, ScreenRect anchor)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source || source.CompositionTarget is not { } target) return;

        double scaleX = target.TransformToDevice.M11;
        double scaleY = target.TransformToDevice.M22;
        double width = Math.Ceiling(window.ActualWidth * scaleX);
        double height = Math.Ceiling(window.ActualHeight * scaleY);
        int x;
        int y;
        if (anchor.IsEmpty)
        {
            // Capture-policy failures have no trustworthy editor anchor, so keep the status card
            // centered on the primary monitor without consulting another process's UIA element.
            x = (int)Math.Max(0, (SystemParameters.PrimaryScreenWidth * scaleX - width) / 2);
            y = (int)Math.Max(0, (SystemParameters.PrimaryScreenHeight * scaleY - height) / 2);
            anchor = new ScreenRect(x, y, 1, 1);
        }
        else
        {
            x = (int)anchor.X;
            y = (int)(anchor.Bottom + 8);
        }

        var monitor = MonitorFromPoint(new NativePoint { X = x, Y = (int)anchor.Y }, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            var work = info.Work;
            if (x + width > work.Right) x = work.Right - (int)width - 2;
            if (x < work.Left) x = work.Left + 2;
            if (y + height > work.Bottom) y = (int)anchor.Y - (int)height - 2;
            if (y < work.Top) y = work.Top + 2;
        }

        SetWindowPos(source.Handle, HwndTopmost, x, y, (int)width, (int)height,
            SwpNoActivate | SwpShowWindow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
