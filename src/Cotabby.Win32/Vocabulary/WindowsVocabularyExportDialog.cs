using System.Runtime.InteropServices;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// One STA save-panel invocation owns its Shell dialog RCW. This replaces NSSavePanel while keeping
/// the captured package's filename/type. Cancellation is marshalled to the owner before Close;
/// stale callbacks cannot affect another invocation. It only returns a filesystem path: archive
/// destination validation and all file creation remain separate. No global dialog/listener exists.
/// </summary>
public sealed class WindowsVocabularyExportDialog : IDisposable
{
    private const int Cancelled = unchecked((int)0x800704c7);
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IFileDialog _dialog;
    private bool _showing, _shown, _disposed;
    public WindowsVocabularyExportDialog(VocabularyExportConfiguration options)
    {
        if (!OperatingSystem.IsWindows() || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("An STA Windows owner is required.");
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new("c0b4e2f3-ba21-4773-8dba-335ec946eb8b"), throwOnError: true)!)!;
        try
        {
            _dialog = (IFileDialog)instance;
            string extension = options.IncludesImages ? "zip" : options.Format.FileExtension();
            _dialog.GetOptions(out uint flags);
            // The Shell must not probe-create a chosen destination before the archive protects
            // its database/media names. Only filesystem items and the one captured type enter Save.
            _dialog.SetOptions(flags | 0x2 | 0x4 | 0x8 | 0x40 | 0x800 | 0x8000 | 0x10000);
            _dialog.SetFileTypes(1, [new(extension.ToUpperInvariant(), "*." + extension)]);
            _dialog.SetFileTypeIndex(1); _dialog.SetDefaultExtension(extension);
            _dialog.SetFileName("Lingrove-vocabulary." + extension);
        }
        catch { Marshal.FinalReleaseComObject(instance); throw; }
    }
    /// <summary>postToOwner must enqueue on the creating UI thread, or run inline there. Shell
    /// Show pumps that thread; Close retires only this invocation and never simulates a keypress.</summary>
    public string? Show(IntPtr owner, Action<Action> postToOwner, CancellationToken cancellationToken = default)
    {
        Verify(); if (_shown) throw new InvalidOperationException("An export dialog is single-use.");
        cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(postToOwner);
        if (owner == IntPtr.Zero) throw new ArgumentException("An export dialog requires its owning window.", nameof(owner));
        _shown = true; _showing = true;
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                postToOwner(() =>
                {
                    VerifyThread(); if (_disposed || !_showing) return;
                    try { _dialog.Close(Cancelled); } catch (COMException) { /* Native Show may already have returned. */ }
                });
            }
            catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { }
        });
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); int result = _dialog.Show(owner);
            if (result == Cancelled || cancellationToken.IsCancellationRequested) return null;
            Marshal.ThrowExceptionForHR(result);
            _dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(0x80058000, out IntPtr path); // SIGDN_FILESYSPATH, allocated by the Shell.
                try { return Marshal.PtrToStringUni(path) ?? throw new InvalidOperationException("No export destination was returned."); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { _showing = false; }
    }
    /// <summary>Noninteractive native startup probe; no Show, destination or file effect.</summary>
    internal (string Filename, uint Options, uint TypeIndex) ReadConfiguration()
    { Verify(); _dialog.GetFileName(out string name); _dialog.GetOptions(out uint flags); _dialog.GetFileTypeIndex(out uint index); return (name, flags, index); }
    private void VerifyThread()
    { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("The export dialog belongs to its creating UI thread."); }
    private void Verify() { VerifyThread(); ObjectDisposedException.ThrowIf(_disposed, this); }
    public void Dispose()
    {
        VerifyThread(); if (_disposed) return;
        if (_showing) throw new InvalidOperationException("Close the modal invocation before disposing its dialog.");
        _disposed = true; Marshal.FinalReleaseComObject(_dialog);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct FilterSpec(string name, string pattern)
    {
        [MarshalAs(UnmanagedType.LPWStr)] public readonly string Name = name;
        [MarshalAs(UnmanagedType.LPWStr)] public readonly string Pattern = pattern;
    }
    // SDK order is ABI, including unused slots. These declarations retain the inherited
    // IModalWindow.Show slot before IFileDialog and borrow no unmanaged pointer across an await.
    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] filters);
        void SetFileTypeIndex(uint index); void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie); void Unadvise(uint cookie);
        void SetOptions(uint options); void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item); void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item); void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetResult(out IShellItem item); void AddPlace(IShellItem item, uint location);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result); void SetClientGuid(in Guid id); void ClearClientData(); void SetFilter(IntPtr filter);
    }
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, in Guid handler, in Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent); void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes); void Compare(IShellItem other, uint hint, out int order);
    }
}
