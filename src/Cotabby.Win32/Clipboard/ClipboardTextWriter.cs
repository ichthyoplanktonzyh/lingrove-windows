using System.ComponentModel;
using System.Runtime.InteropServices;
using Cotabby.Core.Context;
using Microsoft.Win32.SafeHandles;
using static Cotabby.Win32.Clipboard.ClipboardNativeMethods;

namespace Cotabby.Win32.Clipboard;

/// <summary>
/// App-owned explicit Copy boundary. It allocates the complete bounded UTF-16 payload before
/// opening/emptying the clipboard, attempts one exclusive write and closes immediately. Unlike
/// insertion's temporary paste lease, this is a user copy: it is neither restored nor hidden
/// from optional clipboard context. It has no editor, input hook, model or WPF dependency.
/// </summary>
public sealed class ClipboardTextWriter : IClipboardTextWriter
{
    private readonly IClipboardCopyBackend _backend;
    public ClipboardTextWriter() : this(new NativeClipboardCopyBackend()) { }
    internal ClipboardTextWriter(IClipboardCopyBackend backend) => _backend = backend;

    public bool TryWrite(string text, Func<bool> tryCommit)
    {
        ArgumentNullException.ThrowIfNull(tryCommit);
        if (!ClipboardTextCopyPolicy.IsValid(text)) return false;
        try
        {
            using var prepared = _backend.Prepare(text);
            if (prepared is null || !_backend.Open()) return false;
            bool copied, closed;
            // The application grants this exact attempt while the clipboard is already exclusive.
            // Retirement before this grant leaves it untouched; retirement after the grant cannot
            // abandon a half-completed Empty/Set transaction. No app lock spans native clipboard IO.
            try { copied = tryCommit() && _backend.Empty() && prepared.Transfer(); }
            finally { closed = _backend.Close(); }
            return copied && closed;
        }
        catch (Win32Exception) { return false; }
    }

    private sealed class NativeClipboardCopyBackend : IClipboardCopyBackend
    {
        public IPreparedClipboardText? Prepare(string text) => PreparedText.Create(text);
        public bool Open() => OpenClipboard(ClipboardOwnerWindow.Handle);
        public bool Empty() => EmptyClipboard();
        public bool Close() => CloseClipboard();
    }

    /// <summary>
    /// Owns HGLOBAL until SetClipboardData succeeds. Only success transfers ownership to Windows;
    /// every rejected/open/empty/transfer failure frees the prepared handle through SafeHandle.
    /// An already-empty clipboard cannot be rolled back after a native transfer failure; the view
    /// reports failure and lets the user explicitly retry rather than silently copying later.
    /// </summary>
    private sealed class PreparedText : SafeHandleZeroOrMinusOneIsInvalid, IPreparedClipboardText
    {
        private PreparedText() : base(ownsHandle: true) { }
        public static PreparedText? Create(string text)
        {
            var memory = new PreparedText();
            memory.SetHandle(GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, checked((nuint)(text.Length + 1) * sizeof(char))));
            if (memory.IsInvalid) { memory.Dispose(); return null; }
            IntPtr pointer = GlobalLock(memory.handle);
            if (pointer == IntPtr.Zero) { memory.Dispose(); return null; }
            try
            {
                try { Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length); }
                finally { GlobalUnlock(memory.handle); }
            }
            catch { memory.Dispose(); throw; }
            return memory;
        }
        public bool Transfer()
        {
            if (IsClosed || IsInvalid || SetClipboardData(CF_UNICODETEXT, handle) == IntPtr.Zero) return false;
            SetHandleAsInvalid();
            return true;
        }
        protected override bool ReleaseHandle() => GlobalFree(handle) == IntPtr.Zero;
    }
}

/// <summary>A test seam for ordering/ownership failures without touching a test runner's clipboard.</summary>
internal interface IClipboardCopyBackend
{
    IPreparedClipboardText? Prepare(string text);
    bool Open();
    bool Empty();
    bool Close();
}
internal interface IPreparedClipboardText : IDisposable { bool Transfer(); }
