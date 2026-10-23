using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Actual Windows files, ACLs, identity handles and native PNG validation. Twelve operations use
/// the same bytes/actions as the complete original Mac media owner. Extra Windows cases exercise
/// platform protections, not claimed Mac codec equivalence. Every path is a private temporary
/// notebook; no app archive, user image, network, desktop or model is used.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyImageStorageTests(WpfDispatcher dispatcher)
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-image-storage-v1.json"))).RootElement.Clone();
    private static byte[] Input => Convert.FromBase64String(Corpus.GetProperty("input").GetString()!);
    private static VocabularyImageReference Image(byte[] data) => new(Convert.ToHexStringLower(SHA256.HashData(data)), 80, 40, data.Length);
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public Task PinnedMacOwnsMediaReuseRepairAndNoWriteBeforeAdmission(string id) => dispatcher.Run(() =>
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyImageStorage.swift", row.GetProperty("macSource").GetString());
        using var folder = new Folder(); byte[] data = Input; var image = Image(data);
        bool memory = id.StartsWith("memory-", StringComparison.Ordinal);
        var store = new WindowsVocabularyImageStorage(memory ? null : folder.Notebook);
        string leaf = Path.Combine(folder.Media, image.Filename); var result = new Dictionary<string, object>();
        switch (id)
        {
            case "memory-roundtrip": case "disk-roundtrip":
                result["created"] = store.Write(data, image); result["reused"] = !store.Write(data, image);
                result["exact"] = store.Read(image).SequenceEqual(data);
                if (memory)
                {
                    var detached = store.Read(image); detached[0] = 0; result["detached"] = store.Read(image).SequenceEqual(data);
                    store.Remove(image); result["removed"] = !Available(store, image);
                }
                else
                {
                    var reopened = new WindowsVocabularyImageStorage(folder.Notebook); result["reopened"] = reopened.Read(image).SequenceEqual(data);
                    result["files"] = Directory.GetFileSystemEntries(folder.Media).Length;
                }
                break;
            case "memory-missing": result["available"] = Available(store, image); break;
            case "disk-inert":
                result["before"] = Directory.Exists(folder.Media); result["available"] = Available(store, image);
                result["after"] = Directory.Exists(folder.Media); break;
            case "disk-repair":
                _ = store.Write(data, image); File.WriteAllBytes(leaf, [1, 2, 3]); result["before"] = Available(store, image);
                result["created"] = store.Write(data, image); result["exact"] = store.Read(image).SequenceEqual(data);
                result["files"] = Directory.GetFileSystemEntries(folder.Media).Length; break;
            case "disk-remove":
                _ = store.Write(data, image); store.Remove(image); store.Remove(image);
                result["available"] = Available(store, image); result["exists"] = File.Exists(leaf); break;
            case "disk-invalid-bytes":
                byte[] wrong = (byte[])data.Clone(); wrong[0] = 0; result["accepted"] = Accepted(store, wrong, image);
                result["root"] = Directory.Exists(folder.Media); break;
            case "disk-metadata":
                _ = store.Write(data, image); var metadata = image with { Width = image.Width + 1 };
                result["available"] = Available(store, metadata); result["accepted"] = Accepted(store, data, metadata);
                result["exact"] = store.Read(image).SequenceEqual(data); break;
            case "disk-directory-leaf":
                VocabularyWindowsFile.CreateParentDirectory(leaf); string marker = Path.Combine(leaf, "marker"); File.WriteAllBytes(marker, [7]);
                result["accepted"] = Accepted(store, data, image); result["preserved"] = File.ReadAllBytes(marker).SequenceEqual(new byte[] { 7 }); break;
            case "disk-root-link":
                string target = Path.Combine(folder.Path, "target"); VocabularyWindowsFile.CreateParentDirectory(target); Directory.CreateSymbolicLink(folder.Media, target);
                result["accepted"] = Accepted(store, data, image); result["files"] = Directory.GetFileSystemEntries(target).Length; break;
            case "disk-leaf-link":
                VocabularyWindowsFile.CreateParentDirectory(folder.Media); string destination = Path.Combine(folder.Path, "target"); File.WriteAllBytes(destination, [7]);
                File.CreateSymbolicLink(leaf, destination); result["accepted"] = Accepted(store, data, image);
                result["preserved"] = File.ReadAllBytes(destination).SequenceEqual(new byte[] { 7 }); break;
            case "disk-oversize":
                _ = store.Write(data, image); File.WriteAllBytes(leaf, new byte[VocabularyContentPolicy.MaximumImageBytes + 1]);
                result["available"] = Available(store, image); break;
            default: throw new InvalidOperationException("Unknown media probe.");
        }
        var actual = JsonSerializer.SerializeToNode(result);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
        Assert.Empty(Directory.GetFiles(folder.Path, ".vocabulary-image-*", SearchOption.AllDirectories));
        Assert.False(File.Exists(folder.Notebook));
    });

    [Fact]
    public Task NewMediaRootAndPngHaveProtectedCurrentUserAclsAndReuseDoesNotRewrite() => dispatcher.Run(() =>
    {
        using var folder = new Folder(); byte[] data = Input; var image = Image(data); var store = new WindowsVocabularyImageStorage(folder.Notebook);
        Assert.True(store.Write(data, image)); string leaf = Path.Combine(folder.Media, image.Filename);
        using var user = WindowsIdentity.GetCurrent();
        foreach (var security in new FileSystemSecurity[] { new DirectoryInfo(folder.Media).GetAccessControl(), new FileInfo(leaf).GetAccessControl() })
        {
            Assert.True(security.AreAccessRulesProtected); Assert.Equal(user.User, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.All(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule =>
            { Assert.Equal(user.User, rule.IdentityReference); Assert.Equal(AccessControlType.Allow, rule.AccessControlType); });
        }
        var timestamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc); File.SetLastWriteTimeUtc(leaf, timestamp);
        Assert.False(store.Write(data, image)); Assert.Equal(timestamp, File.GetLastWriteTimeUtc(leaf));
        Assert.Equal(data, store.Read(image)); Assert.Single(Directory.GetFileSystemEntries(folder.Media));
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PublicDataPermissionCannotBeSilentlyRepairedOrUsed(bool onDirectory) => dispatcher.Run(() =>
    {
        using var folder = new Folder(); byte[] data = Input; var image = Image(data); var store = new WindowsVocabularyImageStorage(folder.Notebook);
        store.Write(data, image); string leaf = Path.Combine(folder.Media, image.Filename);
        var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        if (onDirectory)
        {
            var root = new DirectoryInfo(folder.Media); var security = root.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(world, FileSystemRights.Read,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow)); root.SetAccessControl(security);
        }
        else
        {
            File.WriteAllBytes(leaf, [7]); var file = new FileInfo(leaf); var security = file.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(world, FileSystemRights.Read, AccessControlType.Allow)); file.SetAccessControl(security);
        }
        byte[] original = File.ReadAllBytes(leaf); Assert.False(Available(store, image)); Assert.False(Accepted(store, data, image));
        store.Remove(image); Assert.Equal(original, File.ReadAllBytes(leaf)); Assert.Single(Directory.GetFileSystemEntries(folder.Media));
    });
    [Fact]
    public Task ADirectoryLeaseDeniesRenameAndChecksTheRequestedAncestorAliasAgain() => dispatcher.Run(() =>
    {
        using var folder = new Folder(); string real = Path.Combine(folder.Path, "real"), other = Path.Combine(folder.Path, "other"), alias = Path.Combine(folder.Path, "alias");
        VocabularyWindowsFile.CreateParentDirectory(Path.Combine(real, "media")); VocabularyWindowsFile.CreateParentDirectory(Path.Combine(other, "media"));
        Directory.CreateSymbolicLink(alias, real);
        using (var root = VocabularyMediaDirectory.Open(Path.Combine(alias, "media"), create: false))
        {
            Assert.ThrowsAny<IOException>(() => Directory.Move(Path.Combine(real, "media"), Path.Combine(real, "moved")));
            Directory.Delete(alias); Directory.CreateSymbolicLink(alias, other);
            Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(root.Verify).Failure);
        }
        Directory.Move(Path.Combine(real, "media"), Path.Combine(real, "moved"));
    });
    [Fact]
    public Task ConcurrentOwnersUnderTheArchiveLockPublishOneCompletePng() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); byte[] data = Input; var image = Image(data);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var fileLock = new VocabularyWindowsFileLock(folder.Notebook); using var lease = fileLock.Enter(write: true);
            var store = new WindowsVocabularyImageStorage(folder.Notebook); bool created = store.Write(data, image); Assert.Equal(data, store.Read(image)); return created;
        })));
        Assert.Equal(1, results.Count(created => created)); Assert.Single(Directory.GetFileSystemEntries(folder.Media));
        Assert.Equal(data, new WindowsVocabularyImageStorage(folder.Notebook).Read(image));
    });
    [Fact]
    public Task ACreateNewCollisionNeverDeletesThePreexistingStagingFile() => dispatcher.Run(() =>
    {
        using var folder = new Folder(); VocabularyWindowsFile.CreateParentDirectory(folder.Media);
        var identity = new Guid("afbeafbe-0000-4000-8000-000000000001");
        string stage = Path.Combine(folder.Media, ".vocabulary-image-" + identity.ToString("N") + ".tmp"); File.WriteAllBytes(stage, [7]);
        var store = new WindowsVocabularyImageStorage(folder.Notebook, () => identity); byte[] data = Input;
        Assert.False(Accepted(store, data, Image(data))); Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(stage));
        Assert.Single(Directory.GetFileSystemEntries(folder.Media)); Assert.False(File.Exists(Path.Combine(folder.Media, Image(data).Filename)));
    });
    [Fact]
    public Task InvalidHashCannotEscapeTheMediaDirectoryOrCreateIt() => dispatcher.Run(() =>
    {
        using var folder = new Folder(); byte[] data = Input; var image = Image(data) with { Digest = "../outside" };
        var store = new WindowsVocabularyImageStorage(folder.Notebook); Assert.False(Accepted(store, data, image)); Assert.False(Available(store, image)); store.Remove(image);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Path));
    });
    private static bool Available(WindowsVocabularyImageStorage store, VocabularyImageReference image)
    { try { _ = store.Read(image); return true; } catch (VocabularyException error) { Assert.Equal(VocabularyFailure.InvalidContent, error.Failure); return false; } }
    private static bool Accepted(WindowsVocabularyImageStorage store, byte[] data, VocabularyImageReference image)
    { try { _ = store.Write(data, image); return true; } catch (VocabularyException error) { Assert.Equal(VocabularyFailure.InvalidContent, error.Failure); return false; } }
    private sealed class Folder : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lingrove-media-" + Guid.NewGuid().ToString("N"));
        internal string Notebook => System.IO.Path.Combine(Path, "notebook.sqlite");
        internal string Media => Notebook + ".media";
        internal Folder() => VocabularyWindowsFile.CreateParentDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
