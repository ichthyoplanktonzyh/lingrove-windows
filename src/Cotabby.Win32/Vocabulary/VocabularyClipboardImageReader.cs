using System.IO;
using System.Runtime.InteropServices;
using Cotabby.Win32.Clipboard;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Read-only encoded-image extraction used inside the killable transfer host. Never run this on
/// a UI thread: GetClipboardData can synchronously request a foreign application's delayed render.
/// It copies only the first non-null PNG/TIFF/JPEG, supported DIB or CF_BITMAP representation, bounds each copy and never
/// empties, replaces or frees Windows-owned clipboard data. Decode belongs to the archive later.
/// </summary>
internal sealed class VocabularyClipboardImageReader
{
    internal const int MaximumBytes = 24 * 1024 * 1024;
    private readonly IVocabularyImageClipboard _native;
    private readonly uint[] _formats;
    internal VocabularyClipboardImageReader(IVocabularyImageClipboard? native = null)
    {
        _native = native ?? new NativeClipboard();
        // Registered aliases are Windows producer conventions, not automatic format conversion.
        // Preserve Mac's PNG -> TIFF -> JPEG groups. A non-null invalid blob never falls through.
        _formats = [_native.Register("PNG"), _native.Register("image/png"), 6,
            _native.Register("JFIF"), _native.Register("JPEG"), _native.Register("image/jpeg"), 17, 8, 2];
    }
    // Metadata only: safe for command admission without invoking delayed clipboard rendering.
    internal bool HasAvailableImage() => _formats.Any(format => format != 0 && _native.Available(format));
    internal byte[]? Read(uint expectedSequence)
    {
        var selected = ReadRepresentation(expectedSequence);
        // Global-memory bytes are detached before DIB wrapping. CF_BITMAP instead returns an
        // envelope copied under its clipboard lease; every representation is detached before
        // pipe output and uses the same later WIC/archive normalization boundary.
        return selected is not { } value ? null : value.Format is 17 or 8 ? VocabularyClipboardDib.Wrap(value.Bytes, value.Format == 17) : value.Bytes;
    }
    private (uint Format, byte[] Bytes)? ReadRepresentation(uint expectedSequence)
    {
        if (expectedSequence == 0 || _native.Sequence != expectedSequence) return null;
        bool opened = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (_native.Sequence != expectedSequence) return null;
            if (_native.Open()) { opened = true; break; }
            if (attempt < 2) Thread.Sleep(5);
        }
        if (!opened) return null;
        try
        {
            if (_native.Sequence != expectedSequence) return null;
            IntPtr owner = _native.Owner;
            foreach (uint format in _formats)
            {
                if (format == 0 || !_native.Available(format)) continue;
                IntPtr handle = _native.Data(format);
                // Mac's data(forType:) nil can try the next representation. Empty or malformed
                // data is non-nil, so it is returned for normal preparation failure, not fallback.
                if (handle == IntPtr.Zero) continue;
                if (_native.Owner != owner) return null;
                uint renderedSequence = _native.Sequence;
                if (renderedSequence == 0) return null;
                if (format == 2)
                {
                    // HBITMAP is not an HGLOBAL. Copy while the clipboard still owns it;
                    // owner/sequence checks and finally-close apply to the native copy too.
                    byte[] bitmap = _native.ReadBitmap(handle);
                    if (bitmap.Length > MaximumBytes) throw Invalid();
                    if (_native.Owner != owner || _native.Sequence != renderedSequence) return null;
                    return (format, bitmap);
                }
                nuint count = _native.Size(handle); if (count > MaximumBytes) throw Invalid();
                byte[] bytes = new byte[(int)count];
                if (count > 0)
                {
                    IntPtr pointer = _native.Lock(handle); if (pointer == IntPtr.Zero) throw Invalid();
                    try { Marshal.Copy(pointer, bytes, 0, bytes.Length); }
                    finally { _native.Unlock(handle); }
                }
                // Delayed rendering may legitimately advance sequence while Data runs. Once a
                // handle has materialized, owner and sequence must stay stable through our copy.
                if (_native.Owner != owner || _native.Sequence != renderedSequence) return null;
                return (format, bytes);
            }
            return null;
        }
        finally { _native.Close(); }
    }
    private static InvalidDataException Invalid() => new("The clipboard image could not be verified.");
    private sealed class NativeClipboard : IVocabularyImageClipboard
    {
        public uint Register(string name) => ClipboardNativeMethods.RegisterClipboardFormat(name);
        public uint Sequence => ClipboardNativeMethods.GetClipboardSequenceNumber();
        public IntPtr Owner => ClipboardNativeMethods.GetClipboardOwner();
        public bool Open() => ClipboardNativeMethods.OpenClipboard(IntPtr.Zero);
        public void Close() => ClipboardNativeMethods.CloseClipboard();
        public bool Available(uint format) => ClipboardNativeMethods.IsClipboardFormatAvailable(format);
        public IntPtr Data(uint format) => ClipboardNativeMethods.GetClipboardData(format);
        public nuint Size(IntPtr handle) => ClipboardNativeMethods.GlobalSize(handle);
        public IntPtr Lock(IntPtr handle) => ClipboardNativeMethods.GlobalLock(handle);
        public void Unlock(IntPtr handle) => ClipboardNativeMethods.GlobalUnlock(handle);
        public byte[] ReadBitmap(IntPtr bitmap) => VocabularyClipboardBitmap.Read(bitmap);
    }
}

/// <summary>Exact Win32 effects; tests use app-owned unmanaged buffers without touching a clipboard.</summary>
internal interface IVocabularyImageClipboard
{
    uint Register(string name);
    uint Sequence { get; }
    IntPtr Owner { get; }
    bool Open();
    void Close();
    bool Available(uint format);
    IntPtr Data(uint format);
    nuint Size(IntPtr handle);
    IntPtr Lock(IntPtr handle);
    void Unlock(IntPtr handle);
    byte[] ReadBitmap(IntPtr bitmap) => throw new InvalidDataException("This clipboard adapter has no bitmap reader.");
}
