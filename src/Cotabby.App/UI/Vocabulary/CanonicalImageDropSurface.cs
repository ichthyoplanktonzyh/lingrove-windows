using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Cotabby.Win32.Vocabulary;
using Cotabby.App.Localization;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>A dedicated native drop surface. The external process owns the OLE target; this
/// WPF host owns placement and joins that process before its HWND is destroyed.</summary>
internal sealed class CanonicalImageDropSurface : HwndHost
{
    private readonly Action<byte[]> _received;
    private readonly Action _failed;
    private readonly Action<bool> _busy;
    private readonly Func<IntPtr, CancellationToken, Task<WindowsVocabularyOleDropTransfer>> _start;
    private readonly CancellationTokenSource _lifetime = new();
    private IntPtr _host;
    private WindowsVocabularyOleDropTransfer? _transfer;
    private Task? _loop, _retirement;
    internal CanonicalImageDropSurface(Action<byte[]> received, Action failed, Action<bool> busy, Func<IntPtr, CancellationToken, Task<WindowsVocabularyOleDropTransfer>>? start = null)
    {
        string label = InterfaceText.Get("Drop image here");
        _received = received; _failed = failed; _busy = busy; _start = start ?? ((parent, token) => WindowsVocabularyOleDropTransfer.StartAsync(parent, token, label)); Height = 42; MinWidth = 160;
        System.Windows.Automation.AutomationProperties.SetName(this, label);
        Loaded += (_, _) => { if (_loop is null && !_lifetime.IsCancellationRequested) _loop = RunAsync(); };
        SizeChanged += (_, _) => Position(); IsVisibleChanged += (_, _) => Position(); IsEnabledChanged += (_, _) => Position();
    }
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _host = CreateWindowExW(0, "STATIC", InterfaceText.Get("Image drop unavailable"), 0x50000001, 0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_host == IntPtr.Zero) throw new InvalidOperationException("Cannot create image drop surface.");
        return new HandleRef(this, _host);
    }
    protected override void DestroyWindowCore(HandleRef hwnd) { _ = DestroyWindow(hwnd.Handle); _host = IntPtr.Zero; }
    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                IntPtr parent = _host;
                var receiver = await Task.Run(() => _start(parent, _lifetime.Token));
                _transfer = receiver; Position();
                try
                {
                    Task first = await Task.WhenAny(receiver.DropStarted, receiver.Result);
                    if (first == receiver.DropStarted && !_lifetime.IsCancellationRequested) _busy(true);
                    byte[]? bytes = await receiver.Result;
                    _busy(false);
                    if (bytes is not null && !_lifetime.IsCancellationRequested && IsEnabled && IsVisible) _received(bytes);
                    else if (bytes is null && !_lifetime.IsCancellationRequested) _failed();
                }
                catch (Exception) when (!_lifetime.IsCancellationRequested && receiver.DropStarted.IsCompleted) { _failed(); }
                finally { _busy(false); _transfer = null; await receiver.DisposeAsync(); }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception) { if (!_lifetime.IsCancellationRequested) await Task.Delay(1000, _lifetime.Token); }
        }
    }
    private void Position()
    {
        if (_transfer is null || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return;
        var size = target.TransformToDevice.Transform(new Point(ActualWidth, ActualHeight));
        _transfer.Position(0, 0, Math.Max(1, (int)Math.Ceiling(size.X)), Math.Max(1, (int)Math.Ceiling(size.Y)), IsVisible && IsEnabled);
    }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _retirement = done.Task; _ = Drain(); return _retirement;
        async Task Drain()
        {
            Exception? failure = null;
            try { _lifetime.Cancel(); } catch (Exception error) { failure = error; }
            try { if (_transfer is { } transfer) await transfer.DisposeAsync(); } catch (Exception error) { failure ??= error; }
            try { if (_loop is not null) await _loop; } catch (OperationCanceledException) { } catch (Exception error) { failure ??= error; }
            _lifetime.Dispose(); if (failure is null) done.TrySetResult(); else done.TrySetException(failure);
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
}
