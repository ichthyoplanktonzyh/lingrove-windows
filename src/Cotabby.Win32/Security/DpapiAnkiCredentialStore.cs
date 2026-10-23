using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Cotabby.Core.Review;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Security;

/// <summary>App-owned Windows counterpart of the independent Anki Keychain item. Construction
/// performs no I/O; explicit settings actions borrow this synchronous boundary. Only ciphertext
/// reaches its separate file, protected for the current Windows user. Keys retain exact UTF-8,
/// including whitespace/NUL; only an empty string deletes. Failures expose no path or key data.</summary>
public sealed class DpapiAnkiCredentialStore(string? filePath = null) : IAnkiCredentialStore
{
    internal static string DefaultFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cotabby", "anki-credential.bin");
    internal static ReadOnlySpan<byte> Entropy => "Lingrove.anki-connect.api-key.v1"u8;
    public const string FailureMessage = "Could not access the Anki API key in Windows credential storage.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _filePath = filePath ?? DefaultFilePath;
    private readonly Func<Guid> _stageIdentity = Guid.NewGuid;
    private readonly Action<string>? _afterFlush;
    internal DpapiAnkiCredentialStore(string filePath, Func<Guid> stageIdentity, Action<string>? afterFlush = null) : this(filePath)
    { _stageIdentity = stageIdentity; _afterFlush = afterFlush; }

    public string ReadKey()
    {
        byte[]? plain = null;
        try
        {
            string path = ResolvePath();
            // File.Exists hides permission errors as absence. Only a real missing item maps to
            // the source's empty result; unreadable/corrupt files must remain explicit failures.
            byte[] encrypted;
            try
            {
                if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw Failure();
                encrypted = File.ReadAllBytes(path);
            }
            catch (FileNotFoundException) { return ""; }
            catch (DirectoryNotFoundException) { return ""; }
            plain = ProtectedData.Unprotect(encrypted, Entropy.ToArray(), DataProtectionScope.CurrentUser);
            return StrictUtf8.GetString(plain);
        }
        catch (Exception error) when (IsStorageFailure(error)) { throw Failure(); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    public void SaveKey(string value)
    {
        byte[]? plain = null;
        try
        {
            string path = ResolvePath();
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0) { File.Delete(path); return; }
            // Validate/encode before touching a destination. Unlike Encoding.UTF8's default,
            // strict encoding cannot silently replace a malformed UTF-16 key with another key.
            plain = StrictUtf8.GetBytes(value);
            byte[] encrypted = ProtectedData.Protect(plain, Entropy.ToArray(), DataProtectionScope.CurrentUser);
            string parent = Path.GetDirectoryName(path)!;
            CreateParent(parent);
            WriteEncrypted(path, encrypted);
        }
        catch (Exception error) when (IsStorageFailure(error)) { throw Failure(); }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private void WriteEncrypted(string path, byte[] encrypted)
    {
        // A read-capable directory handle without delete sharing pins the publication parent.
        // Rename/disposition below target the created file HANDLE, never a reopened random name.
        using var parent = CreateFileW(Path.GetDirectoryName(path)!, 0x80000000, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (parent.IsInvalid) throw Failure();
        var name = new StringBuilder(32768);
        uint count = GetFinalPathNameByHandleW(parent, name, (uint)name.Capacity, 0);
        if (count == 0 || count >= name.Capacity) throw Failure();
        string destination = Path.Combine(name.ToString(), Path.GetFileName(path));
        string candidate = Path.Combine(name.ToString(), ".anki-key-" + _stageIdentity().ToString("N"));
        FileStream? stream = null; bool committed = false;
        try
        {
            stream = new FileInfo(candidate).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.None, FileSecurity());
            stream.Write(encrypted); stream.Flush(flushToDisk: true); _afterFlush?.Invoke(candidate);
            byte[] target = Encoding.Unicode.GetBytes(destination);
            // FILE_RENAME_INFO aligns the HANDLE after its BOOLEAN/flags union, followed by
            // a DWORD byte count and the UTF-16 destination. Windows copies this owned buffer.
            int lengthOffset = IntPtr.Size * 2, nameOffset = lengthOffset + 4;
            byte[] info = new byte[Math.Max(nameOffset + target.Length + 2, IntPtr.Size == 8 ? 24 : 16)];
            BinaryPrimitives.WriteUInt32LittleEndian(info, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(lengthOffset), (uint)target.Length);
            target.CopyTo(info, nameOffset);
            if (!SetFileInformationByHandle(stream.SafeFileHandle, 3, info, (uint)info.Length)) throw Failure();
            committed = true;
        }
        finally
        {
            if (stream is not null)
            {
                // CreateNew failure grants no ownership. On later failure, disposition marks
                // only our retained handle for deletion. Do not unlink a path another caller
                // could have replaced. After commit, close failure cannot revoke the saved key.
                if (!committed) _ = SetFileInformationByHandle(stream.SafeFileHandle, 4, [1], 1);
                try { stream.Dispose(); } catch (Exception error) when (IsStorageFailure(error)) { }
            }
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, [In] byte[] info, uint size);

    private string ResolvePath()
    {
        if (!OperatingSystem.IsWindows()) throw Failure();
        string leaf = Path.GetFileName(_filePath);
        if (string.IsNullOrEmpty(leaf) || leaf.Contains(':') || leaf.EndsWith('.') || leaf.EndsWith(' ')) throw Failure();
        return Path.GetFullPath(_filePath);
    }
    private static void CreateParent(string path)
    {
        if (Directory.Exists(path)) return;
        var missing = new Stack<string>();
        for (string? component = path; component is not null && !Directory.Exists(component); component = Path.GetDirectoryName(component)) missing.Push(component);
        using var identity = WindowsIdentity.GetCurrent(); var user = identity.User ?? throw Failure();
        var security = new DirectorySecurity(); security.SetOwner(user); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        while (missing.TryPop(out string? component)) new DirectoryInfo(component).Create(security);
    }
    private static FileSecurity FileSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent(); var user = identity.User ?? throw Failure();
        var security = new FileSecurity(); security.SetOwner(user); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
    private static bool IsStorageFailure(Exception error) => error is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException or SecurityException;
    private static AnkiReviewException Failure() => new(FailureMessage);
}
