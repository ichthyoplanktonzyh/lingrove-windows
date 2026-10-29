using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Cotabby.Win32.CLI;

/// <summary>Holds the selected executable without delete sharing across validation. A leaf
/// replacement is refused; ancestor retargeting and process creation require further checks.
/// This proves file identity, not publisher trust.</summary>
public sealed class CliApplicationSelection : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly string _requested;
    private readonly string? _applicationData;
    private readonly VocabularyWindowsFile.FileIdentity _file;
    private readonly IReadOnlyList<SafeFileHandle> _parents;
    public CliApplicationIdentity Identity { get; }
    private CliApplicationSelection(string path, SafeFileHandle handle, CliApplicationIdentity identity, string? applicationData, VocabularyWindowsFile.FileIdentity file, IReadOnlyList<SafeFileHandle> parents)
    { _requested = path; _handle = handle; Identity = identity; _applicationData = applicationData; _file = file; _parents = parents; }
    public static CliApplicationSelection Open(string executable, string? applicationData = null)
    {
        string path = System.IO.Path.GetFullPath(executable);
        var candidate = VocabularyWindowsFile.Native.CreateFileW(path, VocabularyWindowsFile.Native.Read, 1,
            IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        var parents = new List<SafeFileHandle>();
        try
        {
            if (candidate.IsInvalid) throw new CliException(CliErrorCode.Unavailable, "Cannot identify the selected application.");
            var file = VocabularyWindowsFile.Identity(candidate);
            var final = new System.Text.StringBuilder(32768);
            uint length = VocabularyWindowsFile.Native.GetFinalPathNameByHandleW(candidate, final, (uint)final.Capacity, 0);
            if (length == 0 || length >= final.Capacity) throw new CliException(CliErrorCode.Unavailable, "Cannot identify the selected application.");
            var identity = CliApplicationIdentity.Resolve(final.ToString(), applicationData);
            // Pin every real directory in the resolved path, preventing a concurrent rename
            // or junction swap while CreateProcess resolves that path.
            string? parent = System.IO.Path.GetDirectoryName(identity.Executable);
            while (!string.IsNullOrEmpty(parent))
            {
                var directory = VocabularyWindowsFile.Native.CreateFileW(parent, VocabularyWindowsFile.Native.Read, VocabularyWindowsFile.Native.ShareReadWrite,
                    IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
                if (directory.IsInvalid || !VocabularyWindowsFile.Native.GetAttributes(directory, 9, out var attributes, (uint)Marshal.SizeOf<VocabularyWindowsFile.FileAttributesInfo>())
                    || (attributes.Attributes & (0x10u | 0x400u)) != 0x10u)
                { directory.Dispose(); throw new CliException(CliErrorCode.Unavailable, "Cannot safely hold the application directory."); }
                parents.Add(directory);
                string? next = System.IO.Path.GetDirectoryName(parent);
                if (string.Equals(parent, next, StringComparison.OrdinalIgnoreCase)) break;
                parent = next;
            }
            using var named = VocabularyWindowsFile.Native.CreateFileW(path, VocabularyWindowsFile.Native.Read, VocabularyWindowsFile.Native.ShareReadWrite,
                IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
            if (named.IsInvalid || VocabularyWindowsFile.Identity(named) != file || CliApplicationIdentity.Resolve(path, applicationData) != identity)
                throw new CliException(CliErrorCode.Unavailable, "The selected application changed.");
            return new(path, candidate, identity, applicationData, file, parents);
        }
        catch (VocabularyException) { foreach (var parent in parents) parent.Dispose(); candidate.Dispose(); throw new CliException(CliErrorCode.Unavailable, "Cannot identify the selected application."); }
        catch { foreach (var parent in parents) parent.Dispose(); candidate.Dispose(); throw; }
    }
    public void Verify()
    {
        if (_handle.IsClosed) throw new ObjectDisposedException(nameof(CliApplicationSelection));
        using var named = VocabularyWindowsFile.Native.CreateFileW(_requested, VocabularyWindowsFile.Native.Read, VocabularyWindowsFile.Native.ShareReadWrite,
            IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        try { if (named.IsInvalid || VocabularyWindowsFile.Identity(named) != _file) throw new CliException(CliErrorCode.Unavailable, "The selected application changed."); }
        catch (VocabularyException) { throw new CliException(CliErrorCode.Unavailable, "The selected application changed."); }
        var current = CliApplicationIdentity.Resolve(_requested, _applicationData);
        if (current != Identity) throw new CliException(CliErrorCode.Unavailable, "The selected application changed.");
    }
    public void StartInBackground()
    {
        Verify();
        try
        {
            var start = new ProcessStartInfo(Identity.Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("-lingrove-cli");
            using var process = Process.Start(start) ?? throw new CliException(CliErrorCode.Unavailable, "Cannot launch the selected application.");
            Verify();
        }
        catch (System.ComponentModel.Win32Exception) { throw new CliException(CliErrorCode.Unavailable, "Cannot launch the selected application."); }
    }
    public void Dispose() { _handle.Dispose(); foreach (var parent in _parents) parent.Dispose(); }
}
