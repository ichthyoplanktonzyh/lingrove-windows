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
    private readonly HashSet<Task> _work = [];
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
        Volatile.Write(ref _current, null);
        if (hwnd == 0) return;
        long epoch = _epoch(); int pid = _process(hwnd);
        if (epoch <= 0 || pid <= 0) return;
        lock (_gate)
        {
            if (_disposed) return;
            var task = Task.Run(() => Resolve(hwnd, pid, epoch));
            _work.Add(task);
            _ = task.ContinueWith(t => { lock (_gate) _work.Remove(t); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private void Resolve(nint hwnd, int pid, long epoch)
    {
        if (!Matches(hwnd, pid, epoch)) return;
        string? name;
        try { name = _processName(pid); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return; }
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
            _disposed = true; Volatile.Write(ref _current, null); if (_focus is not null) _focus.FocusTransition -= FocusTransition;
            var tasks = _work.ToArray();
            return new(_disposal = Task.Run(async () => { try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { } }));
        }
    }
}
