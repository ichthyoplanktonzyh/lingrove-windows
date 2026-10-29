using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.CLI;

/// <summary>Explicit per-user command installation. The command points to this app's own CLI,
/// never edits PATH, and never replaces a pre-existing different file.</summary>
public static class CliCommandInstallation
{
    public static string Install(string appPath, string? directory = null)
    {
        using var app = CliApplicationSelection.Open(appPath);
        string folder = System.IO.Path.GetDirectoryName(app.Identity.Executable)!;
        string cli = System.IO.Path.Combine(folder, "lingrove.exe");
        var sidecars = new[] { cli, System.IO.Path.Combine(folder, "lingrove.dll"), System.IO.Path.Combine(folder, "lingrove.deps.json"), System.IO.Path.Combine(folder, "lingrove.runtimeconfig.json") };
        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (string path in sidecars)
            {
                var handle = VocabularyWindowsFile.Native.CreateFileW(path, VocabularyWindowsFile.Native.Read, 1, IntPtr.Zero,
                    VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw Unavailable(); }
                try { _ = VocabularyWindowsFile.Identity(handle); } catch (VocabularyException) { handle.Dispose(); throw Unavailable(); }
                handles.Add(handle);
            }
            app.Verify();
            byte[] script = Encoding.ASCII.GetBytes(BuildScript(AsciiPath(cli), AsciiPath(app.Identity.Executable)));
            string targetDirectory = directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cotabby", "bin");
            using var root = VocabularyMediaDirectory.Open(targetDirectory, true); string target = System.IO.Path.Combine(root.Path, "lingrove.cmd");
            using (var current = root.OpenFile("lingrove.cmd"))
            {
                if (current is not null)
                {
                    using var stream = new FileStream(current, FileAccess.Read); if (stream.Length > 16384) throw Occupied();
                    using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                    if (!bytes.ToArray().AsSpan().SequenceEqual(script)) throw Occupied();
                    return System.IO.Path.Combine(targetDirectory, "lingrove.cmd");
                }
            }
            string stage = System.IO.Path.Combine(root.Path, ".lingrove-install-" + Guid.NewGuid().ToString("N")); FileStream? output = null; bool published = false;
            try
            {
                output = new FileInfo(stage).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, VocabularyWindowsFile.NewFileSecurity());
                output.Write(script); output.Flush(true); app.Verify(); root.Verify(); CliMailboxDirectory.Rename(output.SafeFileHandle, target); published = true;
                return System.IO.Path.Combine(targetDirectory, "lingrove.cmd");
            }
            finally { if (output is not null) { if (!published) _ = SetFileInformationByHandle(output.SafeFileHandle, 4, [1], 1); output.Dispose(); } }
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
    }
    internal static string BuildScript(string cli, string app)
    {
        static string Escape(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(c => c is '"' or '\r' or '\n' or '\0' || c > 127 || c < 32)) throw Unavailable();
            return value.Replace("%", "%%", StringComparison.Ordinal);
        }
        return "@echo off\r\nsetlocal DisableDelayedExpansion\r\nset \"LINGROVE_CLI_APP=" + Escape(app) + "\"\r\n\"" + Escape(cli) + "\" %*\r\nexit /b %errorlevel%\r\n";
    }
    private static string AsciiPath(string path)
    {
        static string Dos(string value) => value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + value[8..]
            : value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
        string candidate = Dos(path); if (candidate.All(c => c is >= ' ' and <= '~')) return candidate;
        var buffer = new StringBuilder(32768); uint size = GetShortPathNameW(path, buffer, (uint)buffer.Capacity);
        if (size == 0 || size >= buffer.Capacity) throw Unavailable(); candidate = Dos(buffer.ToString());
        if (candidate.Any(c => c is < ' ' or > '~')) throw Unavailable(); return candidate;
    }
    private static CliException Unavailable() => new(CliErrorCode.Unavailable, "Cannot safely install the command for this app path. Use lingrove.exe beside the app.");
    private static CliException Occupied() => new(CliErrorCode.Conflict, "The command path already contains a different file. Choose another location.");
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetShortPathNameW(string path, StringBuilder result, uint capacity);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, [In] byte[] info, uint size);
}
