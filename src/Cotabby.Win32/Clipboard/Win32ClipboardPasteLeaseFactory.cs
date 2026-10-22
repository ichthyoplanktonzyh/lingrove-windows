using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Cotabby.Core.Context;
using Cotabby.Core.Insertion;
using static Cotabby.Win32.Clipboard.ClipboardNativeMethods;

namespace Cotabby.Win32.Clipboard;

/// <summary>
/// Makes a bounded private snapshot of the current clipboard, then replaces it with one Unicode
/// text format for the pending paste. Clipboard mutation is performed while OpenClipboard holds
/// an exclusive lock; any format that cannot be deep-copied as movable global memory rejects the
/// paste before EmptyClipboard can discard it.
/// </summary>
internal sealed class Win32ClipboardPasteLeaseFactory : IClipboardPasteLeaseFactory
{
    internal const int MaximumSnapshotBytes = 32 * 1024 * 1024;
    private const int MaximumSnapshotFormats = 512;

    private readonly ClipboardTextReader? _textReader;

    public Win32ClipboardPasteLeaseFactory(ClipboardTextReader? textReader) => _textReader = textReader;

    public Task<IClipboardPasteLease?> PrepareAsync(string text, CancellationToken ct) =>
        Task.Run(() => TryPrepare(text, ct), CancellationToken.None);

    private IClipboardPasteLease? TryPrepare(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IntPtr ownerWindow = ClipboardOwnerWindow.Handle;
        if (!TryOpenClipboard(ownerWindow)) return null;

        Snapshot? snapshot = null;
        uint stagedSequence = 0;
        bool staged = false;
        Win32ClipboardPasteLease? lease = null;
        bool clipboardClosed = false;
        try
        {
            ct.ThrowIfCancellationRequested();

            // Some clipboard owners render formats on demand in response to GetClipboardData.
            // Materialize every format first, then ensure that rendering did not change the
            // clipboard before we begin our own write accounting or destructive mutation.
            uint sequenceBeforeSnapshot = GetClipboardSequenceNumber();
            if (!TryCaptureSnapshot(out Snapshot capturedSnapshot)) return null;
            snapshot = capturedSnapshot;
            uint sequenceAfterSnapshot = GetClipboardSequenceNumber();
            if (sequenceAfterSnapshot != sequenceBeforeSnapshot) return null;

            ct.ThrowIfCancellationRequested();
            byte[] textPayload = EncodeUnicodeText(text);
            IntPtr stagedHandle = AllocateGlobal(textPayload);
            if (!TryAllocateFormats(snapshot, out IntPtr[] rollbackHandles))
            {
                GlobalFree(stagedHandle);
                return null;
            }

            bool tracking = false;
            try
            {
                _textReader?.BeginOwnWrite(sequenceAfterSnapshot);
                tracking = _textReader is not null;

                if (!EmptyClipboard()) return null;

                IntPtr transferred = SetClipboardData(CF_UNICODETEXT, stagedHandle);
                if (transferred == IntPtr.Zero)
                {
                    // The rollback handles were allocated before EmptyClipboard, so a memory
                    // shortage in the failing SetClipboardData path cannot prevent restoration.
                    if (!TrySetAllocatedFormatsWhileOpen(snapshot, rollbackHandles))
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }
                    return null;
                }

                stagedHandle = IntPtr.Zero; // the clipboard owns the movable HGLOBAL now
                stagedSequence = GetClipboardSequenceNumber();
                staged = true;
                lease = new Win32ClipboardPasteLease(snapshot, stagedSequence, _textReader);
            }
            finally
            {
                if (stagedHandle != IntPtr.Zero) GlobalFree(stagedHandle);
                FreeUntransferred(rollbackHandles);
                if (tracking)
                {
                    _textReader!.EndOwnWrite(GetClipboardSequenceNumber(), containsOwnText: staged);
                }
            }
        }
        finally
        {
            clipboardClosed = CloseClipboard();
        }

        if (!clipboardClosed)
        {
            RecoverFailedClose(snapshot, stagedSequence, staged, ownerWindow);
            return null;
        }

