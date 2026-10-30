using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using System.Windows.Automation;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Reading;

/// <summary>Nonactivating selection affordance. The cached physical rectangle permits cheap
/// mouse-hook ownership decisions without reading WPF from the hook thread.</summary>
internal sealed class ReadingAffordanceWindow : Window
{
    private sealed record Bounds(int Left, int Top, int Right, int Bottom);
    private Bounds? _bounds;
    private Action? _activate;
    private readonly Button _button;
    private readonly ProgressBar _progress = new() { Width = 18, Height = 18, IsIndeterminate = true, Focusable = false };
    private object? _progressOwner;
    internal ReadingAffordanceWindow()
    {
        Width = 36; Height = 36; ShowActivated = false; ShowInTaskbar = false; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; Topmost = true; Background = Brushes.Transparent; AllowsTransparency = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        WindowTheme.Follow(this);
        _progress.SetResourceReference(StyleProperty, "ReadingProgressRing");
        _button = new Button { Content = "文", FontSize = 16, Padding = new Thickness(4), Opacity = 1, VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = false, ToolTip = InterfaceText.Get("Read selected text", "Learning") };
        _button.SetResourceReference(StyleProperty, "Btn"); _button.Click += (_, _) => _activate?.Invoke(); Content = _button;
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
        Closed += (_, _) => { Volatile.Write(ref _bounds, null); _activate = null; _progressOwner = null; };
    }
    internal void ShowAt(int x, int y, Action activate)
    {
        _progressOwner = null; _button.Content = "文"; _button.IsEnabled = true;
        _button.ToolTip = InterfaceText.Get("Read selected text", "Learning"); AutomationProperties.SetName(_button, InterfaceText.Get("Read selected text", "Learning"));
        _activate = activate; Place(x, y);
    }
    internal void ShowProgress(ReadingSelection selection, ReadingVisualFrame frame, object owner)
    {
        _progressOwner = owner; _activate = null; _button.Content = _progress; _button.IsEnabled = false;
        _button.ToolTip = InterfaceText.Get("Preparing…", "Learning"); AutomationProperties.SetName(_button, InterfaceText.Get("Preparing…", "Learning"));
        var anchor = selection.SelectionBounds is { } selected && ReadingVisualPolicy.Valid(selected) ? selected : (ReadingVisualPolicy.Valid(selection.Anchor) ? selection.Anchor
            : new ScreenRect(frame.X, frame.Y, 1, 1));
        Place((int)Math.Clamp(anchor.Right + 10 * frame.Scale, int.MinValue, int.MaxValue), (int)Math.Clamp(anchor.Bottom + 10 * frame.Scale, int.MinValue, int.MaxValue));
        // Monitor-edge placement can fold the surface back over its anchor. Never obscure the
        // frozen point whose topmost native-window identity still guards this OCR operation.
        if (Contains((int)frame.X, (int)frame.Y)) HideProgress(owner);
    }
    internal void HideProgress(object owner) { if (ReferenceEquals(_progressOwner, owner)) HideAffordance(); }
    private void Place(int x, int y)
    {
        Show(); UpdateLayout(); LearningPanelPlacement.Position(this, new ScreenRect(x, y, 1, 1));
        if (GetWindowRect(new WindowInteropHelper(this).Handle, out var rect))
            Volatile.Write(ref _bounds, new(rect.Left, rect.Top, rect.Right, rect.Bottom));
        else HideAffordance();
    }
    internal void HideAffordance() { Volatile.Write(ref _bounds, null); _activate = null; _progressOwner = null; Hide(); }
    internal bool Contains(int x, int y)
    {
        var b = Volatile.Read(ref _bounds);
        return b is not null && x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
}
