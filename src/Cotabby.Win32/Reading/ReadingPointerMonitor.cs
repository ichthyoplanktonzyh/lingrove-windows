using System.Runtime.InteropServices;
using Cotabby.Core.LanguageLearning.Reading;
using static Cotabby.Win32.Interop.NativeMethods;

namespace Cotabby.Win32.Reading;

/// <summary>Single observation-only mouse hook. Subscribers receive immutable metadata on the
/// hook thread and must only enqueue work; UIA, Dispatcher waits and input suppression are forbidden.</summary>
public sealed class ReadingPointerMonitor : IReadingPointerMonitor
{
    private const int WhMouseLl = 14, LeftDown = 0x0201, LeftUp = 0x0202, RightDown = 0x0204, Wheel = 0x020A, HWheel = 0x020E;
    private readonly object _gate = new();
    private Thread? _thread; private uint _threadId; private nint _hook; private LowLevelKeyboardProc? _callback;
    private bool _running, _stopping, _disposed; private uint _lastUp; private int _lastX, _lastY; private int _clickCount;
    public event Action<ReadingPointerEvent>? Pointer;
    [StructLayout(LayoutKind.Sequential)] private struct MouseInfo { public int X, Y; public uint Data, Flags, Time; public nint Extra; }
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG message, nint window, uint minimum, uint maximum, uint remove);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    public static (int X, int Y)? CurrentPosition() => GetCursorPos(out var point) ? (point.X, point.Y) : null;
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (_stopping)
            {
                if (Thread.CurrentThread == _thread) throw new InvalidOperationException("The pointer hook is stopping.");
                Monitor.Wait(_gate); ObjectDisposedException.ThrowIf(_disposed, this);
            }
            if (_running) return;
            using var ready = new ManualResetEventSlim(); Exception? failure = null;
            _running = true; _thread = new Thread(() =>
            {
                try
                {
                    _threadId = GetCurrentThreadId(); _callback = Callback;
                    _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
                    if (_hook == 0) throw new InvalidOperationException("Reading pointer observation is unavailable.");
                    // Create the thread queue before publishing readiness, so a concurrent Stop
                    // cannot lose WM_QUIT before GetMessage first creates the queue.
                    PeekMessage(out _, 0, 0, 0, 0);
                    ready.Set(); while (GetMessage(out MSG message, 0, 0, 0) > 0) { TranslateMessage(in message); DispatchMessage(in message); }
                }
                catch (Exception error) { failure = error; ready.Set(); }
                finally
                {
                    if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
                    lock (_gate) { _running = false; _stopping = false; _threadId = 0; _lastUp = 0; _clickCount = 0; Monitor.PulseAll(_gate); }
                }
            }) { IsBackground = true, Name = "Cotabby.ReadingPointer" };
            _thread.Start(); ready.Wait(); if (failure is not null) { _running = false; throw failure; }
        }
    }
    private nint Callback(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0 && data != 0 && (int)message is LeftDown or LeftUp or RightDown or Wheel or HWheel)
            {
                var value = Marshal.PtrToStructure<MouseInfo>(data);
                if ((value.Flags & 1) == 0)
                {
                    var kind = (int)message switch { LeftDown => ReadingPointerKind.LeftDown, LeftUp => ReadingPointerKind.LeftUp,
                        RightDown => ReadingPointerKind.RightDown, _ => ReadingPointerKind.Scroll };
                    if (kind == ReadingPointerKind.LeftUp)
                    {
                        bool same = unchecked(value.Time - _lastUp) <= GetDoubleClickTime()
                            && Math.Abs((long)value.X - _lastX) <= GetSystemMetrics(36) / 2
                            && Math.Abs((long)value.Y - _lastY) <= GetSystemMetrics(37) / 2;
                        _clickCount = same ? Math.Min(2, _clickCount + 1) : 1;
                        _lastUp = value.Time; _lastX = value.X; _lastY = value.Y;
                    }
                    if (Pointer is { } handlers) foreach (Action<ReadingPointerEvent> handler in handlers.GetInvocationList())
                        try { handler(new(kind, value.X, value.Y, kind == ReadingPointerKind.LeftUp ? _clickCount : 0, value.Time)); } catch { }
                }
            }
        }
        catch { /* Never prevent the host from receiving a mouse event. */ }
        return CallNextHookEx(_hook, code, message, data);
    }
    public void Stop()
    {
        Thread? worker;
        lock (_gate)
        {
            if (!_running && !_stopping) return;
            worker = _thread;
            if (_running) { _running = false; _stopping = true; if (_threadId != 0) PostThreadMessage(_threadId, 0x0012, 0, 0); }
        }
        if (worker is not null && worker != Thread.CurrentThread) worker.Join();
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } Stop(); Pointer = null; }
}