        return lease;
    }

    private static bool TryCaptureSnapshot(out Snapshot snapshot)
    {
        var formats = new List<SavedFormat>();
        int totalBytes = 0;
        uint previousFormat = 0;

        while (true)
        {
            Marshal.SetLastPInvokeError(0);
            uint format = EnumClipboardFormats(previousFormat);
            int error = Marshal.GetLastPInvokeError();
            if (format == 0)
            {
                if (error != 0)
                {
                    snapshot = Snapshot.Empty;
                    return false;
                }
                break;
            }

            if (format == previousFormat || !IsDeepCopyableGlobalFormat(format))
            {
                snapshot = Snapshot.Empty;
                return false;
            }

            if (formats.Count >= MaximumSnapshotFormats)
            {
                snapshot = Snapshot.Empty;
                return false;
            }

            IntPtr clipboardHandle = GetClipboardData(format);
            if (clipboardHandle == IntPtr.Zero)
            {
                // Null can mean delayed rendering, an unavailable private format, or an opaque
                // handle. We do not infer ownership or discard a format we could not read.
                snapshot = Snapshot.Empty;
                return false;
            }

            nuint nativeSize = GlobalSize(clipboardHandle);
            if (nativeSize == 0 || nativeSize > (nuint)MaximumSnapshotBytes
                || totalBytes > MaximumSnapshotBytes - (int)nativeSize)
            {
                snapshot = Snapshot.Empty;
                return false;
            }

            int size = checked((int)nativeSize);
            IntPtr source = GlobalLock(clipboardHandle);
            if (source == IntPtr.Zero)
            {
                snapshot = Snapshot.Empty;
                return false;
            }

            try
            {
                // GetClipboardData returns a clipboard-owned handle. We copy its full bounded
                // allocation into managed memory and leave the original handle with Windows.
                byte[] bytes = new byte[size];
                Marshal.Copy(source, bytes, 0, size);
                formats.Add(new SavedFormat(format, bytes));
                totalBytes += size;
            }
            finally
            {
                // GlobalUnlock returns false when this decrement reaches the unlocked state, so
                // the return value is not itself an error signal. The clipboard retains ownership.
                GlobalUnlock(clipboardHandle);
            }

            previousFormat = format;
        }

        snapshot = new Snapshot(formats.ToArray());
        return true;
    }

    private static bool IsDeepCopyableGlobalFormat(uint format)
    {
        if (ClipboardFormatCopyPolicy.IsStandardGlobalFormat(format)) return true;

        // Only the common HTML and RTF registered formats are accepted. An unfamiliar registered
        // format may use an opaque handle, delayed rendering, or an application-specific protocol.
        return TryGetRegisteredFormatName(format, out string name)
            && ClipboardFormatCopyPolicy.IsSupportedRegisteredName(name);
    }

    private static unsafe bool TryGetRegisteredFormatName(uint format, out string name)
    {
        const int MaxFormatNameLength = 256;
        char* buffer = stackalloc char[MaxFormatNameLength];
        int length = GetClipboardFormatName(format, buffer, MaxFormatNameLength);
        if (length <= 0)
        {
            name = string.Empty;
            return false;
        }

        name = new string(buffer, 0, length);
        return true;
    }

    private static byte[] EncodeUnicodeText(string text)
    {
        byte[] payload = new byte[checked(Encoding.Unicode.GetByteCount(text) + sizeof(char))];
        Encoding.Unicode.GetBytes(text, 0, text.Length, payload, 0);
        return payload;
    }

    private static bool TrySetAllocatedFormatsWhileOpen(Snapshot snapshot, IntPtr[] handles)
    {
        for (int index = 0; index < snapshot.Formats.Length; index++)
        {
            SavedFormat format = snapshot.Formats[index];
            if (SetClipboardData(format.Format, handles[index]) == IntPtr.Zero) return false;
            handles[index] = IntPtr.Zero;
        }

        return true;
    }

    private void RecoverFailedClose(Snapshot? snapshot, uint stagedSequence, bool staged, IntPtr ownerWindow)
    {
        // CloseClipboard failed on this same worker thread. Probe whether it still owns the open
        // clipboard first; if so, perform the rollback there rather than dispatching cleanup to a
        // different thread that cannot release this thread's clipboard lock.
        bool alreadyOpenOnCurrentThread = IsClipboardOpenOnCurrentThread();
        if (!alreadyOpenOnCurrentThread && !staged) return;

        if (!alreadyOpenOnCurrentThread && !TryOpenClipboard(ownerWindow))
        {
            int openError = Marshal.GetLastPInvokeError();
            if (HasStaleClipboardOwnership(stagedSequence, ownerWindow)) return;
            throw new Win32Exception(openError);
        }

        try
        {
            if (staged)
            {
                RestoreSnapshotWhileOpen(
                    snapshot ?? throw new InvalidOperationException("The staged clipboard snapshot is missing."),
                    stagedSequence,
                    _textReader,
                    ownerWindow);
            }
        }
        finally
        {
            if (!CloseClipboard()) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static bool IsClipboardOpenOnCurrentThread()
    {
        Marshal.SetLastPInvokeError(0);
        EnumClipboardFormats(0);
        return Marshal.GetLastPInvokeError() == 0;
    }

    private static bool HasStaleClipboardOwnership(uint stagedSequence, IntPtr ownerWindow)
    {
        if (GetClipboardSequenceNumber() != stagedSequence) return true;
        if (!ClipboardOwnerWindow.IsAlive)
        {
            throw new InvalidOperationException("The clipboard owner window was destroyed before restoration.");
        }
        return GetClipboardOwner() != ownerWindow;
    }

    internal static bool RestoreSnapshotWhileOpen(
        Snapshot snapshot,
        uint stagedSequence,
        ClipboardTextReader? textReader,
        IntPtr ownerWindow)
    {
        uint currentSequence = GetClipboardSequenceNumber();
        if (currentSequence != stagedSequence) return false;
        if (!ClipboardOwnerWindow.IsAlive)
        {
            throw new InvalidOperationException("The clipboard owner window was destroyed before restoration.");
        }
        if (GetClipboardOwner() != ownerWindow) return false;

        if (!TryAllocateFormats(snapshot, out IntPtr[] handles))
        {
            throw new InvalidOperationException("Could not allocate saved clipboard formats for restoration.");
        }

        bool tracking = false;
        try
        {
            textReader?.BeginOwnWrite(currentSequence);
            tracking = textReader is not null;

            if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastPInvokeError());

            for (int index = 0; index < snapshot.Formats.Length; index++)
            {
                SavedFormat format = snapshot.Formats[index];
                if (SetClipboardData(format.Format, handles[index]) == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                handles[index] = IntPtr.Zero; // clipboard takes ownership on success
            }

            return true;
        }
        finally
        {
            if (tracking) textReader!.EndOwnWrite(GetClipboardSequenceNumber(), containsOwnText: false);
            FreeUntransferred(handles);
        }
    }

    internal static bool TryAllocateFormats(Snapshot snapshot, out IntPtr[] handles)
    {
        handles = new IntPtr[snapshot.Formats.Length];
        try
        {
            for (int index = 0; index < snapshot.Formats.Length; index++)
            {
                handles[index] = AllocateGlobal(snapshot.Formats[index].Bytes);
            }
            return true;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or Win32Exception)
        {
            FreeUntransferred(handles);
            handles = [];
            return false;
        }
        catch
        {
            FreeUntransferred(handles);
            throw;
        }
    }

    internal static IntPtr AllocateGlobal(byte[] bytes)
    {
        IntPtr handle = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)bytes.Length);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError());

        IntPtr destination = GlobalLock(handle);
        if (destination == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            GlobalFree(handle);
            throw new Win32Exception(error);
        }

        bool copied = false;
        try
        {
            Marshal.Copy(bytes, 0, destination, bytes.Length);
            copied = true;
            return handle;
        }
        finally
        {
            GlobalUnlock(handle);
            if (!copied) GlobalFree(handle);
        }
    }

    internal static void FreeUntransferred(IntPtr[] handles)
    {
        foreach (IntPtr handle in handles)
        {
            if (handle != IntPtr.Zero) GlobalFree(handle);
        }
    }

    private static bool TryOpenClipboard(IntPtr ownerWindow)
    {
        const int OpenAttempts = 4;
        const int OpenRetryMs = 5;
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(ownerWindow)) return true;
            if (attempt + 1 < OpenAttempts) Thread.Sleep(OpenRetryMs);
        }
        return false;
    }

    internal sealed record Snapshot(SavedFormat[] Formats)
    {
        public static Snapshot Empty { get; } = new([]);
    }

    internal readonly record struct SavedFormat(uint Format, byte[] Bytes);
}

