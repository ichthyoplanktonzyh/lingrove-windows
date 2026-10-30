using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;

namespace Cotabby.App.UI.Reading;

/// <summary>One explicit region gesture inside the permitted physical capture surface.
/// This disposable view owns no screen pixels and cannot authorize reading a region.</summary>
internal sealed class ReadingRegionPicker : Window, IDisposable
{
    private readonly ScreenRect _bounds;
    private readonly TaskCompletionSource<ScreenRect?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly System.Windows.Shapes.Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Border _selection = new() { BorderBrush = SystemColors.HighlightBrush, BorderThickness = new Thickness(2), Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private Point? _start;
    private CancellationTokenRegistration _registration;
    private bool _finished;
    internal ReadingRegionPicker(ScreenRect bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
            || bounds.X < int.MinValue || bounds.Y < int.MinValue || bounds.Right > int.MaxValue || bounds.Bottom > int.MaxValue
            || bounds.IsEmpty || bounds.Width < 4 || bounds.Height < 4 || bounds.Width > 32768 || bounds.Height > 32768)
            throw new ArgumentException("The screen capture area is unavailable.");
        _bounds = bounds; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; AllowsTransparency = true; Background = Brushes.Transparent;
        Width = bounds.Width; Height = bounds.Height; Cursor = Cursors.Cross;
        _canvas.Children.Add(_shade); _canvas.Children.Add(_selection);
        _canvas.SizeChanged += (_, _) => UpdateShade();
        var instructions = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 25, 25, 25)),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), IsHitTestVisible = false,
            Child = new TextBlock { Text = "Select a region to read · Esc or right-click to cancel\nWindows OCR runs on this device; screen pixels are not sent to a model.",
                Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), FontSize = 13, TextWrapping = TextWrapping.Wrap } };
        Canvas.SetLeft(instructions, 18); Canvas.SetTop(instructions, 18); _canvas.Children.Add(instructions); Content = _canvas;
        SourceInitialized += (_, _) => LearningPanelPlacement.Install(this);
        _canvas.MouseLeftButtonDown += (_, e) => { Begin(_canvas.PointToScreen(e.GetPosition(_canvas))); if (!_canvas.CaptureMouse()) Finish(null); e.Handled = true; };
        _canvas.MouseMove += (_, e) => { if (_start is not null) Update(_canvas.PointToScreen(e.GetPosition(_canvas))); };
        _canvas.MouseLeftButtonUp += (_, e) => { End(_canvas.PointToScreen(e.GetPosition(_canvas))); _canvas.ReleaseMouseCapture(); e.Handled = true; };
        _canvas.MouseRightButtonDown += (_, e) => { Finish(null); e.Handled = true; };
        _canvas.LostMouseCapture += (_, _) => { if (_start is not null && !_finished) Finish(null); };
        Closed += (_, _) => { if (!_finished) { _finished = true; _registration.Dispose(); _result.TrySetResult(null); } };
    }
    internal Task<ScreenRect?> SelectAsync(CancellationToken token)
    {
        Dispatcher.VerifyAccess(); token.ThrowIfCancellationRequested();
        _registration = token.Register(() =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try { Dispatcher.BeginInvoke(() => Finish(null)); }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { }
        });
        if (token.IsCancellationRequested) { Finish(null); return _result.Task; }
        Show(); var hwnd = new WindowInteropHelper(this).Handle;
        if (!SetWindowPos(hwnd, new nint(-1), checked((int)_bounds.X), checked((int)_bounds.Y), checked((int)_bounds.Width), checked((int)_bounds.Height), 0x0010 | 0x0040)) Finish(null);
        return _result.Task;
    }
    internal void Begin(Point physical) { if (!_finished) _start = Clamp(physical); }
    internal void Update(Point physical)
    {
        if (_finished || _start is not { } start) return;
        var end = Clamp(physical); var a = _canvas.PointFromScreen(start); var b = _canvas.PointFromScreen(end);
        Canvas.SetLeft(_selection, Math.Min(a.X, b.X)); Canvas.SetTop(_selection, Math.Min(a.Y, b.Y));
        _selection.Width = Math.Abs(a.X - b.X); _selection.Height = Math.Abs(a.Y - b.Y); _selection.Visibility = Visibility.Visible;
        UpdateShade();
    }
    private void UpdateShade()
    {
        var surface = new RectangleGeometry(new Rect(0, 0, Math.Max(0, _canvas.ActualWidth), Math.Max(0, _canvas.ActualHeight)));
        _shade.Data = _selection.Visibility != Visibility.Visible ? surface : new CombinedGeometry(GeometryCombineMode.Exclude, surface,
            new RectangleGeometry(new Rect(Canvas.GetLeft(_selection), Canvas.GetTop(_selection), _selection.Width, _selection.Height)));
    }
    internal void End(Point physical)
    {
        if (_finished || _start is not { } start) return;
        var end = Clamp(physical); double x = Math.Min(start.X, end.X), y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(start.X - end.X), height = Math.Abs(start.Y - end.Y);
        Finish(width >= 4 && height >= 4 ? new ScreenRect(x, y, width, height) : null);
    }
    private Point Clamp(Point value) => new(Math.Clamp(Math.Round(value.X), _bounds.X, _bounds.Right), Math.Clamp(Math.Round(value.Y), _bounds.Y, _bounds.Bottom));
    private void Finish(ScreenRect? region)
    {
        if (_finished) return; _finished = true; _registration.Dispose();
        if (IsVisible) Hide(); _result.TrySetResult(region); Close();
    }
    public void Dispose() { Dispatcher.VerifyAccess(); Finish(null); }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
