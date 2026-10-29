using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Cotabby.Win32.Vocabulary;
using Microsoft.Win32.SafeHandles;

namespace Cotabby.Win32.Dictionary;

/// <summary>Owns one newly created private WebView profile. The exact directory and its physical
/// ancestors stay pinned until cleanup; no pathname-based recursive delete is ever performed.</summary>
public sealed class WindowsPrivateProfileDirectory : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<SafeFileHandle> _parents;
    private SafeFileHandle? _rootPin, _rootDelete;
    private readonly SafeFileHandle _identity;
    private readonly string _leaf;
    private Task? _deletion;
    private Task? _disposal;
    private bool _deleted, _disposed;
    private readonly Dictionary<string, (ulong Low, ulong High)> _observed = new(StringComparer.OrdinalIgnoreCase);
    private long _observedNameBytes;
    internal Action<string>? AfterEnumeration { get; set; }
    internal Action? AfterPinReleased { get; set; }
    internal IReadOnlyList<SafeFileHandle> HandlesForTest => _parents.Concat(new[] { _rootPin, _rootDelete, _identity }.OfType<SafeFileHandle>()).ToArray();
    public string Path { get; }
    private WindowsPrivateProfileDirectory(List<SafeFileHandle> parents, SafeFileHandle root, SafeFileHandle identity, string leaf, string path)
    { _parents = parents; _rootPin = root; _identity = identity; _leaf = leaf; Path = path; }

    /// <summary>The parent must already exist and be private. Creation does not adopt an existing leaf.</summary>
    public static WindowsPrivateProfileDirectory Create(string parent)
        => CreateCore(parent, null);
    internal static WindowsPrivateProfileDirectory CreateForTest(string parent, Action<SafeFileHandle, int> afterOpen)
        => CreateCore(parent, afterOpen);
    private static WindowsPrivateProfileDirectory CreateCore(string parent, Action<SafeFileHandle, int>? afterOpen)
    {
        string full = System.IO.Path.GetFullPath(parent); var pins = new List<SafeFileHandle>(); SafeFileHandle? root = null, identityHandle = null; bool creationHandle = false; string? createdLeaf = null;
        try
        {
            using var resolved = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(full), 0x80000000, 3,
                IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
            Validate(resolved, directory: true, allowLink: false);
            var final = new StringBuilder(32768); uint length = VocabularyWindowsFile.Native.GetFinalPathNameByHandleW(resolved, final, (uint)final.Capacity, 0);
            if (length == 0 || length >= final.Capacity) throw Invalid();
            string canonical = final.ToString();
            if (!canonical.StartsWith(@"\\?\", StringComparison.Ordinal) || canonical.Length < 7 || canonical[5] != ':' || canonical[6] != '\\') throw Invalid();
            VocabularyWindowsFile.CheckParentInheritance(canonical);
            var volume = VocabularyWindowsFile.Native.CreateFileW(canonical[..7], 0x80000000, 3, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
            pins.Add(volume); afterOpen?.Invoke(volume, pins.Count - 1); Validate(volume, true, false);
            foreach (string component in canonical[7..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = OpenRelative(pins[^1], component, create: false, delete: false, null); pins.Add(next); afterOpen?.Invoke(next, pins.Count - 1); Validate(next, true, false);
            }
            if (Identity(pins[^1]) != Identity(resolved)) throw Invalid();
            VocabularyWindowsFile.CheckParentInheritance(canonical);
            using var identity = WindowsIdentity.GetCurrent(); var user = identity.User ?? throw Invalid();
            var security = new DirectorySecurity(); security.SetOwner(user); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            string leaf = "profile-" + Guid.NewGuid().ToString("N");
            root = OpenRelative(pins[^1], leaf, create: true, delete: true, security.GetSecurityDescriptorBinaryForm());
            creationHandle = true; createdLeaf = leaf;
            Validate(root, true, false); ValidatePrivateAcl(root, user);
            identityHandle = OpenRelative(pins[^1], leaf, create: false, delete: false, descriptor: null, share: 7);
            Validate(identityHandle, true, false); if (Identity(identityHandle) != Identity(root)) throw Invalid();
            // Release create-time DELETE before WebView use. The share-delete identity handle
            // prevents ID reuse while the read-only deny-delete pin is installed and verified.
            root.Dispose(); root = null; creationHandle = false;
            root = OpenRelative(pins[^1], leaf, create: false, delete: false, descriptor: null);
            Validate(root, true, false); if (Identity(identityHandle) != Identity(root)) throw Invalid();
            return new(pins, root, identityHandle, leaf, System.IO.Path.Combine(canonical[4..], leaf));
        }
        catch
        {
            try
            {
                if (creationHandle && root is not null) DeleteHandle(root);
                else if (identityHandle is not null && createdLeaf is not null)
                {
                    root?.Dispose(); root = null;
                    using var candidate = OpenRelative(pins[^1], createdLeaf, create: false, delete: true, descriptor: null);
                    if (Identity(candidate) == Identity(identityHandle)) DeleteHandle(candidate);
                }
            }
            catch { /* Preserve an unverifiable or locked leaf; never delete a replacement. */ }
            identityHandle?.Dispose(); root?.Dispose(); foreach (var pin in pins) pin.Dispose(); throw;
        }
    }
    /// <summary>Create a missing private parent without changing an existing parent's ACL, then
    /// validate it through the same admission path as <see cref="Create"/>.</summary>
    public static WindowsPrivateProfileDirectory CreateWithPrivateParent(string parent)
    {
        VocabularyWindowsFile.CreateParentDirectory(System.IO.Path.GetFullPath(parent));
        return Create(parent);
    }

    /// <summary>Call only after WebView and all profile users have stopped. Failure retains the root
    /// and its identity lock for retry; prior successful child removals are not rolled back.</summary>
    public Task DeleteOwnedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); if (_deleted) return Task.CompletedTask;
            if (_deletion is { IsCompleted: false }) return _deletion;
            return _deletion = Task.Run(() => DeleteCore(cancellationToken));
        }
    }
    private void DeleteCore(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_rootDelete is null)
        {
            // Keep the share-delete identity handle alive across the only unlock/reopen gap.
            // The old file ID cannot be recycled while that handle exists; a changed name is
            // rejected before any mutation. A successful DELETE handle pins the matched leaf.
            _rootPin?.Dispose(); _rootPin = null; AfterPinReleased?.Invoke();
            var candidate = OpenRelative(_parents[^1], _leaf, create: false, delete: true, descriptor: null);
            try { Validate(candidate, true, false); if (Identity(candidate) != Identity(_identity)) throw Invalid(); _rootDelete = candidate; }
            catch { candidate.Dispose(); throw; }
        }
        int budget = 100000; DeleteChildren(_parents[^1], _leaf, _rootDelete, token, 0, "", ref budget);
        token.ThrowIfCancellationRequested(); DeleteHandle(_rootDelete); _rootDelete.Dispose();
        lock (_gate) _deleted = true;
    }
    private void DeleteChildren(SafeFileHandle parent, string directoryName, SafeFileHandle directory, CancellationToken token, int depth, string prefix, ref int budget)
    {
        if (depth > 64) throw Invalid();
        var entries = Enumerate(parent, directoryName, directory, token);
        foreach (var (name, low, high) in entries)
        {
            token.ThrowIfCancellationRequested(); if (--budget < 0) throw Invalid(); string key = prefix + name;
            if (_observed.TryGetValue(key, out var existing) && existing != (low, high)) throw Invalid();
            if (!_observed.ContainsKey(key))
            {
                if (_observed.Count >= 100000 || _observedNameBytes + key.Length * 2L > 64L * 1024 * 1024) throw Invalid();
                _observedNameBytes += key.Length * 2L;
            }
            _observed[key] = (low, high);
        }
        AfterEnumeration?.Invoke(depth.ToString());
        foreach (var (name, low, high) in entries)
        {
            token.ThrowIfCancellationRequested(); using var child = OpenRelative(directory, name, false, true, null);
            var identity = Identity(child); if (identity.Low != low || identity.High != high) throw Invalid();
            var attributes = Attributes(child); bool link = (attributes & 0x400) != 0, isDirectory = (attributes & 0x10) != 0;
            if (isDirectory && !link) DeleteChildren(directory, name, child, token, depth + 1, prefix + name + "\\", ref budget);
            token.ThrowIfCancellationRequested(); DeleteHandle(child);
        }
    }
    private static List<(string Name, ulong Low, ulong High)> Enumerate(SafeFileHandle parent, string directoryName, SafeFileHandle directory, CancellationToken token)
    {
        // A new relative handle owns a fresh enumeration cursor on each retry. Duplicating the
        // retained handle shares its cursor and may incorrectly report a prior end-of-directory.
        using var scan = OpenRelative(parent, directoryName, create: false, delete: false, descriptor: null, share: 7);
        Validate(scan, directory: true, allowLink: false);
        if (Identity(scan) != Identity(directory)) throw Invalid();
        var result = new List<(string, ulong, ulong)>(); byte[] buffer = new byte[65536]; int kind = 20;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!GetDirectoryInfo(scan, kind, buffer, (uint)buffer.Length))
            { if (Marshal.GetLastPInvokeError() == 18) return result; throw Invalid(); }
            kind = 19; int position = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (position < 0 || position > buffer.Length - 88) throw Invalid();
                uint next = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(position)), size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(position + 60));
                if ((size & 1) != 0 || size > buffer.Length - position - 88) throw Invalid();
                string name = new UnicodeEncoding(false, false, true).GetString(buffer, position + 88, (int)size);
                if (name is not ("." or ".."))
                {
                    if (name.Length is 0 or > 255 || name.IndexOfAny(['\\', '/', ':', '\0']) >= 0 || result.Count >= 100000) throw Invalid();
                    ulong low = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(position + 72)), high = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(position + 80));
                    if (low == 0 && high == 0) throw Invalid(); result.Add((name, low, high));
                }
                if (next == 0) break;
                if (next < 88 || next > buffer.Length - position || (next & 7) != 0) throw Invalid(); position += (int)next;
            }
        }
    }
    private static void DeleteHandle(SafeFileHandle handle)
    {
        // POSIX disposition removes only this handle's opened link, including a reparse leaf.
        int flags = 1 | 2 | 16;
        if (!VocabularyWindowsFile.Native.SetFileInformationByHandle(handle, 21, ref flags, sizeof(int))) throw Invalid();
    }
    private static uint Attributes(SafeFileHandle handle)
    {
        if (handle.IsInvalid || VocabularyWindowsFile.Native.GetFileType(handle) != 1 || !VocabularyWindowsFile.Native.GetAttributes(handle, 9, out var a, 8)) throw Invalid();
        return a.Attributes;
    }
    private static void Validate(SafeFileHandle handle, bool directory, bool allowLink)
    { uint a = Attributes(handle); if (((a & 16) != 0) != directory || (!allowLink && (a & 0x400) != 0)) throw Invalid(); _ = Identity(handle); }
    private static VocabularyWindowsFile.FileIdentity Identity(SafeFileHandle handle)
    { if (!VocabularyWindowsFile.Native.GetIdentity(handle, 18, out var id, 24) || (id.Low == 0 && id.High == 0)) throw Invalid(); return id; }
    private static void ValidatePrivateAcl(SafeFileHandle handle, SecurityIdentifier user)
    {
        if (GetSecurityInfo(handle, 1, 5, out _, out _, out _, out _, out IntPtr descriptor) != 0 || descriptor == IntPtr.Zero) throw Invalid();
        try
        {
            uint length = GetSecurityDescriptorLength(descriptor); if (length == 0 || length > 65536) throw Invalid();
            byte[] bytes = new byte[length]; Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            var value = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0);
            if (!Equals(value.Owner, user) || (value.ControlFlags & System.Security.AccessControl.ControlFlags.DiscretionaryAclProtected) == 0
                || value.DiscretionaryAcl is not { Count: > 0 } acl) throw Invalid();
            bool userAccess = false;
            foreach (System.Security.AccessControl.GenericAce ace in acl)
            {
                if (ace is not System.Security.AccessControl.QualifiedAce qualified) throw Invalid();
                if (qualified.AceQualifier != System.Security.AccessControl.AceQualifier.AccessAllowed) continue;
                if (!Equals(qualified.SecurityIdentifier, user)) throw Invalid(); userAccess = true;
            }
            if (!userAccess) throw Invalid();
        }
        finally { _ = LocalFree(descriptor); }
    }

    private static unsafe SafeFileHandle OpenRelative(SafeFileHandle parent, string name, bool create, bool delete, byte[]? descriptor, uint share = 3)
    {
        fixed (char* text = name) fixed (byte* acl = descriptor)
        {
            var unicode = new UnicodeString { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)(name.Length * 2)), Buffer = (IntPtr)text };
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), Root = parent.DangerousGetHandle(), Name = (IntPtr)(&unicode), Flags = 0x40, Security = (IntPtr)acl };
            uint desired = 0x80000000 | 0x00100000 | (delete ? 0x00010000u : 0);
            uint options = 0x20 | 0x00200000 | (create ? 1u : 0);
            int status = NtCreateFile(out var handle, desired, ref attributes, out _, IntPtr.Zero, 0, share, create ? 2u : 1u, options, IntPtr.Zero, 0);
            if (status < 0) { handle?.Dispose(); throw new Win32Exception((int)RtlNtStatusToDosError(status)); }
            return handle;
        }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource done; Task? running;
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _disposed = true; running = _deletion;
            done = new(TaskCreationOptions.RunContinuationsAsynchronously); _disposal = done.Task;
        }
        _ = DrainAsync(running, done); return new(done.Task);
    }
    private async Task DrainAsync(Task? running, TaskCompletionSource done)
    {
        Exception? failure = null;
        try { if (running is not null) await running.ConfigureAwait(false); } catch (Exception error) { failure = error; }
        foreach (var handle in new[] { _rootDelete, _rootPin, _identity }.Concat(_parents))
            try { handle?.Dispose(); } catch (Exception error) { failure ??= error; }
        if (failure is null) done.TrySetResult(); else done.TrySetException(failure);
    }
    private static IOException Invalid() => new("The private profile directory could not be verified or removed.");
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { internal ushort Length, MaximumLength; internal IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { internal int Length; internal IntPtr Root, Name; internal uint Flags; internal IntPtr Security, Qos; }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { internal IntPtr Status, Information; }
    [DllImport("ntdll.dll", ExactSpelling = true)] private static extern int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes, out IoStatus status, IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
    [DllImport("ntdll.dll", ExactSpelling = true)] private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDirectoryInfo(SafeFileHandle directory, int kind, [Out] byte[] buffer, uint size);
    [DllImport("advapi32.dll", ExactSpelling = true)] private static extern uint GetSecurityInfo(SafeFileHandle handle, int type, uint info, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll", ExactSpelling = true)] private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr LocalFree(IntPtr memory);
}
