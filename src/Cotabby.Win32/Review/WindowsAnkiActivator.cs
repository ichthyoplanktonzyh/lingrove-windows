using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cotabby.Win32.Review;

/// <summary>Best-effort activation of an already running Anki after acknowledged navigation.
/// Construction is inert. This adapter never launches a collection, reads window text, sends
/// keystrokes or bypasses Windows foreground restrictions. Unrecognized executable hosts are
/// left alone. The review controller owns when activation is authorized.</summary>
public sealed class WindowsAnkiActivator
{
    private readonly IAnkiWindows _windows;
    public WindowsAnkiActivator() : this(new NativeAnkiWindows()) { }
    internal WindowsAnkiActivator(IAnkiWindows windows) => _windows = windows;

    public void ActivateExisting()
    {
        try
        {
            foreach (var candidate in _windows.FindExisting().Take(64))
            {
                if (!_windows.IsCurrent(candidate)) continue;
                if (_windows.IsMinimized(candidate.Window)) _windows.Restore(candidate.Window);
                // Restoration can dispatch native work. Recheck the HWND/PID before handing
                // foreground to it: a closed/reused window must not activate another process.
                if (_windows.IsCurrent(candidate)) _windows.TryForeground(candidate.Window);
                return;
            }
        }
        catch (Exception error) when (Unavailable(error)) { /* Activation is best effort, as on Mac. */ }
    }
    private static bool Unavailable(Exception error) => error is Win32Exception or InvalidOperationException or NotSupportedException;
    internal readonly record struct Candidate(nint Window, int ProcessId);
    internal interface IAnkiWindows
    {
        IReadOnlyList<Candidate> FindExisting();
        bool IsCurrent(Candidate candidate);
        bool IsMinimized(nint window);
        void Restore(nint window);
        void TryForeground(nint window);
    }
    private sealed class NativeAnkiWindows : IAnkiWindows
    {
        public IReadOnlyList<Candidate> FindExisting()
        {
            var found = new List<Candidate>();
            var processes = Process.GetProcessesByName("anki");
            try
            {
                foreach (var process in processes.Take(64))
                {
                    try
                    {
                        if (process.MainWindowHandle is var window && window != 0)
                        {
                            var candidate = new Candidate(window, process.Id);
                            if (IsCurrent(candidate)) found.Add(candidate);
                        }
                    }
                    catch (Exception error) when (Unavailable(error)) { }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
            return found;
        }
        public bool IsCurrent(Candidate candidate)
        {
            if (candidate.Window == 0 || !IsWindow(candidate.Window) || !IsWindowVisible(candidate.Window)) return false;
            GetWindowThreadProcessId(candidate.Window, out uint owner);
            if (owner != candidate.ProcessId) return false;
            try
            {
                using var process = Process.GetProcessById(candidate.ProcessId);
                using var self = Process.GetCurrentProcess();
                return process.SessionId == self.SessionId && !process.HasExited
                    && string.Equals(System.IO.Path.GetFileName(process.MainModule?.FileName), "anki.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error) when (Unavailable(error) || error is ArgumentException) { return false; }
        }
        public bool IsMinimized(nint window) => IsIconic(window);
        public void Restore(nint window) => _ = ShowWindowAsync(window, 9 /* SW_RESTORE */);
        public void TryForeground(nint window) => _ = SetForegroundWindow(window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowAsync(nint window, int command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    }
}
