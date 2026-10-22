using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Cotabby.App.UI;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.App.UI.Learning;

/// <summary>
/// Displays one readonly translation result beside the exact editor caret that supplied it. The
/// WPF window owns presentation only; its controller retains the field snapshot, request identity
/// and cancellation, just as the Mac coordinator owns capture while LearningSentencePanel renders.
/// </summary>
/// <remarks>
/// <c>WS_EX_NOACTIVATE</c> keeps typing in the host field while buttons remain clickable. UIA
/// rectangles are physical pixels, so placement uses SetWindowPos rather than WPF's primary-screen
/// DIPs; the caret is never rediscovered after the request starts.
/// </remarks>
public partial class LearningPreviewWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly IntPtr HwndTopmost = new(-1);

    private HwndSource? _source;

    public LearningPreviewWindow()
    {
        InitializeComponent();
        WindowTheme.Follow(this);
        SourceInitialized += OnSourceInitialized;
    }

    public event Action? RetryRequested;
    public event Action? CancelRequested;
    public event Action? DismissRequested;

    /// <summary>Shows a coordinator snapshot while leaving the host application active.</summary>
    public void ShowState(LearningPreviewState state, ScreenRect anchor, bool canRetry)
    {
        Dispatcher.VerifyAccess();
        SourceSection.Visibility = Visibility.Visible;
        SourceText.Text = state.Source;
        DirectionText.Text = state.Options is { } options
            ? $"{options.SourceLanguage.Label()}  →  {options.TargetLanguage.Label()}"
            : "";
        DirectionText.Visibility = DirectionText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool loading = state.Status == LearningPreviewStatus.Loading;
        bool ready = state.Status == LearningPreviewStatus.Ready && state.Result is not null;
        TitleText.Text = "Sentence translation";
        StatusText.Text = loading ? "Translating…" : ready ? "Translation preview" : "Could not translate";
        Progress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ResultSection.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        ResultText.Text = ready ? state.Result ?? string.Empty : string.Empty;
        ErrorText.Text = ready ? "" : state.ErrorMessage ?? "";
        ErrorText.Visibility = ErrorText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = !loading && !ready && canRetry ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = loading ? "Cancel" : "Close";

        PresentAt(anchor);
    }

    /// <summary>Shows a fixed capture-policy error when no sentence request could be created.</summary>
    public void ShowCaptureFailure(string message, ScreenRect anchor)
    {
        Dispatcher.VerifyAccess();
        SourceSection.Visibility = Visibility.Collapsed;
        SourceText.Text = string.Empty;
        DirectionText.Visibility = Visibility.Collapsed;
        ResultSection.Visibility = Visibility.Collapsed;
        ResultText.Text = string.Empty;
        Progress.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        TitleText.Text = "Sentence preview unavailable";
        StatusText.Text = "No text was sent.";
        CancelButton.Content = "Close";
        PresentAt(anchor);
    }

    /// <summary>Hides without activating the WPF window or changing the host's input focus.</summary>
    public void HidePreview()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HidePreview);
            return;
        }

        Hide();
    }

    private void PresentAt(ScreenRect anchor)
    {
        if (!IsVisible) Show();
        Visibility = Visibility.Visible;
        UpdateLayout();
        PositionAt(anchor);
    }

    private void PositionAt(ScreenRect anchor)
    {
        if (_source?.CompositionTarget is not { } target) return;

        double scaleX = target.TransformToDevice.M11;
        double scaleY = target.TransformToDevice.M22;
        double width = Math.Ceiling(ActualWidth * scaleX);
        double height = Math.Ceiling(ActualHeight * scaleY);
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

        SetWindowPos(_source.Handle, HwndTopmost, x, y, (int)width, (int)height,
            SwpNoActivate | SwpShowWindow);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        int style = GetWindowLong(_source.Handle, GwlExStyle);
        SetWindowLong(_source.Handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();

    private void OnCancelClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => DismissRequested?.Invoke();

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
