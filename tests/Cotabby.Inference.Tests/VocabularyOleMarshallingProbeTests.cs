using System.Runtime.InteropServices;

namespace Cotabby.Inference.Tests;

/// <summary>Bounded synthetic COM object: no foreign process or clipboard is consulted.</summary>
public sealed class VocabularyOleMarshallingProbeTests
{
    [Fact]
    public async Task CustomMarshalCanHoldTheCallingApartment()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var task = Task.Run(() => MarshalOnCurrentThread(new HeldMarshal(entered, release)));
        try { Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); Assert.False(task.IsCompleted); }
        finally { release.Set(); }
        await task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void MarshalOnCurrentThread(HeldMarshal held)
    {
        int initialized = CoInitializeEx(0, 0); IntPtr unknown = IntPtr.Zero, stream = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(0, true, out stream));
            unknown = Marshal.GetIUnknownForObject(held); Guid iid = new("00000000-0000-0000-C000-000000000046");
            _ = CoMarshalInterface(stream, ref iid, unknown, 0, IntPtr.Zero, 0);
        }
        finally { if (unknown != IntPtr.Zero) Marshal.Release(unknown); if (stream != IntPtr.Zero) Marshal.Release(stream); if (initialized >= 0) CoUninitialize(); }
    }
    [ComVisible(true), Guid("00000003-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICustomMarshal
    {
        [PreserveSig] int GetUnmarshalClass(ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags, out Guid clsid);
        [PreserveSig] int GetMarshalSizeMax(ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags, out uint size);
        [PreserveSig] int MarshalInterface(IntPtr stream, ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags);
        [PreserveSig] int UnmarshalInterface(IntPtr stream, ref Guid iid, out IntPtr value);
        [PreserveSig] int ReleaseMarshalData(IntPtr stream);
        [PreserveSig] int DisconnectObject(uint reserved);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class HeldMarshal(ManualResetEventSlim entered, ManualResetEventSlim release) : ICustomMarshal
    {
        public int GetUnmarshalClass(ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags, out Guid clsid)
        { entered.Set(); release.Wait(TimeSpan.FromSeconds(15)); clsid = Guid.Empty; return unchecked((int)0x80004001); }
        public int GetMarshalSizeMax(ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags, out uint size) { size = 0; return unchecked((int)0x80004001); }
        public int MarshalInterface(IntPtr stream, ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags) => unchecked((int)0x80004001);
        public int UnmarshalInterface(IntPtr stream, ref Guid iid, out IntPtr value) { value = IntPtr.Zero; return unchecked((int)0x80004001); }
        public int ReleaseMarshalData(IntPtr stream) => 0;
        public int DisconnectObject(uint reserved) => 0;
    }
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CreateStreamOnHGlobal(IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out IntPtr stream);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoMarshalInterface(IntPtr stream, ref Guid iid, IntPtr value, uint context, IntPtr destination, uint flags);
}
