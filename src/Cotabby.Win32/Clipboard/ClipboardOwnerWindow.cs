using System.ComponentModel;
using System.Runtime.InteropServices;
using Cotabby.Win32.Interop;

namespace Cotabby.Win32.Clipboard;

/// <summary>
/// Holds a real process-owned HWND for clipboard writes. Win32 makes that window the owner only
/// after EmptyClipboard; passing NULL would make later SetClipboardData calls fail. A dedicated
/// message thread keeps the hidden owner alive for the process lifetime so it can receive owner
/// notifications while paste leases come and go on thread-pool threads.
/// </summary>
internal static class ClipboardOwnerWindow
{
    private static readonly Lazy<IntPtr> Owner = new(CreateOwner);

    public static IntPtr Handle => Owner.Value;

    /// <summary>Clipboard ownership is checked before restoring; a destroyed HWND can never authorize it.</summary>
    public static bool IsAlive => Handle != IntPtr.Zero && ClipboardNativeMethods.IsWindow(Handle);

    private static IntPtr CreateOwner()
    {
        var ready = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunOwnerThread(ready))
        {
            IsBackground = true,
            Name = "Lingrove clipboard owner",
        };
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    private static void RunOwnerThread(TaskCompletionSource<IntPtr> ready)
    {
        // HWND_MESSAGE creates a hidden message-only window, while the STATIC system class
        // supplies a real window procedure without tying clipboard ownership to a WPF view.
        IntPtr handle = ClipboardNativeMethods.CreateWindowEx(
            extendedStyle: 0,
            className: "STATIC",
            windowName: "",
            style: 0,
            x: 0,
            y: 0,
            width: 0,
            height: 0,
            parentWindow: new IntPtr(-3),
            menu: IntPtr.Zero,
            instance: NativeMethods.GetModuleHandle(null),
            parameter: IntPtr.Zero);

        if (handle == IntPtr.Zero)
        {
            ready.TrySetException(new Win32Exception(Marshal.GetLastPInvokeError()));
            return;
        }

        ready.TrySetResult(handle);
        while (ClipboardNativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            ClipboardNativeMethods.TranslateMessage(in message);
            ClipboardNativeMethods.DispatchMessage(in message);
        }

        ClipboardNativeMethods.DestroyWindow(handle);
    }
}
