using Cotabby.Win32.Dictionary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests;

public sealed class WindowsPrivateProfileDirectoryTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "lingrove-profile-owner-" + Guid.NewGuid().ToString("N"));
    public WindowsPrivateProfileDirectoryTests() => VocabularyWindowsFile.CreateParentDirectory(_parent);
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FailedAncestorValidationClosesEveryOpenedHandleImmediately(int failIndex)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var opened = new List<Microsoft.Win32.SafeHandles.SafeFileHandle>();
            Assert.Throws<InvalidOperationException>(() => WindowsPrivateProfileDirectory.CreateForTest(_parent, (handle, index) =>
            { opened.Add(handle); if (index == failIndex) throw new InvalidOperationException("synthetic admission failure"); }));
            Assert.Equal(failIndex + 1, opened.Count); Assert.All(opened, handle => Assert.True(handle.IsClosed));
        }
    }
    [Fact]
    public async Task ConcurrentDisposeWaitsForCleanupAndAllOwnedHandles()
    {
        var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path;
        File.WriteAllText(Path.Combine(path, "value"), "data");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        profile.AfterEnumeration = _ => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); };
        Task cleanup = profile.DeleteOwnedAsync();
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
            var handles = profile.HandlesForTest;
            Task first = profile.DisposeAsync().AsTask(), second = profile.DisposeAsync().AsTask();
            Assert.Same(first, second); Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
            release.Set(); await Task.WhenAll(cleanup, first, second).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(handles, handle => Assert.True(handle.IsClosed)); Assert.False(Directory.Exists(path));
        }
        finally { release.Set(); try { await profile.DisposeAsync(); } catch { } }
    }
    [Fact]
    public async Task NestedFilesAndLinksAreRemovedWithoutFollowingTargets()
    {
        string outside = Path.Combine(_parent, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "keep"), "sentinel");
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path;
        Directory.CreateDirectory(Path.Combine(path, "nested", "deeper")); File.WriteAllText(Path.Combine(path, "nested", "deeper", "value"), "data");
        Directory.CreateSymbolicLink(Path.Combine(path, "link"), outside);
        await profile.DeleteOwnedAsync(); Assert.False(Directory.Exists(path)); Assert.Equal("sentinel", File.ReadAllText(Path.Combine(outside, "keep")));
    }
    [Fact]
    public async Task RootPinBlocksReplacementAndLockedChildFailsUntilReleased()
    {
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path;
        var acl = new DirectoryInfo(path).GetAccessControl(); Assert.True(acl.AreAccessRulesProtected);
        using var current = System.Security.Principal.WindowsIdentity.GetCurrent(); Assert.Equal(current.User, acl.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)));
        Assert.ThrowsAny<IOException>(() => Directory.Move(path, Path.Combine(_parent, "other")));
        Assert.ThrowsAny<IOException>(() => Directory.Move(_parent, _parent + "-moved"));
        string file = Path.Combine(path, "locked"); File.WriteAllText(file, "x");
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        { await Assert.ThrowsAnyAsync<Exception>(() => profile.DeleteOwnedAsync()); Assert.True(Directory.Exists(path)); }
        await profile.DeleteOwnedAsync(); Assert.False(Directory.Exists(path));
    }
    [Fact]
    public async Task SameNameReplacementAfterEnumerationIsNotDeleted()
    {
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path;
        string child = Path.Combine(path, "file"); File.WriteAllText(child, "same");
        profile.AfterEnumeration = _ => { profile.AfterEnumeration = null; ReplaceByHandle(child, () => File.WriteAllText(child, "same")); };
        await Assert.ThrowsAnyAsync<Exception>(() => profile.DeleteOwnedAsync()); Assert.Equal("same", File.ReadAllText(child)); Assert.True(Directory.Exists(path));
        await Assert.ThrowsAnyAsync<Exception>(() => profile.DeleteOwnedAsync()); Assert.Equal("same", File.ReadAllText(child));
        File.Delete(child); await profile.DeleteOwnedAsync(); Assert.False(Directory.Exists(path));
    }
    [Fact]
    public async Task ReplacementDirectoryAndContentsAreNeverAdoptedOnRetry()
    {
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path;
        string child = Path.Combine(path, "child"); Directory.CreateDirectory(child);
        profile.AfterEnumeration = _ => { profile.AfterEnumeration = null; ReplaceByHandle(child, () => { Directory.CreateDirectory(child); File.WriteAllText(Path.Combine(child, "keep"), "sentinel"); }); };
        await Assert.ThrowsAnyAsync<Exception>(() => profile.DeleteOwnedAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => profile.DeleteOwnedAsync()); Assert.Equal("sentinel", File.ReadAllText(Path.Combine(child, "keep")));
        Directory.Delete(child, true); await profile.DeleteOwnedAsync(); Assert.False(Directory.Exists(path));
    }
    [Fact]
    public async Task FileSymlinkDoesNotAlterItsTarget()
    {
        string target = Path.Combine(_parent, "target"); File.WriteAllText(target, "sentinel");
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path;
        File.CreateSymbolicLink(Path.Combine(path, "link"), target); await profile.DeleteOwnedAsync();
        Assert.Equal("sentinel", File.ReadAllText(target)); Assert.False(Directory.Exists(path));
    }
    [Fact]
    public async Task CancelledCleanupKeepsRootForRetry()
    {
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path;
        using var token = new CancellationTokenSource(); token.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => profile.DeleteOwnedAsync(token.Token));
        Assert.True(Directory.Exists(path)); await profile.DeleteOwnedAsync(); Assert.False(Directory.Exists(path));
    }
    [Fact]
    public async Task MissingParentIsCreatedPrivatelyWithoutChangingAnExistingParent()
    {
        string directory = Path.Combine(_parent, "private-parent");
        await using (var first = WindowsPrivateProfileDirectory.CreateWithPrivateParent(directory)) { Assert.True(Directory.Exists(first.Path)); await first.DeleteOwnedAsync(); }
        var before = new DirectoryInfo(directory).GetAccessControl().GetSecurityDescriptorBinaryForm();
        await using (var second = WindowsPrivateProfileDirectory.CreateWithPrivateParent(directory)) await second.DeleteOwnedAsync();
        Assert.Equal(before, new DirectoryInfo(directory).GetAccessControl().GetSecurityDescriptorBinaryForm());
    }
    [Fact]
    public async Task ProfileCanBeOpenedWithoutDeleteSharingAndItsChildrenCanBeRenamed()
    {
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent);
        using (var compatible = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(profile.Path), 0x80000000, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) Assert.False(compatible.IsInvalid);
        using (var compatible = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(profile.Path), 0x80000000, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) Assert.False(compatible.IsInvalid);
        string first = Path.Combine(profile.Path, "first"), second = Path.Combine(profile.Path, "second"); File.WriteAllText(first, "data"); File.Move(first, second);
        await profile.DeleteOwnedAsync();
    }
    [Fact]
    public async Task RootReplacementDuringPinUpgradeIsNeverDeleted()
    {
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string path = profile.Path, moved = path + "-original";
        profile.AfterPinReleased = () => { profile.AfterPinReleased = null; Directory.Move(path, moved); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "keep"), "sentinel"); };
        await Assert.ThrowsAnyAsync<Exception>(() => profile.DeleteOwnedAsync());
        Assert.Equal("sentinel", File.ReadAllText(Path.Combine(path, "keep"))); Assert.True(Directory.Exists(moved));
        Directory.Delete(path, true); Directory.Move(moved, path); await profile.DeleteOwnedAsync(); Assert.False(Directory.Exists(path));
    }
    [Fact]
    public async Task DeniedDeleteKeepsOwnerAndCanBeRetriedAfterAccessIsRestored()
    {
        await using var profile = WindowsPrivateProfileDirectory.Create(_parent); string file = Path.Combine(profile.Path, "protected"); File.WriteAllText(file, "data");
        var info = new FileInfo(file); var denied = info.GetAccessControl();
        using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
        var rule = new System.Security.AccessControl.FileSystemAccessRule(current.User!, System.Security.AccessControl.FileSystemRights.Delete | System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny);
        denied.AddAccessRule(rule);
        info.SetAccessControl(denied);
        try { await Assert.ThrowsAnyAsync<Exception>(() => profile.DeleteOwnedAsync()); Assert.True(File.Exists(file)); }
        finally { denied.RemoveAccessRuleSpecific(rule); info.SetAccessControl(denied); }
        await profile.DeleteOwnedAsync(); Assert.False(Directory.Exists(profile.Path));
    }
    private static void ReplaceByHandle(string path, Action replace)
    {
        using var held = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(path), 0x80000000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        using (var deletion = VocabularyWindowsFile.Native.CreateFileW(VocabularyWindowsFile.Extended(path), 0x10000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero))
        { Assert.False(held.IsInvalid); Assert.False(deletion.IsInvalid); int flags = 3; Assert.True(VocabularyWindowsFile.Native.SetFileInformationByHandle(deletion, 21, ref flags, 4)); }
        replace();
    }
    public void Dispose() { if (Directory.Exists(_parent)) Directory.Delete(_parent, true); }
}
