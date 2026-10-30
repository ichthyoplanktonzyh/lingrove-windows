using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Reading;

/// <summary>Nonactivating selection affordance. The cached physical rectangle permits cheap
/// mouse-hook ownership decisions without reading WPF from the hook thread.</summary>
internal sealed class ReadingAffordanceWindow : Window
{
    private sealed record Bounds(int Left, int Top, int Right, int Bottom);
    private Bounds? _bounds;
    private Action? _activate;
    internal ReadingAffordanceWindow()
    {
        Width = 36; Height = 36; ShowActivated = false; ShowInTaskbar = false; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; Topmost = true; Background = Brushes.Transparent; AllowsTransparency = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        WindowTheme.Follow(this);
        var button = new Button { Content = "文", FontSize = 16, Focusable = false, ToolTip = InterfaceText.Get("Read selected text", "Learning") };
        button.SetResourceReference(StyleProperty, "Btn"); button.Click += (_, _) => _activate?.Invoke(); Content = button;
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
    }
    internal void ShowAt(int x, int y, Action activate)
    {
        _activate = activate; Show(); UpdateLayout(); LearningPanelPlacement.Position(this, new ScreenRect(x, y, 1, 1));
        if (GetWindowRect(new WindowInteropHelper(this).Handle, out var rect))
            Volatile.Write(ref _bounds, new(rect.Left, rect.Top, rect.Right, rect.Bottom));
    }
    internal void HideAffordance() { Volatile.Write(ref _bounds, null); _activate = null; Hide(); }
    internal bool Contains(int x, int y)
    {
        var b = Volatile.Read(ref _bounds);
        return b is not null && x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
}