/// <summary>Pure allowlist for clipboard formats whose payload contract is movable global memory.</summary>
internal static class ClipboardFormatCopyPolicy
{
    public static bool IsStandardGlobalFormat(uint format) =>
        format is ClipboardNativeMethods.CF_TEXT
            or ClipboardNativeMethods.CF_SYLK
            or ClipboardNativeMethods.CF_DIF
            or ClipboardNativeMethods.CF_TIFF
            or ClipboardNativeMethods.CF_OEMTEXT
            or ClipboardNativeMethods.CF_DIB
            or ClipboardNativeMethods.CF_PENDATA
            or ClipboardNativeMethods.CF_RIFF
            or ClipboardNativeMethods.CF_WAVE
            or ClipboardNativeMethods.CF_UNICODETEXT
            or ClipboardNativeMethods.CF_HDROP
            or ClipboardNativeMethods.CF_LOCALE
            or ClipboardNativeMethods.CF_DIBV5;

    public static bool IsSupportedRegisteredName(string? name) =>
        string.Equals(name, "HTML Format", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Rich Text Format", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Owns the saved private clipboard copy until the paste has had time to finish. Disposal restores
/// it only while a fresh exclusive open still sees the sequence this lease staged; a user copy
/// therefore wins even when it happens before the host consumes Ctrl+V.
/// </summary>
internal sealed class Win32ClipboardPasteLease : IClipboardPasteLease
{
    private readonly uint _stagedSequence;
    private readonly ClipboardTextReader? _textReader;
    private Win32ClipboardPasteLeaseFactory.Snapshot? _snapshot;
    private int _disposed;

    public Win32ClipboardPasteLease(
        Win32ClipboardPasteLeaseFactory.Snapshot snapshot,
        uint stagedSequence,
        ClipboardTextReader? textReader)
    {
        _snapshot = snapshot;
        _stagedSequence = stagedSequence;
        _textReader = textReader;
    }

    public bool IsCurrent => Volatile.Read(ref _disposed) == 0
        && ClipboardOwnerWindow.IsAlive
        && GetClipboardSequenceNumber() == _stagedSequence
        && GetClipboardOwner() == ClipboardOwnerWindow.Handle;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        Win32ClipboardPasteLeaseFactory.Snapshot? snapshot = Interlocked.Exchange(ref _snapshot, null);
        if (snapshot is null) return ValueTask.CompletedTask;

        return new ValueTask(Task.Run(() => RestoreIfCurrent(snapshot), CancellationToken.None));
    }

    private void RestoreIfCurrent(Win32ClipboardPasteLeaseFactory.Snapshot snapshot)
    {
        IntPtr ownerWindow = ClipboardOwnerWindow.Handle;
        if (HasStaleClipboardOwnership(ownerWindow)) return;

        if (!TryOpenClipboard(ownerWindow, out int openError))
        {
            // A copy can land while OpenClipboard is retrying. If ownership moved, leave that
            // newer clipboard alone and report no cleanup error for the stale lease.
            if (HasStaleClipboardOwnership(ownerWindow)) return;
            throw new Win32Exception(openError);
        }

        try
        {
            // Opening the clipboard excludes intervening writers, making this sequence check and
            // restoration one transaction. IsCurrent alone is advisory; this is the authoritative
            // guard against overwriting a copy made in the narrow interval after that check.
            Win32ClipboardPasteLeaseFactory.RestoreSnapshotWhileOpen(
                snapshot,
                _stagedSequence,
                _textReader,
                ownerWindow);
        }
        finally
        {
            if (!CloseClipboard()) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static bool TryOpenClipboard(IntPtr ownerWindow, out int lastError)
    {
        const int OpenAttempts = 4;
        const int OpenRetryMs = 5;
        lastError = 0;
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(ownerWindow)) return true;
            lastError = Marshal.GetLastPInvokeError();
            if (attempt + 1 < OpenAttempts) Thread.Sleep(OpenRetryMs);
        }
        return false;
    }

    private bool HasStaleClipboardOwnership(IntPtr ownerWindow)
    {
        if (GetClipboardSequenceNumber() != _stagedSequence) return true;
        if (!ClipboardOwnerWindow.IsAlive)
        {
            throw new InvalidOperationException("The clipboard owner window was destroyed before restoration.");
        }
        return GetClipboardOwner() != ownerWindow;
    }
}

internal interface IClipboardPasteLeaseFactory
{
    Task<IClipboardPasteLease?> PrepareAsync(string text, CancellationToken ct);
}
