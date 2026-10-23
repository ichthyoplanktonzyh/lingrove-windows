using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real NTFS handles, ACLs and publication. Only staging identity and a precommit callback
/// are controlled; tests never replace native file effects with successful fake responses.</summary>
[Collection("WPF views")]
public sealed class VocabularyExportFileTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    [InlineData(".lock")]
    public void NotebookAndSidecarsCannotBeExportDestinations(string suffix)
    {
        using var folder = new Folder(); string protectedPath = folder.Notebook + suffix;
        File.WriteAllBytes(protectedPath, [9, 8, 7]);
        AssertInvalid(() => new WindowsVocabularyExportFile(protectedPath, folder.Notebook));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(protectedPath)); AssertNoStage(folder.Path);
    }
    [Theory]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    [InlineData(".lock")]
    public void HardLinkIdentityProtectsEachNotebookFile(string suffix)
    {
        using var folder = new Folder(); string protectedPath = folder.Notebook + suffix;
        File.WriteAllBytes(protectedPath, [9]); Assert.True(CreateHardLinkW(folder.Export, protectedPath, IntPtr.Zero));
        AssertInvalid(() => new WindowsVocabularyExportFile(folder.Export, folder.Notebook));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(protectedPath)); AssertNoStage(folder.Path);
    }
    [Theory]
    [InlineData("")]
    [InlineData("child.json")]
    [InlineData("nested/child.json")]
    public void MediaRootAndDescendantsCannotBeExportDestinations(string relative)
    {
        using var folder = new Folder(); string media = folder.Notebook + ".media";
        VocabularyWindowsFile.CreateParentDirectory(System.IO.Path.Combine(media, "nested"));
        string destination = relative.Length == 0 ? media : System.IO.Path.Combine(media, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        AssertInvalid(() => new WindowsVocabularyExportFile(destination, folder.Notebook)); AssertNoStage(media);
    }
    [Fact]
    public void ResolvedParentAliasCannotBypassMediaProtection()
    {
        using var folder = new Folder(); string media = folder.Notebook + ".media", alias = System.IO.Path.Combine(folder.Path, "alias");
        VocabularyWindowsFile.CreateParentDirectory(media); Directory.CreateSymbolicLink(alias, media);
        AssertInvalid(() => new WindowsVocabularyExportFile(System.IO.Path.Combine(alias, "export.json"), folder.Notebook)); AssertNoStage(media);
    }
    [Fact]
    public void LeafReparsePointIsRejectedWithoutChangingItsTarget()
    {
        using var folder = new Folder(); File.WriteAllBytes(folder.Notebook, [9]); File.CreateSymbolicLink(folder.Export, folder.Notebook);
        AssertInvalid(() => new WindowsVocabularyExportFile(folder.Export, folder.Notebook));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(folder.Notebook)); Assert.True(File.GetAttributes(folder.Export).HasFlag(FileAttributes.ReparsePoint));
    }
    [Theory]
    [InlineData("stream:ads")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void AmbiguousWindowsLeavesCauseNoStagingMutation(string name)
    {
        using var folder = new Folder(); AssertInvalid(() => new WindowsVocabularyExportFile(System.IO.Path.Combine(folder.Path, name), null)); AssertNoStage(folder.Path);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewAndReplacementFilesOwnCompleteBytesAndPrivateAcl(bool replace)
    {
        using var folder = new Folder();
        if (replace)
        {
            File.WriteAllBytes(folder.Export, [9]); var security = new FileInfo(folder.Export).GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadData, AccessControlType.Allow));
            new FileInfo(folder.Export).SetAccessControl(security);
        }
        byte[] bytes = Enumerable.Range(0, 65537).Select(i => (byte)i).ToArray();
        using (var file = new WindowsVocabularyExportFile(folder.Export, folder.Notebook)) file.Write(bytes, default);
        Assert.Equal(bytes, File.ReadAllBytes(folder.Export));
        using var handle = VocabularyWindowsFile.Native.CreateFileW(folder.Export, VocabularyWindowsFile.Native.Read, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        VocabularyWindowsFile.CheckFileAccess(handle); AssertNoStage(folder.Path); Assert.False(File.Exists(folder.Notebook));
    }
    [Fact]
    public void MediaSiblingFolderRemainsAValidUserDestination()
    {
        using var folder = new Folder(); string sibling = folder.Notebook + ".media-copy"; VocabularyWindowsFile.CreateParentDirectory(sibling);
        string target = System.IO.Path.Combine(sibling, "export.json");
        using var file = new WindowsVocabularyExportFile(target, folder.Notebook); file.Write([1, 2, 3], default);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
    }
    [Fact]
    public void StagingCollisionNeverDeletesAnotherFile()
    {
        using var folder = new Folder(); string stage = System.IO.Path.Combine(folder.Path, ".notebook-" + Guid.Empty.ToString("D"));
        File.WriteAllBytes(stage, [7]); File.WriteAllBytes(folder.Export, [9]);
        using var file = new WindowsVocabularyExportFile(folder.Export, null, () => Guid.Empty);
        Assert.Throws<IOException>(() => file.Write([1, 2, 3], default));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(stage)); Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(folder.Export));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationBeforeWriteOrAfterFlushRetainsOriginalFile(bool afterFlush)
    {
        using var folder = new Folder(); using var cancellation = new CancellationTokenSource(); File.WriteAllBytes(folder.Export, [9]);
        if (!afterFlush) cancellation.Cancel();
        using var file = new WindowsVocabularyExportFile(folder.Export, null, afterFlush: () => cancellation.Cancel());
        Assert.Throws<OperationCanceledException>(() => file.Write([1, 2, 3], cancellation.Token));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(folder.Export)); AssertNoStage(folder.Path);
    }
    [Fact]
    public void RenameFailureRetainsOriginalFileAndCleansOnlyOwnedStage()
    {
        using var folder = new Folder(); File.WriteAllBytes(folder.Export, [9]);
        using var locked = new FileStream(folder.Export, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var file = new WindowsVocabularyExportFile(folder.Export, null);
        AssertInvalid(() => file.Write([1, 2, 3], default));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(folder.Export)); AssertNoStage(folder.Path);
    }
    [Fact]
    public void RechecksTargetHardLinkAfterFlushBeforePublishing()
    {
        using var folder = new Folder(); File.WriteAllBytes(folder.Notebook, [9]);
        using var file = new WindowsVocabularyExportFile(folder.Export, folder.Notebook, afterFlush: () => Assert.True(CreateHardLinkW(folder.Export, folder.Notebook, IntPtr.Zero)));
        AssertInvalid(() => file.Write([1, 2, 3], default));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(folder.Notebook)); AssertNoStage(folder.Path);
    }
    [Fact]
    public void ParentLeasePreventsRenameWhileTheExportIsStaged()
    {
        using var folder = new Folder(); string renamed = folder.Path + "-renamed";
        using var file = new WindowsVocabularyExportFile(folder.Export, null);
        Assert.True(Record.Exception(() => Directory.Move(folder.Path, renamed)) is IOException or UnauthorizedAccessException);
        file.Write([1, 2, 3], default); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(folder.Export));
    }
    [Fact]
    public void RetargetedAncestorAliasFailsBeforePublication()
    {
        using var folder = new Folder(); string first = System.IO.Path.Combine(folder.Path, "first"), second = System.IO.Path.Combine(folder.Path, "second"), alias = System.IO.Path.Combine(folder.Path, "alias");
        VocabularyWindowsFile.CreateParentDirectory(first); VocabularyWindowsFile.CreateParentDirectory(second); Directory.CreateSymbolicLink(alias, first);
        string offered = System.IO.Path.Combine(alias, "export.json");
        using var file = new WindowsVocabularyExportFile(offered, null, afterFlush: () => { Directory.Delete(alias); Directory.CreateSymbolicLink(alias, second); });
        AssertInvalid(() => file.Write([1, 2, 3], default));
        Assert.Empty(Directory.GetFiles(first)); Assert.Empty(Directory.GetFiles(second));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldStageCannotBeReplacedOrDeletedAfterFlush(bool cancelAfterAttempt)
    {
        using var folder = new Folder(); using var cancellation = new CancellationTokenSource();
        Guid stageId = Guid.NewGuid(); string staging = System.IO.Path.Combine(folder.Path, ".notebook-" + stageId.ToString("D"));
        string attacker = System.IO.Path.Combine(folder.Path, "peer-file"); File.WriteAllBytes(attacker, [9]);
        using var file = new WindowsVocabularyExportFile(folder.Export, null, () => stageId, () => {
            Assert.True(Record.Exception(() => File.Delete(staging)) is IOException or UnauthorizedAccessException);
            Assert.True(Record.Exception(() => File.Move(attacker, staging, overwrite: true)) is IOException or UnauthorizedAccessException);
            if (cancelAfterAttempt) cancellation.Cancel();
        });
        if (cancelAfterAttempt) Assert.Throws<OperationCanceledException>(() => file.Write([1, 2, 3], cancellation.Token));
        else { file.Write([1, 2, 3], default); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(folder.Export)); }
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(attacker)); AssertNoStage(folder.Path);
    }
    private static void AssertInvalid(Action action) => Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(action).Failure);
    private static void AssertNoStage(string directory) => Assert.Empty(Directory.GetFiles(directory, ".notebook-*", SearchOption.AllDirectories));
    internal sealed class Folder : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lingrove-export-file-" + Guid.NewGuid().ToString("N"));
        internal string Notebook => System.IO.Path.Combine(Path, "notebook.sqlite");
        internal string Export => System.IO.Path.Combine(Path, "export.json");
        internal Folder() { VocabularyWindowsFile.CreateParentDirectory(Path); }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);
}
