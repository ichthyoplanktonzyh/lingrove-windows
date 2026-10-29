using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

/// <summary>Pins the selected app, its CLI sidecar and their resolved ancestors while creating
/// the child process. This establishes path/file identity, not publisher authenticity.</summary>
internal sealed class PinnedCommand : IDisposable
{
    private const uint Read = 0x80000000, OpenExisting = 3, OpenReparse = 0x00200000, Backup = 0x02000000;
    private readonly List<SafeFileHandle> _handles = [];
    private readonly string _app;
    private readonly FileId _appId, _cliId;
    public string CliPath { get; }
    private PinnedCommand(string app, FileId appId, string cli, FileId cliId, IEnumerable<SafeFileHandle> handles)
    { _app = app; _appId = appId; CliPath = cli; _cliId = cliId; _handles.AddRange(handles); }
    public static PinnedCommand Open(string app)
    {
        var handles = new List<SafeFileHandle>();
        try
        {
            var appHandle = OpenFile(app); handles.Add(appHandle); var appId = Identity(appHandle, false); string appFinal = FinalPath(appHandle);
            if (!string.Equals(Path.GetFileName(appFinal), "Cotabby.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException();
            string cli = Path.Combine(Path.GetDirectoryName(appFinal)!, "lingrove.exe");
            var cliHandle = OpenFile(cli); handles.Add(cliHandle); var cliId = Identity(cliHandle, false); string cliFinal = FinalPath(cliHandle);
            if (!string.Equals(cliFinal, cli, StringComparison.OrdinalIgnoreCase)) throw new IOException();
            string? parent = Path.GetDirectoryName(appFinal);
            while (!string.IsNullOrEmpty(parent))
            {
                var directory = CreateFileW(parent, Read, 3, IntPtr.Zero, OpenExisting, OpenReparse | Backup, IntPtr.Zero);
                if (directory.IsInvalid) { directory.Dispose(); throw new IOException(); }
                handles.Add(directory); _ = Identity(directory, true);
                string? next = Path.GetDirectoryName(parent); if (string.Equals(parent, next, StringComparison.OrdinalIgnoreCase)) break; parent = next;
            }
            var result = new PinnedCommand(appFinal, appId, cliFinal, cliId, handles); result.Verify(); return result;
        }
        catch { foreach (var handle in handles) handle.Dispose(); throw; }
    }
    public void Verify()
    {
        using var app = OpenFile(_app); using var cli = OpenFile(CliPath);
        if (Identity(app, false) != _appId || Identity(cli, false) != _cliId
            || !string.Equals(FinalPath(app), _app, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(FinalPath(cli), CliPath, StringComparison.OrdinalIgnoreCase)) throw new IOException();
    }
    public void Dispose() { foreach (var handle in _handles) handle.Dispose(); }
    private static SafeFileHandle OpenFile(string path)
    {
        var handle = CreateFileW(path, Read, 1, IntPtr.Zero, OpenExisting, OpenReparse | Backup, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException(); } return handle;
    }
    private static FileId Identity(SafeFileHandle handle, bool directory)
    {
        if (GetFileType(handle) != 1 || !GetAttributes(handle, 9, out var attributes, (uint)Marshal.SizeOf<Attributes>())
            || (attributes.Value & (0x10u | 0x400u)) != (directory ? 0x10u : 0u)
            || !GetIdentity(handle, 18, out var id, (uint)Marshal.SizeOf<FileId>())) throw new IOException();
        return id;
    }
    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768); uint size = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (size == 0 || size >= buffer.Capacity) throw new IOException();
        string path = buffer.ToString();
        if (!(path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
            || path.Length >= 7 && path.StartsWith(@"\\?\", StringComparison.Ordinal) && char.IsAsciiLetter(path[4]) && path[5] == ':' && path[6] == '\\')) throw new IOException();
        return path;
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct FileId(ulong Volume, ulong Low, ulong High);
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public uint Value, ReparseTag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetAttributes(SafeFileHandle file, int type, out Attributes result, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIdentity(SafeFileHandle file, int type, out FileId result, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
