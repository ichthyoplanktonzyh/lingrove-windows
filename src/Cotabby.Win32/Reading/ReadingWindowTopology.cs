using System.Runtime.InteropServices;
using Cotabby.Core.Focus;

namespace Cotabby.Win32.Reading;

/// <summary>Borrowed window/display geometry only. HWNDs never enter portable reading receipts.</summary>
internal static unsafe partial class ReadingWindowTopology
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private struct MonitorBounds { public Rect Bounds; public int Count; }
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint dc, Rect* clip,
        delegate* unmanaged<nint, nint, Rect*, nint, int> callback, nint state);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsChild(nint parent, nint child);
    internal static bool IsOriginDescendant(nint origin, nint child) => origin != 0 && child != 0 && (origin == child || IsChild(origin, child));
    internal static ScreenRect? WindowBounds(nint window) => window != 0 && GetWindowRect(window, out var r) && r.Right > r.Left && r.Bottom > r.Top
        ? new(r.Left, r.Top, (double)r.Right - r.Left, (double)r.Bottom - r.Top) : null;
    internal static ScreenRect DesktopBounds()
    {
        // Enumerate physical virtual-screen rectangles; GetSystemMetrics is not per-monitor DPI
        // aware. The stack state is borrowed only by this synchronous native enumeration.
        MonitorBounds state = default;
        if (!EnumDisplayMonitors(0, null, &IncludeMonitor, (nint)(&state)) || state.Count == 0) return default;
        return new(state.Bounds.Left, state.Bounds.Top, (double)state.Bounds.Right - state.Bounds.Left, (double)state.Bounds.Bottom - state.Bounds.Top);
    }
    [UnmanagedCallersOnly]
    private static int IncludeMonitor(nint monitor, nint dc, Rect* rectangle, nint data)
    {
        if (rectangle is null || data == 0) return 0;
        var state = (MonitorBounds*)data;
        if (state->Count >= 64 || rectangle->Right <= rectangle->Left || rectangle->Bottom <= rectangle->Top) return 0;
        if (state->Count++ == 0) state->Bounds = *rectangle;
        else
        {
            state->Bounds.Left = Math.Min(state->Bounds.Left, rectangle->Left); state->Bounds.Top = Math.Min(state->Bounds.Top, rectangle->Top);
            state->Bounds.Right = Math.Max(state->Bounds.Right, rectangle->Right); state->Bounds.Bottom = Math.Max(state->Bounds.Bottom, rectangle->Bottom);
        }
        return 1;
    }
}
