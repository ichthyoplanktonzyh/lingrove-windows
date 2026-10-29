using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Cotabby.Win32.Speech;

/// <summary>COM-visible output sink for SAPI. Every write checks cancellation, extent and the
/// fixed PCM duration budget before accepting bytes; no native producer can grow it first and
/// have the caller discover an oversized recording afterwards.</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class BoundedSpeechStream(int maximumBytes, CancellationToken token) : IStream
{
    private readonly MemoryStream _data = new();
    private readonly object _gate = new();
    private bool _overflowed;
    internal bool Overflowed { get { lock (_gate) return _overflowed; } }
    internal byte[] Bytes() { lock (_gate) return _data.ToArray(); }
    public void Write(byte[] pv, int cb, IntPtr pcbWritten)
    {
        lock (_gate)
        {
            if (token.IsCancellationRequested) throw new COMException("Cancelled", unchecked((int)0x80004004));
            if (cb < 0 || cb > pv.Length || _data.Position > maximumBytes - cb) { _overflowed = true; throw new COMException("Speech recording limit", unchecked((int)0x80030070)); }
            _data.Write(pv, 0, cb); if (pcbWritten != IntPtr.Zero) Marshal.WriteInt32(pcbWritten, cb);
        }
    }
    public void Read(byte[] pv, int cb, IntPtr pcbRead) { lock (_gate) { int n = _data.Read(pv, 0, cb); if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n); } }
    public void Seek(long move, int origin, IntPtr newPosition)
    {
        lock (_gate)
        {
            long target = origin switch { 0 => move, 1 => checked(_data.Position + move), 2 => checked(_data.Length + move), _ => -1 };
            if (target < 0 || target > maximumBytes) throw new COMException("Invalid seek", unchecked((int)0x80030019));
            _data.Position = target; if (newPosition != IntPtr.Zero) Marshal.WriteInt64(newPosition, target);
        }
    }
    public void SetSize(long size) { lock (_gate) { if (token.IsCancellationRequested) throw new COMException("Cancelled", unchecked((int)0x80004004)); if (size < 0 || size > maximumBytes) { _overflowed = true; throw new COMException("Speech recording limit", unchecked((int)0x80030070)); } _data.SetLength(size); } }
    public void Commit(int flags) { }
    public void Stat(out STATSTG stat, int flags) { lock (_gate) stat = new() { type = 2, cbSize = _data.Length }; }
    public void CopyTo(IStream target, long count, IntPtr read, IntPtr written) => throw new COMException("Unsupported", unchecked((int)0x80004001));
    public void Revert() => throw new COMException("Unsupported", unchecked((int)0x80004001));
    public void LockRegion(long offset, long count, int type) => throw new COMException("Unsupported", unchecked((int)0x80004001));
    public void UnlockRegion(long offset, long count, int type) => throw new COMException("Unsupported", unchecked((int)0x80004001));
    public void Clone(out IStream stream) { stream = null!; throw new COMException("Unsupported", unchecked((int)0x80004001)); }
}
