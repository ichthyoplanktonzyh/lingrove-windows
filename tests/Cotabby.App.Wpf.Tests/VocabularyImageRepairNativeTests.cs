using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class VocabularyImageRepairNativeTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task OutsideReadersAndSeparateWritersObserveOneAtomicIdentityChange() => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture(); var phases = new List<VocabularyImageRepairPhase>();
        f.Storage.RepairObserved = phase =>
        {
            phases.Add(phase);
            if (phase is not (VocabularyImageRepairPhase.TargetClosed or VocabularyImageRepairPhase.BeforeCommit)) return;
            using var reader = new FileStream(f.Target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var data = new MemoryStream(); reader.CopyTo(data); Assert.Equal(f.Damaged, data.ToArray());
            foreach (string mode in new[] { "write", "delete", "rename", "replace" }) AssertExternalMutationRejected(f, mode);
        };
        Assert.Null(f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference));
        Assert.Equal(new[] { VocabularyImageRepairPhase.BeforeTargetOpen, VocabularyImageRepairPhase.TargetClosed,
            VocabularyImageRepairPhase.BeforeCommit, VocabularyImageRepairPhase.Committed }, phases);
        Assert.Equal(f.Image.CopyBytes(), f.Storage.Read(f.Image.Reference));
        Assert.NotEqual(f.Original, f.Storage.CaptureIdentity(f.Image.Reference));
        Assert.Empty(Directory.GetFiles(f.Directory, ".vocabulary-image-*.tmp"));
        using var user = WindowsIdentity.GetCurrent();
        var acl = new FileInfo(f.Target).GetAccessControl(); Assert.True(acl.AreAccessRulesProtected);
        Assert.Equal(user.User, acl.GetOwner(typeof(SecurityIdentifier)));
        return Task.CompletedTask;
    });

    [Fact]
    public Task PersistentOutsideReaderRetainsOldObjectAcrossSafeCommitOrRollback() => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture();
        using var reader = new FileStream(f.Target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        bool committed;
        try { Assert.Null(f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference)); committed = true; }
        catch (VocabularyException) { committed = false; }
        // Windows may refuse a transacted delete while another reader retains the old object.
        // Either outcome must be atomic: no missing name, mixed bytes, or successful false repair.
        Assert.Equal(committed ? f.Image.CopyBytes() : f.Damaged, File.ReadAllBytes(f.Target));
        Assert.Equal(!committed, f.Original == f.Storage.CaptureIdentity(f.Image.Reference));
        using var old = new MemoryStream(); reader.CopyTo(old); Assert.Equal(f.Damaged, old.ToArray());
        Assert.Empty(Directory.GetFiles(f.Directory, ".vocabulary-image-*.tmp"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData((int)VocabularyImageRepairPhase.TargetClosed)]
    [InlineData((int)VocabularyImageRepairPhase.BeforeCommit)]
    public Task FailureBeforeCommitRestoresOriginalBytesIdentityAndNamespace(int failAt) => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture();
        f.Storage.RepairObserved = phase => { if ((int)phase == failAt) throw new IOException("Owned pre-commit fixture failure"); };
        Assert.Throws<VocabularyException>(() => f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference));
        Assert.Equal(f.Damaged, File.ReadAllBytes(f.Target)); Assert.Equal(f.Original, f.Storage.CaptureIdentity(f.Image.Reference));
        Assert.Empty(Directory.GetFiles(f.Directory, ".vocabulary-image-*.tmp"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ChangedCorruptIdentityIsNeverOverwritten() => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture(); byte[] replacement = [31, 41, 59];
        f.Storage.RepairObserved = phase =>
        {
            if (phase != VocabularyImageRepairPhase.BeforeTargetOpen) return;
            File.Move(f.Target, f.Target + ".retained"); File.WriteAllBytes(f.Target, replacement);
        };
        Assert.Throws<VocabularyException>(() => f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference));
        Assert.Equal(replacement, File.ReadAllBytes(f.Target)); Assert.Equal(f.Damaged, File.ReadAllBytes(f.Target + ".retained"));
        Assert.Empty(Directory.GetFiles(f.Directory, ".vocabulary-image-*.tmp"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ExistingStageCollisionRollsBackWithoutDeletingEitherPriorObject() => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture(); string collision = Path.Combine(f.Directory, ".vocabulary-image-" + f.Stage.ToString("N") + ".tmp");
        byte[] retained = [11, 22, 33]; File.WriteAllBytes(collision, retained);
        Assert.Throws<VocabularyException>(() => f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference));
        Assert.Equal(f.Damaged, File.ReadAllBytes(f.Target)); Assert.Equal(f.Original, f.Storage.CaptureIdentity(f.Image.Reference));
        Assert.Equal(retained, File.ReadAllBytes(collision));
        return Task.CompletedTask;
    });

    [Fact]
    public Task AbsentDestinationCollisionNeverReplacesAnUnexpectedCorruptWinner() => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture(); File.Delete(f.Target); byte[] winner = [6, 2, 6]; int publications = 0;
        f.Storage.BeforeOwnedPublish = () => { publications++; File.WriteAllBytes(f.Target, winner); };
        Assert.Throws<VocabularyException>(() => f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference));
        Assert.Equal(1, publications); Assert.Equal(winner, File.ReadAllBytes(f.Target));
        Assert.Empty(Directory.GetFiles(f.Directory, ".vocabulary-image-*.tmp"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task WritableOpenHandleRejectsRepairBeforeAnyNamespaceChange() => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture();
        using (var writer = new FileStream(f.Target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            Assert.Throws<VocabularyException>(() => f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference));
        Assert.Equal(f.Damaged, File.ReadAllBytes(f.Target)); Assert.Equal(f.Original, f.Storage.CaptureIdentity(f.Image.Reference));
        Assert.Empty(Directory.GetFiles(f.Directory, ".vocabulary-image-*.tmp"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReparseLeafCannotBecomeARepairCandidate() => dispatcher.RunAsync(() =>
    {
        using var f = new Fixture(); string outside = Path.Combine(f.Root, "other-owned.bin"); byte[] retained = [2, 7, 1, 8];
        File.WriteAllBytes(outside, retained); File.Delete(f.Target); File.CreateSymbolicLink(f.Target, outside);
        Assert.Throws<VocabularyException>(() => f.Storage.WriteOwned(f.Image.CopyBytes(), f.Image.Reference));
        Assert.NotNull(new FileInfo(f.Target).LinkTarget); Assert.Equal(retained, File.ReadAllBytes(outside));
        Assert.Empty(Directory.GetFiles(f.Directory, ".vocabulary-image-*.tmp"));
        return Task.CompletedTask;
    });

    private const string MutationScript = """
        $ErrorActionPreference = 'Stop'
        $root = [IO.Path]::GetFullPath($env:LINGROVE_REPAIR_ROOT)
        if (-not $root.StartsWith([IO.Path]::GetFullPath($env:LINGROVE_REPAIR_PARENT) + 'lingrove-media-repair-', [StringComparison]::OrdinalIgnoreCase)) { [Console]::WriteLine('guard-root'); exit 9 }
        if ([IO.File]::ReadAllText((Join-Path $root '.owned')) -ne $env:LINGROVE_REPAIR_TOKEN) { [Console]::WriteLine('guard-owner'); exit 9 }
        if ($env:LINGROVE_REPAIR_LEAF -notmatch '^lg-image-[a-f0-9]{64}\.png$') { [Console]::WriteLine('guard-leaf'); exit 9 }
        $target = Join-Path (Join-Path $root 'media') $env:LINGROVE_REPAIR_LEAF
        try {
            switch ($env:LINGROVE_REPAIR_MODE) {
                'write' { [IO.File]::WriteAllBytes($target, [byte[]](1,2,3)) }
                'delete' { [IO.File]::Delete($target) }
                'rename' { [IO.File]::Move($target, $target + '.moved') }
                'replace' { [IO.File]::Replace((Join-Path $root 'other.bin'), $target, (Join-Path $root 'backup.bin')) }
                default { exit 9 }
            }
            exit 7
        } catch {
            $cause = $_.Exception
            while ($cause.InnerException) { $cause = $cause.InnerException }
            if ($cause -is [IO.IOException]) { [Console]::WriteLine($cause.HResult -band 65535); exit 0 }
            [Console]::WriteLine($cause.GetType().Name + ':' + ($cause.HResult -band 65535)); exit 9
        }
        """;
    private static void AssertExternalMutationRejected(Fixture fixture, string mode)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", MutationScript }) start.ArgumentList.Add(argument);
        start.Environment["LINGROVE_REPAIR_ROOT"] = fixture.Root; start.Environment["LINGROVE_REPAIR_TOKEN"] = fixture.Token;
        start.Environment["LINGROVE_REPAIR_PARENT"] = Path.GetFullPath(Path.GetTempPath());
        start.Environment["LINGROVE_REPAIR_LEAF"] = fixture.Image.Reference.Filename; start.Environment["LINGROVE_REPAIR_MODE"] = mode;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Owned writer did not start.");
        if (!child.WaitForExit(5000)) { child.Kill(entireProcessTree: true); child.WaitForExit(); Assert.Fail("Owned writer did not promptly reject the transaction conflict."); }
        string output = child.StandardOutput.ReadToEnd().Trim(); Assert.True(child.ExitCode == 0, "Owned writer " + mode + " exited " + child.ExitCode + ": " + output); Assert.Equal("6800", output);
    }
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "lingrove-media-repair-" + Guid.NewGuid().ToString("N"));
        internal string Token { get; } = Guid.NewGuid().ToString("N");
        internal Guid Stage { get; } = Guid.NewGuid();
        internal string Directory => Path.Combine(Root, "media");
        internal string Target => Path.Combine(Directory, Image.Reference.Filename);
        internal byte[] Damaged { get; } = [7, 8, 9];
        internal VocabularyPreparedImage Image { get; }
        internal WindowsVocabularyImageStorage Storage { get; }
        internal VocabularyWindowsFile.FileIdentity? Original { get; }
        internal Fixture()
        {
            System.IO.Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, ".owned"), Token); File.WriteAllBytes(Path.Combine(Root, "other.bin"), [3, 1, 4]);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!);
            Image = new(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes);
            Storage = WindowsVocabularyImageStorage.ForDirectory(Directory, () => Stage);
            Assert.NotNull(Storage.WriteOwned(bytes, Image.Reference)); File.WriteAllBytes(Target, Damaged); Original = Storage.CaptureIdentity(Image.Reference); Assert.NotNull(Original);
        }
        public void Dispose()
        {
            string absolute = Path.GetFullPath(Root), parent = Path.GetFullPath(Path.GetTempPath());
            if (!absolute.StartsWith(parent + "lingrove-media-repair-", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected fixture cleanup root.");
            if (System.IO.Directory.Exists(absolute)) System.IO.Directory.Delete(absolute, recursive: true);
        }
    }
}
