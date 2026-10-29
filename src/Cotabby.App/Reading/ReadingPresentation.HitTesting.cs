using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Cotabby.App.Reading;

internal sealed partial class ReadingPresentation
{
    private sealed record HitBounds(int Left, int Top, int Right, int Bottom);
    private HitBounds? _readingBounds, _dictionaryBounds;
    private void InitializeHitTesting()
    {
        _reading.LocationChanged += (_, _) => UpdateBounds(); _reading.SizeChanged += (_, _) => UpdateBounds();
        _reading.IsVisibleChanged += (_, _) => UpdateBounds();
        _dictionary.LocationChanged += (_, _) => UpdateBounds(); _dictionary.SizeChanged += (_, _) => UpdateBounds();
        _dictionary.IsVisibleChanged += (_, _) => UpdateBounds();
    }
    private void UpdateBounds()
    {
        Volatile.Write(ref _readingBounds, BoundsOf(_reading)); Volatile.Write(ref _dictionaryBounds, BoundsOf(_dictionary));
    }
    private static HitBounds? BoundsOf(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return window.IsVisible && hwnd != 0 && GetWindowRect(hwnd, out var rect)
            ? new(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    }
    internal bool Contains(int x, int y) => Contains(Volatile.Read(ref _readingBounds), x, y) || Contains(Volatile.Read(ref _dictionaryBounds), x, y);
    private static bool Contains(HitBounds? bounds, int x, int y) => bounds is not null && x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
}
