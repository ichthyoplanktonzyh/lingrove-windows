using Cotabby.Core.Context;
using static Cotabby.Win32.Interop.NativeMethods;

namespace Cotabby.Win32.Clipboard;

/// <summary>
/// Reads clipboard text through the raw Win32 API, which works from any thread.
/// </summary>
/// <remarks>
/// The guarded inserter reports each clipboard mutation while it holds the clipboard open
/// exclusively. <see cref="ClipboardWriteAccounting"/> subtracts only those exact sequence
/// deltas, so a user copy made while a paste lease is alive remains available as context.
/// </remarks>
public sealed class ClipboardTextReader : IClipboardReader
{
    /// <summary>Longest text returned. Anything past it is cut off.</summary>
    public const int MaxChars = 16_384;

    private const int OpenAttempts = 3;
    private const int OpenRetryMs = 5;

    private readonly ClipboardWriteAccounting _writeAccounting;

    public ClipboardTextReader() : this(new ClipboardWriteAccounting()) { }

    internal ClipboardTextReader(ClipboardWriteAccounting writeAccounting) =>
        _writeAccounting = writeAccounting;

    public uint SequenceNumber => _writeAccounting.GetVisibleSequence(GetClipboardSequenceNumber());

    public string? ReadText()
    {
        if (!TryOpenClipboard()) return null;
        try
        {
            // Read the sequence only after the clipboard is open. This ties the ownership check to
            // the same clipboard contents from which the text handle is read.
            uint sequence = GetClipboardSequenceNumber();
            if (_writeAccounting.IsOwnText(sequence)) return null;
            return ReadUnicodeTextWhileOpen();
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Begin tracking a mutation after OpenClipboard has succeeded.</summary>
    internal void BeginOwnWrite(uint sequenceBeforeWrite) => _writeAccounting.BeginOwnWrite(sequenceBeforeWrite);

    /// <summary>Finish tracking with a sequence read taken before CloseClipboard.</summary>
    internal void EndOwnWrite(uint sequenceAfterWrite, bool containsOwnText) =>
        _writeAccounting.EndOwnWrite(sequenceAfterWrite, containsOwnText);

    private static unsafe string? ReadUnicodeTextWhileOpen()
    {
        IntPtr handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == IntPtr.Zero) return null;
        char* text = (char*)GlobalLock(handle);
        if (text is null) return null;
        try
        {
            int limit = (int)Math.Min(GlobalSize(handle) / sizeof(char), (nuint)MaxChars);
            int length = new ReadOnlySpan<char>(text, limit).IndexOf('\0');
            if (length < 0) length = limit;
            // A cut must not leave half a surrogate pair.
            if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
            return new string(text, 0, length);
        }
        finally
        {
            // Clipboard handles belong to Windows; release only our temporary lock, never the
            // HGLOBAL itself.
            GlobalUnlock(handle);
        }
    }

    /// <summary>Another process may hold the clipboard open for a moment.</summary>
    private static bool TryOpenClipboard()
    {
        for (int attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            if (attempt < OpenAttempts) Thread.Sleep(OpenRetryMs);
        }
        return false;
    }
}
