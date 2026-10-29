using System.Diagnostics;
using System.Runtime.Versioning;
using Cotabby.Win32.Focus;
using Native = Cotabby.Win32.Interop.NativeMethods;

namespace Cotabby.Win32.Reading;

/// <summary>Cheap hook-time origin facts. Process names resolve off-hook on the existing focus
/// owner's transition signal; no UIA, additional global listener or editor read is performed.</summary>
public sealed record ReadingForeground(nint Window, int ProcessId, long FocusEpoch, string ProcessName);

[SupportedOSPlatform("windows8.1")]
public sealed class Win32ReadingOriginTracker : IAsyncDisposable
{
    private readonly UiaFocusTracker? _focus;
    private readonly Func<long> _epoch;
    private readonly Func<nint> _foreground;
    private readonly Func<nint, int> _process;
    private readonly Func<int, string?> _processName;
    private readonly object _gate = new();
    private (nint Window, int ProcessId, long Epoch)? _pending;
    private Task? _worker;
    private ReadingForeground? _current;
    private bool _disposed;
    private Task? _disposal;

    public Win32ReadingOriginTracker(UiaFocusTracker focus)
    {
        _focus = focus; _epoch = () => focus.ActiveFocusEpoch; _foreground = Native.GetForegroundWindow;
        _process = hwnd => Native.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid > int.MaxValue ? 0 : (int)pid;
        _processName = pid => { using var process = Process.GetProcessById(pid); return process.ProcessName; };
        focus.FocusTransition += FocusTransition;
    }
    internal Win32ReadingOriginTracker(Func<long> epoch, Func<nint> foreground, Func<nint, int> process, Func<int, string?> processName)
    { _epoch = epoch; _foreground = foreground; _process = process; _processName = processName; }
    internal void FocusTransition(nint hwnd)
    {
        if (hwnd == 0)
        {
            lock (_gate)
            {
                if (_epoch() != 0) return; // A late Stop must not erase a newer Start.
                Volatile.Write(ref _current, null); _pending = null;
            }
            return;
        }
        long epoch = _epoch(); int pid = _process(hwnd);
        if (epoch <= 0 || pid <= 0) return;
        lock (_gate)
        {
            if (_disposed || !Matches(hwnd, pid, epoch)) return;
            Volatile.Write(ref _current, null); _pending = (hwnd, pid, epoch);
            _worker ??= Task.Run(Drain);
        }
    }
    private void Drain()
    {
        while (true)
        {
            (nint Window, int ProcessId, long Epoch) next;
            lock (_gate)
            {
                if (_disposed || _pending is not { } pending) { _worker = null; return; }
                next = pending; _pending = null;
            }
            Resolve(next.Window, next.ProcessId, next.Epoch);
        }
    }
    private void Resolve(nint hwnd, int pid, long epoch)
    {
        if (!Matches(hwnd, pid, epoch)) return;
        string? name;
        try { name = _processName(pid); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException) { return; }
        if (string.IsNullOrWhiteSpace(name) || !Matches(hwnd, pid, epoch)) return;
        lock (_gate) if (!_disposed && Matches(hwnd, pid, epoch)) Volatile.Write(ref _current, new(hwnd, pid, epoch, name));
    }
    private bool Matches(nint hwnd, int pid, long epoch) => _epoch() == epoch
        && _foreground() == hwnd && _process(hwnd) == pid;

    public ReadingForeground? Current
    {
        get
        {
            var value = Volatile.Read(ref _current);
            return value is not null && Matches(value.Window, value.ProcessId, value.FocusEpoch) ? value : null;
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _disposed = true; Volatile.Write(ref _current, null); _pending = null; if (_focus is not null) _focus.FocusTransition -= FocusTransition;
            var worker = _worker;
            return new(_disposal = Task.Run(async () => { if (worker is not null) try { await worker.ConfigureAwait(false); } catch { } }));
        }
    }
}
