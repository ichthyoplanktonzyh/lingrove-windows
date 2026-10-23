using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using Value = Cotabby.Win32.Vocabulary.VocabularySQLiteConnection.Value;

namespace Cotabby.Inference.Tests.Vocabulary;

/// <summary>Real Windows file/SQLite checks in disposable temporary folders; no desktop, models or user notebook.</summary>
public sealed class VocabularyWindowsFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lingrove-sqlite-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "vocabulary.sqlite");
    public VocabularyWindowsFileTests() => VocabularyWindowsFile.CreateParentDirectory(_root);
    private static VocabularyDocument Document()
    {
        var time = DateTimeOffset.UnixEpoch;
        var usage = new VocabularyExample(Guid.NewGuid(), "The bank.", "银行。", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "context", time);
        var sense = new VocabularySense(Guid.NewGuid(), "meaning", time, time) { Examples = [usage], IsSavedWithoutContext = true };
        return new() { Entries = [new(Guid.NewGuid(), "bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, [sense], time, time)] };
    }
    private static VocabularyDocument Note(VocabularyDocument doc, string note) => doc with
    { Entries = [doc.Entries[0] with { Senses = [doc.Entries[0].Senses[0] with { Note = note }] }] };
    private static void Failure(VocabularyFailure expected, Action action) => Assert.Equal(expected, Assert.Throws<VocabularyException>(action).Failure);
    private static void OpenFailure(VocabularyFailure expected, string path) => Failure(expected, () =>
    { using var unexpected = VocabularySQLiteDatabase.OpenFile(path); });
    private void NoStages() => Assert.Empty(Directory.GetFiles(_root, ".vocabulary-*", SearchOption.AllDirectories));

    [Fact]
    public void NativeLayoutsRetainFullWindowsIdentityAndOverlappedOffsets()
    { Assert.Equal(24, Marshal.SizeOf<VocabularyWindowsFile.FileIdentity>()); Assert.Equal(IntPtr.Size == 8 ? 32 : 20, Marshal.SizeOf<VocabularyWindowsFile.LockOverlapped>()); }
    [Fact]
    public void NewDirectoryHasCurrentUserOnlyPermissionsAndExistingDirectoryAclIsPreserved()
    {
        byte[] previous = new DirectoryInfo(_root).GetAccessControl().GetSecurityDescriptorBinaryForm();
        string parent = Path.Combine(_root, "private", "nested");
        using (VocabularySQLiteDatabase.OpenFile(Path.Combine(parent, "vocabulary.sqlite"))) { }
        using var identity = WindowsIdentity.GetCurrent();
        foreach (string component in new[] { Path.GetDirectoryName(parent)!, parent })
        {
            var security = new DirectoryInfo(component).GetAccessControl(); Assert.True(security.AreAccessRulesProtected);
            Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.All(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule =>
            { Assert.Equal(identity.User, rule.IdentityReference); Assert.Equal(AccessControlType.Allow, rule.AccessControlType); });
        }
        Assert.Equal(previous, new DirectoryInfo(_root).GetAccessControl().GetSecurityDescriptorBinaryForm());
    }
    [Fact]
    public void NewDatabaseAndLockFilesUseCurrentUserAclEvenInAnExistingDirectory()
    {
        using (VocabularySQLiteDatabase.OpenFile(FilePath)) { }
        using var identity = WindowsIdentity.GetCurrent();
        foreach (string path in new[] { FilePath, FilePath + ".lock" })
        {
            var security = new FileInfo(path).GetAccessControl(); Assert.True(security.AreAccessRulesProtected);
            Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.All(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule =>
            { Assert.Equal(identity.User, rule.IdentityReference); Assert.Equal(AccessControlType.Allow, rule.AccessControlType); });
        }
    }
    [Fact]
    public void SharedInheritableDataAccessIsRejectedWithoutChangingTheExistingDirectoryAcl()
    {
        string folder = Path.Combine(_root, "shared"); Directory.CreateDirectory(folder);
        var directory = new DirectoryInfo(folder); var security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadAndExecute,
            InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security); byte[] previous = directory.GetAccessControl().GetSecurityDescriptorBinaryForm();
        OpenFailure(VocabularyFailure.InvalidArchive, Path.Combine(folder, "vocabulary.sqlite"));
        Assert.Equal(previous, directory.GetAccessControl().GetSecurityDescriptorBinaryForm()); Assert.Empty(Directory.GetFiles(folder));
    }
    [Theory]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.CreateFiles)]
    [InlineData(FileSystemRights.DeleteSubdirectoriesAndFiles)]
    public void UnsafeDirectDirectoryGrantsCannotBypassPrivateChildInheritance(FileSystemRights rights)
    {
        var directory = new DirectoryInfo(_root); var security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), rights,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security); byte[] before = directory.GetAccessControl().GetSecurityDescriptorBinaryForm();
        OpenFailure(VocabularyFailure.InvalidArchive, FilePath);
        Assert.Equal(before, directory.GetAccessControl().GetSecurityDescriptorBinaryForm()); Assert.Empty(Directory.GetFiles(_root));
    }
    [Fact]
    public void LiveWalAndSharedMemoryInheritPrivateDataPermissions()
    {
        using var db = VocabularySQLiteDatabase.OpenFile(FilePath); db.Write(Document(), new(), 0);
        using var identity = WindowsIdentity.GetCurrent();
        foreach (string suffix in new[] { "-wal", "-shm" })
        {
            var file = new FileInfo(FilePath + suffix); Assert.True(file.Exists);
            Assert.All(file.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
                rule => Assert.Equal(identity.User, rule.IdentityReference));
        }
    }
    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public void ExistingSidecarWithBroadDataAclIsRejectedBeforeSqliteTouchesIt(string suffix)
    {
        using (VocabularySQLiteDatabase.OpenFile(FilePath)) { }
        string sidecar = FilePath + suffix; byte[] bytes = [1, 2, 3, 4]; File.WriteAllBytes(sidecar, bytes);
        var file = new FileInfo(sidecar); var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(security); byte[] original = File.ReadAllBytes(FilePath);
        OpenFailure(VocabularyFailure.InvalidArchive, FilePath);
        Assert.Equal(bytes, File.ReadAllBytes(sidecar)); Assert.Equal(original, File.ReadAllBytes(FilePath));
    }
    [Theory]
    [InlineData("-wal")]
    [InlineData("-journal")]
    public void ExistingSidecarSymlinkCannotRedirectSqliteToAnotherFile(string suffix)
    {
        using (VocabularySQLiteDatabase.OpenFile(FilePath)) { }
        string target = Path.Combine(_root, "target"); File.WriteAllText(target, "preserved"); File.CreateSymbolicLink(FilePath + suffix, target);
        OpenFailure(VocabularyFailure.InvalidArchive, FilePath); Assert.Equal("preserved", File.ReadAllText(target));
    }
    [Fact]
    public void NamedConnectionCannotBypassFileLockThroughTheMemoryAdmissionHelper()
    {
        using (VocabularySQLiteDatabase.OpenFile(FilePath)) { }
        var connection = VocabularySQLiteConnection.OpenNamed(FilePath);
        Failure(VocabularyFailure.InvalidArchive, () => { using var unexpected = VocabularySQLiteDatabase.Admit(connection); });
        using var reopened = VocabularySQLiteDatabase.OpenFile(FilePath); Assert.Equal(0UL, reopened.Revision());
    }
    [Fact]
    public void FileCommitReopensWithExactValuesRevisionAndWalSettings()
    {
        var doc = Document();
        using (var db = VocabularySQLiteDatabase.OpenFile(FilePath)) { Assert.Equal(1UL, db.Write(doc, new(), 0)); }
        using (var db = VocabularySQLiteDatabase.OpenFile(FilePath)) { Assert.Equal(doc, db.Read().Document); Assert.Equal(1UL, db.Read().Revision); }
        using (var c = VocabularySQLiteConnection.OpenNamed(FilePath))
        { Assert.Equal("wal", Assert.IsType<Value.Text>(c.Rows("PRAGMA journal_mode")[0][0]).Data); Assert.Equal(2, c.Scalar("PRAGMA synchronous")); }
        Assert.False(File.Exists(FilePath + "-wal")); Assert.False(File.Exists(FilePath + "-shm")); NoStages();
    }
    [Fact]
    public void TwoLiveConnectionsRejectStaleCommitAndReadTheWinningRevision()
    {
        using var first = VocabularySQLiteDatabase.OpenFile(FilePath); using var second = VocabularySQLiteDatabase.OpenFile(FilePath);
        var old = Document(); first.Write(old, new(), 0); Assert.Equal(old, second.Read().Document);
        var next = Note(old, "winner"); first.Write(next, old, 1);
        Failure(VocabularyFailure.EditConflict, () => second.Write(Note(old, "stale"), old, 1));
        Assert.Equal(next, second.Read().Document); Assert.Equal(2UL, second.Revision());
    }
    [Fact]
    public void NoteAndNoopCommitsDoNotRewriteUnrelatedUsagePayloads()
    {
        using var db = VocabularySQLiteDatabase.OpenFile(FilePath); var old = Document(); db.Write(old, new(), 0);
        using var c = VocabularySQLiteConnection.OpenNamed(FilePath); byte[] before = Assert.IsType<Value.Blob>(c.Rows("SELECT payload FROM usages")[0][0]).Data;
        var next = Note(old, "edited"); db.Write(next, old, 1); Assert.Equal(2, db.LastWrittenRows);
        Assert.Equal(before, Assert.IsType<Value.Blob>(c.Rows("SELECT payload FROM usages")[0][0]).Data);
        db.Write(next, next, 2); Assert.Equal(1, db.LastWrittenRows);
    }
    [Fact]
    public void CorruptOriginalBytesArePreservedRatherThanReset()
    {
        byte[] bytes = [1, 2, 3, 4, 5]; File.WriteAllBytes(FilePath, bytes);
        OpenFailure(VocabularyFailure.InvalidArchive, FilePath); Assert.Equal(bytes, File.ReadAllBytes(FilePath)); NoStages();
    }
    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void UnsupportedFileVersionNeverRewritesOriginalDatabase(int version)
    {
        using (var db = VocabularySQLiteDatabase.OpenFile(FilePath)) db.Write(Document(), new(), 0);
        using (var c = VocabularySQLiteConnection.OpenNamed(FilePath)) c.Execute("PRAGMA user_version=" + version);
        byte[] before = File.ReadAllBytes(FilePath);
        OpenFailure(VocabularyFailure.UnsupportedVersion, FilePath); Assert.Equal(before, File.ReadAllBytes(FilePath)); NoStages();
    }
    [Fact]
    public void ExistingVersionOneFileUpgradesWithoutLosingContentOrRevision()
    {
        var doc = Document(); using (var db = VocabularySQLiteDatabase.OpenFile(FilePath)) db.Write(doc, new(), 0);
        using (var c = VocabularySQLiteConnection.OpenNamed(FilePath)) { c.Execute("DROP TABLE cli_receipts"); c.Execute("PRAGMA user_version=1"); }
        using var reopened = VocabularySQLiteDatabase.OpenFile(FilePath); Assert.Equal(doc, reopened.Read().Document); Assert.Equal(1UL, reopened.Revision());
        reopened.Write(doc, doc, 1, new("key", "hash", [0, 255])); Assert.Equal(new byte[] { 0, 255 }, reopened.Receipt("key")!.Result);
    }
    [Fact]
    public void ADirectoryCannotBeOpenedOrOverwrittenAsTheDatabase()
    {
        Directory.CreateDirectory(FilePath); string marker = Path.Combine(FilePath, "marker"); File.WriteAllText(marker, "preserved");
        OpenFailure(VocabularyFailure.InvalidArchive, FilePath); Assert.Equal("preserved", File.ReadAllText(marker)); NoStages();
    }
    [Fact]
    public void DatabaseLeafSymlinkIsRejectedWithoutTouchingItsTarget()
    {
        string target = Path.Combine(_root, "target"); byte[] bytes = [1, 2, 3]; File.WriteAllBytes(target, bytes); File.CreateSymbolicLink(FilePath, target);
        OpenFailure(VocabularyFailure.InvalidArchive, FilePath); Assert.Equal(bytes, File.ReadAllBytes(target)); NoStages();
    }
    [Fact]
    public void LockLeafSymlinkIsRejectedWithoutTouchingItsTarget()
    {
        using (VocabularySQLiteDatabase.OpenFile(FilePath)) { }
        string target = Path.Combine(_root, "target"); File.WriteAllText(target, "preserved"); File.Delete(FilePath + ".lock"); File.CreateSymbolicLink(FilePath + ".lock", target);
        OpenFailure(VocabularyFailure.InvalidArchive, FilePath); Assert.Equal("preserved", File.ReadAllText(target));
    }
    [Fact]
    public void ParentAliasResolvesWhileTheRegularDatabaseLeafRemainsChecked()
    {
        string real = Path.Combine(_root, "real"), alias = Path.Combine(_root, "alias"); Directory.CreateDirectory(real); Directory.CreateSymbolicLink(alias, real);
        var doc = Document(); using (var db = VocabularySQLiteDatabase.OpenFile(Path.Combine(alias, "vocabulary.sqlite"))) db.Write(doc, new(), 0);
        using var direct = VocabularySQLiteDatabase.OpenFile(Path.Combine(real, "vocabulary.sqlite")); Assert.Equal(doc, direct.Read().Document);
    }
    [Fact]
    public void RetargetedParentAliasCannotAcknowledgeAWriteToTheOldFile()
    {
        string real = Path.Combine(_root, "real"), other = Path.Combine(_root, "other"), alias = Path.Combine(_root, "alias");
        Directory.CreateDirectory(real); Directory.CreateDirectory(other); Directory.CreateSymbolicLink(alias, real);
        using (VocabularySQLiteDatabase.OpenFile(Path.Combine(other, "vocabulary.sqlite"))) { }
        var old = Document();
        using (var db = VocabularySQLiteDatabase.OpenFile(Path.Combine(alias, "vocabulary.sqlite")))
        {
            db.Write(old, new(), 0); Directory.Delete(alias); Directory.CreateSymbolicLink(alias, other);
            Failure(VocabularyFailure.InvalidArchive, () => db.Write(Note(old, "detached"), old, 1));
        }
        using var direct = VocabularySQLiteDatabase.OpenFile(Path.Combine(real, "vocabulary.sqlite")); Assert.Equal(old, direct.Read().Document); Assert.Equal(1UL, direct.Revision());
    }
    [Fact]
    public void RetainedHandlesPreventDatabaseAndLockRenameUntilOwnerCloses()
    {
        using (var db = VocabularySQLiteDatabase.OpenFile(FilePath))
        {
            Assert.ThrowsAny<IOException>(() => File.Move(FilePath, FilePath + ".old"));
            Assert.ThrowsAny<IOException>(() => File.Move(FilePath + ".lock", FilePath + ".lock.old"));
            Assert.Equal(0UL, db.Read().Revision);
        }
        File.Move(FilePath, FilePath + ".old"); File.Move(FilePath + ".lock", FilePath + ".lock.old");
    }
    [Fact]
    public void RetainedParentHandleStabilizesTheContainingDirectoryUntilOwnerCloses()
    {
        string folder = Path.Combine(_root, "parent");
        using (VocabularySQLiteDatabase.OpenFile(Path.Combine(folder, "vocabulary.sqlite")))
            Assert.ThrowsAny<IOException>(() => Directory.Move(folder, folder + ".moved"));
        Directory.Move(folder, folder + ".moved");
    }
    [Fact]
    public void SharedLocksCoexistAndExclusiveContentionIsBoundedAndRecoverable()
    {
        using var first = new VocabularyWindowsFileLock(FilePath); using var second = new VocabularyWindowsFileLock(FilePath); using var third = new VocabularyWindowsFileLock(FilePath);
        using (first.Enter(false)) using (second.Enter(false))
        {
            var timer = Stopwatch.StartNew(); Failure(VocabularyFailure.StorageBusy, () => third.Enter(true)); Assert.True(timer.Elapsed >= TimeSpan.FromSeconds(1));
        }
        using (third.Enter(true)) { }
    }
    [Fact]
    public async Task ExclusiveLockInAnotherProcessBlocksOurSharedAccessAndReleasesCleanly()
    {
        string script = Path.Combine(_root, "hold-lock.ps1");
        File.WriteAllText(script, """
            param([string]$Path)
            $ErrorActionPreference = 'Stop'
            $file = [System.IO.File]::Open($Path, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::ReadWrite)
            try {
                $file.Lock(0, 1)
                [Console]::Out.WriteLine('locked')
                [Console]::Out.Flush()
                [Console]::In.ReadLine() | Out-Null
                $file.Unlock(0, 1)
            } finally { $file.Dispose() }
            """);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, FilePath + ".lock" }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.Equal("locked", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            using var gate = new VocabularyWindowsFileLock(FilePath);
            Failure(VocabularyFailure.StorageBusy, () => gate.Enter(false));
            await child.StandardInput.WriteLineAsync("release"); await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(child.ExitCode == 0, await errors); using (gate.Enter(false)) { }
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10000); }
        }
    }
    [Fact]
    public void MetadataLockReleasesOnExceptionAndNestedWriteUpgradeFailsClosed()
    {
        using var first = VocabularySQLiteDatabase.OpenFile(FilePath); using var second = VocabularySQLiteDatabase.OpenFile(FilePath);
        Assert.Throws<InvalidOperationException>(() => first.Access<int>(true, () => throw new InvalidOperationException()));
        Assert.Equal(0UL, second.Read().Revision); Failure(VocabularyFailure.InvalidContent, () => first.Access(false, () => first.Write(new(), new(), 0)));
        Assert.Equal(0UL, second.Read().Revision);
    }
    [Fact]
    public void ExternalSqliteWriterContentionMapsToBusyWithoutChangingTheGraph()
    {
        using var db = VocabularySQLiteDatabase.OpenFile(FilePath); using var external = VocabularySQLiteConnection.OpenNamed(FilePath); external.Execute("BEGIN IMMEDIATE");
        try { Failure(VocabularyFailure.StorageBusy, () => db.Write(Document(), new(), 0)); } finally { external.Execute("ROLLBACK"); }
        Assert.Empty(db.Read().Document.Entries); Assert.Equal(0UL, db.Revision());
    }
    [Fact]
    public void ConcurrentCreatorsPublishOneCompleteSchemaAndLeaveNoStagingFiles()
    {
        Parallel.For(0, 4, _ => { using var db = VocabularySQLiteDatabase.OpenFile(FilePath); Assert.Equal(0UL, db.Read().Revision); });
        using var reopened = VocabularySQLiteDatabase.OpenFile(FilePath); Assert.Empty(reopened.Read().Document.Entries); NoStages();
    }
    [Fact]
    public void LongUnicodeParentPathIsResolvedAndPassedToSqliteWithoutTruncation()
    {
        string folder = _root; for (int index = 0; index < 16; index++) folder = Path.Combine(folder, new string('a', 60) + "拾语");
        string path = Path.Combine(folder, "词库.sqlite"); var doc = Document(); Assert.True(Encoding.UTF8.GetByteCount(path) > 1040);
        using (var db = VocabularySQLiteDatabase.OpenFile(path)) db.Write(doc, new(), 0);
        using var reopened = VocabularySQLiteDatabase.OpenFile(path); Assert.Equal(doc, reopened.Read().Document); NoStages();
    }
    [Theory]
    [InlineData("vocabulary.sqlite:stream")]
    [InlineData("vocabulary.sqlite.")]
    [InlineData("vocabulary.sqlite ")]
    public void AmbiguousStreamOrTrimmedLeafNamesAreRejectedBeforeCreatingFiles(string leaf)
    {
        OpenFailure(VocabularyFailure.InvalidArchive, Path.Combine(_root, leaf)); Assert.Empty(Directory.GetFileSystemEntries(_root));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
