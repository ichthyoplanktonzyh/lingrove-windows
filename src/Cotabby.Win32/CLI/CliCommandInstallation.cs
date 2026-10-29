using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Cotabby.Win32.CLI;

/// <summary>Explicit user-local command installation. Owned launcher files are privately and
/// exclusively published before the command; foreign files are never replaced and PATH is untouched.</summary>
public static class CliCommandInstallation
{
    private static readonly string[] LauncherFiles = ["lingrove-launcher.exe", "lingrove-launcher.dll", "lingrove-launcher.deps.json", "lingrove-launcher.runtimeconfig.json"];
    public static string Install(string appPath, string? directory = null)
    {
        using var app = CliApplicationSelection.Open(appPath);
        string folder = System.IO.Path.GetDirectoryName(app.Identity.Executable)!;
        var cliFiles = new[] { "lingrove.exe", "lingrove.dll", "lingrove.deps.json", "lingrove.runtimeconfig.json" };
        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (string name in cliFiles.Concat(LauncherFiles))
            {
                var handle = VocabularyWindowsFile.Native.CreateFileW(System.IO.Path.Combine(folder, name), VocabularyWindowsFile.Native.Read, 1, IntPtr.Zero,
                    VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw Unavailable(); }
                try { _ = VocabularyWindowsFile.Identity(handle); } catch (VocabularyException) { handle.Dispose(); throw Unavailable(); }
                handles.Add(handle);
            }
            var payload = new List<byte[]>();
            foreach (var handle in handles.Skip(cliFiles.Length))
            {
                // Duplicate the read position only through the held file: each handle is used once.
                using var stream = new FileStream(handle, FileAccess.Read); if (stream.Length > 8 * 1024 * 1024) throw Unavailable();
                using var bytes = new MemoryStream(); stream.CopyTo(bytes); payload.Add(bytes.ToArray());
            }
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var bytes in payload) { digest.AppendData(BitConverter.GetBytes(bytes.Length)); digest.AppendData(bytes); }
            string version = "launcher-v1-" + Convert.ToHexStringLower(digest.GetHashAndReset());
            string targetDirectory = directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cotabby", "bin");
            using var root = VocabularyMediaDirectory.Open(targetDirectory, true);
            using var launcher = VocabularyMediaDirectory.Open(System.IO.Path.Combine(root.Path, version), true);
            for (int index = 0; index < LauncherFiles.Length; index++) EnsureFile(launcher, LauncherFiles[index], payload[index]);
            app.Verify(); root.Verify(); launcher.Verify();
            byte[] script = Encoding.ASCII.GetBytes(BuildScript(version, app.Identity.Executable));
            EnsureFile(root, "lingrove.cmd", script);
            return System.IO.Path.Combine(targetDirectory, "lingrove.cmd");
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
    }
    private static void EnsureFile(VocabularyMediaDirectory root, string name, byte[] bytes)
    {
        using (var existing = root.OpenFile(name))
        {
            if (existing is not null)
            {
                using var stream = new FileStream(existing, FileAccess.Read); if (stream.Length != bytes.Length) throw Occupied();
                byte[] current = new byte[bytes.Length]; stream.ReadExactly(current); root.Verify();
                if (!current.AsSpan().SequenceEqual(bytes)) throw Occupied(); return;
            }
        }
        string stage = System.IO.Path.Combine(root.Path, ".lingrove-install-" + Guid.NewGuid().ToString("N")); FileStream? output = null; bool published = false;
        try
        {
            output = new FileInfo(stage).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, VocabularyWindowsFile.NewFileSecurity());
            output.Write(bytes); output.Flush(true); root.Verify(); CliMailboxDirectory.Rename(output.SafeFileHandle, System.IO.Path.Combine(root.Path, name)); published = true;
        }
        finally { if (output is not null) { if (!published) _ = SetFileInformationByHandle(output.SafeFileHandle, 4, [1], 1); output.Dispose(); } }
    }
    internal static string BuildScript(string version, string app)
    {
        if (!version.StartsWith("launcher-v1-", StringComparison.Ordinal) || version.Length != 76 || version[12..].Any(c => !char.IsAsciiHexDigit(c))) throw Unavailable();
        string encoded = Convert.ToBase64String(new UTF8Encoding(false, true).GetBytes(app));
        if (encoded.Length > 65536) throw Unavailable();
        return "@echo off\r\nsetlocal DisableDelayedExpansion\r\nset \"LINGROVE_CLI_APP_B64=" + encoded + "\"\r\n\"%~dp0" + version + "\\lingrove-launcher.exe\" %*\r\nexit /b %errorlevel%\r\n";
    }
    private static CliException Unavailable() => new(CliErrorCode.Unavailable, "Cannot safely install the command for this app. Use lingrove.exe beside the app.");
    private static CliException Occupied() => new(CliErrorCode.Conflict, "The command path already contains a different file. Choose another location.");
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, [In] byte[] info, uint size);
}
