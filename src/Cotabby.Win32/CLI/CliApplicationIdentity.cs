using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Win32.CLI;

/// <summary>Separates app installations by the final opened executable path and file identity. Both client and app
/// compute the same identity without reading notebook data; the caller explicitly selects an app.</summary>
public sealed record CliApplicationIdentity(string Executable, string MailboxRoot)
{
    public static CliApplicationIdentity Resolve(string executable, string? applicationData = null)
    {
        string path = System.IO.Path.GetFullPath(executable);
        if (!string.Equals(System.IO.Path.GetFileName(path), "Cotabby.exe", StringComparison.OrdinalIgnoreCase))
            throw new CliException(CliErrorCode.Unavailable, "Select a Cotabby.exe application.");
        using var handle = VocabularyWindowsFile.Native.CreateFileW(path, VocabularyWindowsFile.Native.Read, VocabularyWindowsFile.Native.ShareReadWrite,
            IntPtr.Zero, VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new CliException(CliErrorCode.Unavailable, "Cannot identify the selected application.");
        VocabularyWindowsFile.FileIdentity identity;
        try { identity = VocabularyWindowsFile.Identity(handle); }
        catch (VocabularyException) { throw new CliException(CliErrorCode.Unavailable, "Cannot identify the selected application."); }
        var final = new StringBuilder(32768);
        uint count = VocabularyWindowsFile.Native.GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
        if (count == 0 || count >= final.Capacity) throw new CliException(CliErrorCode.Unavailable, "Cannot identify the selected application.");
        string resolved = final.ToString();
        if (!(resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || resolved.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
            || resolved.Length >= 7 && resolved.StartsWith(@"\\?\", StringComparison.Ordinal) && char.IsAsciiLetter(resolved[4]) && resolved[5] == ':' && resolved[6] == '\\'))
            throw new CliException(CliErrorCode.Unavailable, "Cannot identify the selected application.");
        string seed = resolved.ToUpperInvariant() + "\0" + identity.Volume.ToString("X16") + identity.Low.ToString("X16") + identity.High.ToString("X16");
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
        string data = applicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new(resolved, System.IO.Path.Combine(data, "Cotabby", "Automation-v1", hash));
    }
}
