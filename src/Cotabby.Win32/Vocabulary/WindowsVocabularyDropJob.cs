using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Limits the untrusted OLE receiver and all of its children, and terminates them when
/// the editor releases its job. The receiver must be assigned before it accepts providers.</summary>
internal sealed class WindowsVocabularyDropJob : IDisposable
{
    private readonly JobHandle _handle;
    internal SafeHandle Handle => _handle;
    internal WindowsVocabularyDropJob()
    {
        _handle = CreateJobObjectW(IntPtr.Zero, null);
        if (_handle.IsInvalid) { int error = Marshal.GetLastPInvokeError(); _handle.Dispose(); throw new Win32Exception(error); }
        var limits = new Extended { Basic = new Basic { Flags = 0x2000 | 0x100 | 0x200 | 0x8, ActiveLimit = 1 },
            ProcessMemory = 512u * 1024u * 1024u, JobMemory = 512u * 1024u * 1024u };
        if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf<Extended>()))
        { int error = Marshal.GetLastPInvokeError(); _handle.Dispose(); throw new Win32Exception(error); }
    }
    public void Dispose() => _handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct Basic
    { internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinimumWorkingSet, MaximumWorkingSet; internal uint ActiveLimit; internal nuint Affinity; internal uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct Counters
    { internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct Extended
    { internal Basic Basic; internal Counters Io; internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    private sealed class JobHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    { protected override bool ReleaseHandle() => CloseHandle(handle); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern JobHandle CreateJobObjectW(IntPtr security, string? name);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(JobHandle job, int kind, ref Extended limits, uint size);
    [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
