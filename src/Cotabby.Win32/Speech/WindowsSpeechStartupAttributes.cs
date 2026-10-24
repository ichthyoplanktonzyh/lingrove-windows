using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Cotabby.Win32.Speech;

/// <summary>One CreateProcess call owns this attribute list and its pinned handle arrays. The
/// kernel assigns the helper to its kill-on-close job during creation, closing the launch-to-job
/// crash gap. Only the three explicitly selected stdio handles may be inherited by the child.</summary>
internal sealed class WindowsSpeechStartupAttributes : IDisposable
{
    private readonly List<SafeHandle> _pins = [];
    private readonly List<IntPtr> _values = [];
    private bool _initialized;
    internal IntPtr Pointer { get; private set; }
    internal WindowsSpeechStartupAttributes(SafeHandle job, params SafeHandle[] inherited)
    {
        try
        {
            nuint bytes = 0;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref bytes);
            if (bytes == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Pointer = Marshal.AllocHGlobal(checked((nint)bytes));
            if (!InitializeProcThreadAttributeList(Pointer, 2, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            _initialized = true;
            Set(0x2000d, [job]); // PROC_THREAD_ATTRIBUTE_JOB_LIST: supported Windows10+, assigned atomically.
            Set(0x20002, inherited); // PROC_THREAD_ATTRIBUTE_HANDLE_LIST; bInheritHandles must also be true.
        }
        catch { Dispose(); throw; }
    }
    private void Set(nuint key, SafeHandle[] handles)
    {
        IntPtr array = Marshal.AllocHGlobal(checked(handles.Length * IntPtr.Size)); _values.Add(array);
        for (int i = 0; i < handles.Length; i++)
        {
            bool pinned = false; handles[i].DangerousAddRef(ref pinned);
            if (!pinned) throw new ObjectDisposedException(nameof(handles));
            _pins.Add(handles[i]); Marshal.WriteIntPtr(array, i * IntPtr.Size, handles[i].DangerousGetHandle());
        }
        if (!UpdateProcThreadAttribute(Pointer, 0, key, array, checked((nuint)(handles.Length * IntPtr.Size)), IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose()
    {
        if (Pointer != IntPtr.Zero) { if (_initialized) DeleteProcThreadAttributeList(Pointer); Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero; _initialized = false; }
        foreach (IntPtr value in _values) Marshal.FreeHGlobal(value); _values.Clear();
        foreach (var handle in _pins) handle.DangerousRelease(); _pins.Clear();
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, uint count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint key, IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
}
