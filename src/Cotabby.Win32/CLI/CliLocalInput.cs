using System.IO;
using System.Text.Json.Nodes;
using System.Text;
using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Win32.CLI;

/// <summary>Explicitly requested client input only; all bytes are read from admitted regular
/// handles, and images are attached before a request enters the app-owned mailbox.</summary>
public static class CliLocalInput
{
    public static async Task<byte[]> ReadAsync(string path, int maximum, CancellationToken cancellationToken = default)
    {
        if (maximum < 0 || maximum > CliInputContract.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximum));
        return await Task.Run(() => Read(path, maximum, cancellationToken), cancellationToken).ConfigureAwait(false);
    }
    private static byte[] Read(string path, int maximum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\?\GLOBALROOT\", StringComparison.OrdinalIgnoreCase))
            throw new CliException(CliErrorCode.InvalidInput, "Expected a regular file within the byte limit.");
        using var handle = VocabularyWindowsFile.Native.CreateFileW(path, VocabularyWindowsFile.Native.Read, VocabularyWindowsFile.Native.ShareReadWrite, IntPtr.Zero,
            VocabularyWindowsFile.Native.OpenExisting, VocabularyWindowsFile.Native.OpenReparsePoint | VocabularyWindowsFile.Native.BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new CliException(CliErrorCode.Io, "Cannot read the requested file.");
        try
        {
            _ = VocabularyWindowsFile.Identity(handle);
            var final = new StringBuilder(32768);
            uint length = VocabularyWindowsFile.Native.GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
            string name = final.ToString();
            if (length == 0 || length >= final.Capacity || !(name.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
                || name.Length >= 7 && name.StartsWith(@"\\?\", StringComparison.Ordinal) && char.IsAsciiLetter(name[4]) && name[5] == ':' && name[6] == '\\'))
                throw new CliException(CliErrorCode.InvalidInput, "Expected a regular file within the byte limit.");
        }
        catch (VocabularyException) { throw new CliException(CliErrorCode.InvalidInput, "Expected a regular file within the byte limit."); }
        using var stream = new FileStream(handle, FileAccess.Read); if (stream.Length < 0 || stream.Length > maximum) throw new CliException(CliErrorCode.InvalidInput, "Expected a regular file within the byte limit.");
        using var result = new MemoryStream((int)stream.Length); byte[] buffer = new byte[65536];
        while (true)
        {
            token.ThrowIfCancellationRequested(); int size = stream.Read(buffer, 0, Math.Min(buffer.Length, maximum + 1 - (int)result.Length));
            if (size == 0) break; result.Write(buffer, 0, size);
            if (result.Length > maximum) throw new CliException(CliErrorCode.InvalidInput, "File exceeds the byte limit.");
        }
        token.ThrowIfCancellationRequested(); return result.ToArray();
    }
    public static async Task<CliBatch> AttachImagesAsync(CliBatch batch, string baseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var value = batch.CopyValue(); int total = 0;
        foreach (var op in value["operations"]!.AsArray())
        {
            var action = op!["add"] ?? op["append"];
            if (action?["content"] is not JsonArray content) continue;
            foreach (var block in content)
            {
                if (block?["image"] is not JsonObject image) continue;
                string path = image["path"]?.GetValue<string>() ?? throw new CliException(CliErrorCode.InvalidInput, "An image needs a local path.");
                if (path.Length == 0 || image["data"] is not null) throw new CliException(CliErrorCode.InvalidInput, "An image needs a local path.");
                if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
                    path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length == 1 ? "" : path[2..]);
                path = System.IO.Path.GetFullPath(path, baseDirectory);
                byte[] bytes = await ReadAsync(path, CliInputContract.MaximumImageBytes - total, cancellationToken).ConfigureAwait(false);
                total += bytes.Length; image.Remove("path"); image["data"] = Convert.ToBase64String(bytes);
            }
        }
        return CliInputContract.ValidateWire(value);
    }
}
