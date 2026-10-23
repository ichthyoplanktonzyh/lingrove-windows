using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Cotabby.Win32.Clipboard;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Window-owned receiver for the Shell's existing-file WM_DROPFILES compatibility path. This
/// receives copied path memory, not a foreign IDataObject callback. It frees the Shell allocation
/// before publishing one detached first path and client pixel point. The App gates that point to
/// its explanation area and borrows the archive for bounded worker IO. Virtual files and image
/// pixels without a path require a separate provider adapter; no OLE object escapes Drop here.
/// </summary>
public sealed class WindowsVocabularyFileDrop : IDisposable
{
    private const int DropMessage = 0x0233, MaximumBytes = 1024 * 1024, MaximumPathCharacters = 32767;
    private readonly HwndSource _source;
    private Action<string, Point>? _received;
    private bool _disposed;

    public WindowsVocabularyFileDrop(HwndSource source, Action<string, Point> received)
    {
        source.Dispatcher.VerifyAccess(); _source = source; _received = received;
        source.AddHook(Message); DragAcceptFiles(source.Handle, true);
    }
    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed || message != DropMessage) return IntPtr.Zero;
        handled = true; (string Path, Point Point)? drop = null;
        try { drop = Read(wParam); }
        finally { DragFinish(wParam); }
        // Shell memory is no longer borrowed when App starts asynchronous preparation. Only
        // one path is handed off, even when the transfer lists several files.
        if (drop is { } value)
        {
            try { _received?.Invoke(value.Path, value.Point); }
            catch (Exception) { /* A presentation failure cannot escape a native window procedure. */ }
        }
        return IntPtr.Zero;
    }
    private static (string Path, Point Point)? Read(IntPtr memory)
    {
        nuint size = ClipboardNativeMethods.GlobalSize(memory);
        if (size < 24 || size > MaximumBytes) return null;
        IntPtr pointer = ClipboardNativeMethods.GlobalLock(memory); if (pointer == IntPtr.Zero) return null;
        byte[] bytes;
        try { bytes = new byte[(int)size]; Marshal.Copy(pointer, bytes, 0, bytes.Length); }
        finally { ClipboardNativeMethods.GlobalUnlock(memory); }
        // DROPFILES is 20 bytes on both Win32 and x64: DWORD offset, POINT and two BOOLs.
        // Inspect only our bounded copy. Strict Unicode avoids native scanning past malformed
        // memory; ANSI/virtual payloads are outside this first file-path-only adapter.
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes), nonClient = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)),
            wide = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
        if (offset < 20 || offset > bytes.Length - 4 || (offset & 1) != 0 || nonClient != 0 || wide == 0) return null;
        int end = offset;
        while (end <= bytes.Length - 2 && end - offset <= MaximumPathCharacters * 2)
        {
            if (bytes[end] == 0 && bytes[end + 1] == 0) break;
            end += 2;
        }
        if (end == offset || end > bytes.Length - 4 || end - offset > MaximumPathCharacters * 2) return null;
        try
        {
            string path = new UnicodeEncoding(false, false, true).GetString(bytes, offset, end - offset);
            if (!System.IO.Path.IsPathFullyQualified(path)) return null;
            return (path, new Point(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8))));
        }
        catch (ArgumentException) { return null; }
    }
    public void Dispose()
    {
        _source.Dispatcher.VerifyAccess(); if (_disposed) return; _disposed = true; _received = null;
        if (!_source.IsDisposed) { DragAcceptFiles(_source.Handle, false); _source.RemoveHook(Message); }
    }
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void DragAcceptFiles(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool accept);
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void DragFinish(IntPtr drop);
}
