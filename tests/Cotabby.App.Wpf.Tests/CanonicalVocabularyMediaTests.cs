using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalVocabularyMediaTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task NativeBitmapUsesExistingPreparationCommitAndReopen() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-bitmap-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName); VocabularyPreparedImage image;
        try
        {
            using var bitmap = new Cotabby.Tests.Shared.VocabularyBitmapFixture();
            byte[] encoded = VocabularyClipboardBitmap.Read(bitmap.Handle);
            await using (var store = FrozenVocabularyStoreFactory.Create(file, false))
            {
                image = await store.PrepareImageAsync(encoded);
                Assert.Equal(3, image.Reference.Width); Assert.Equal(2, image.Reference.Height);
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image.CopyBytes()[..8]);
                var before = await store.ReadAsync();
                var committed = await store.SaveTemplateAsync(Template(image.Reference), Supplied(image));
                Assert.Equal(before.Revision + 1, committed.Revision);
                Assert.Equal(image.CopyBytes(), await store.ReadImageAsync(image.Reference));
            }
            await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(file, installBundledClassification: false);
            Assert.Equal(image.CopyBytes(), await reopened.ReadImageAsync(image.Reference));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    private sealed class CancelAfterWrite(IOwnedVocabularyImageStorage inner, CancellationTokenSource cancellation) : IOwnedVocabularyImageStorage
    {
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken token) => inner.Prepare(data, token);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken token) => inner.PrepareFile(path, token);
        public byte[] Read(VocabularyImageReference image) => inner.Read(image);
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image) { var created = inner.Write(data, image); cancellation.Cancel(); return created; }
        public void Remove(VocabularyImageReference image) => inner.Remove(image);
        public VocabularyWindowsFile.FileIdentity? WriteOwned(ReadOnlySpan<byte> data, VocabularyImageReference image) { var identity = inner.WriteOwned(data, image); cancellation.Cancel(); return identity; }
        public VocabularyWindowsFile.FileIdentity? CaptureIdentity(VocabularyImageReference image) => inner.CaptureIdentity(image);
        public void RemoveOwned(VocabularyImageReference image, VocabularyWindowsFile.FileIdentity identity) => inner.RemoveOwned(image, identity);
    }
    private sealed class ReplaceAfterWrite(IOwnedVocabularyImageStorage inner, string directory, CancellationTokenSource cancellation) : IOwnedVocabularyImageStorage
    {
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken token) => inner.Prepare(data, token);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken token) => inner.PrepareFile(path, token);
        public byte[] Read(VocabularyImageReference image) => inner.Read(image);
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image) => inner.Write(data, image);
        public void Remove(VocabularyImageReference image) => inner.Remove(image);
        public VocabularyWindowsFile.FileIdentity? WriteOwned(ReadOnlySpan<byte> data, VocabularyImageReference image)
        {
            var identity = inner.WriteOwned(data, image); string path = Path.Combine(directory, image.Filename);
            File.Move(path, path + ".original"); File.Copy(path + ".original", path); cancellation.Cancel(); return identity;
        }
        public VocabularyWindowsFile.FileIdentity? CaptureIdentity(VocabularyImageReference image) => inner.CaptureIdentity(image);
        public void RemoveOwned(VocabularyImageReference image, VocabularyWindowsFile.FileIdentity identity) => inner.RemoveOwned(image, identity);
    }
    private sealed class ReplaceAfterCapture(IOwnedVocabularyImageStorage inner, string directory) : IOwnedVocabularyImageStorage
    {
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken token) => inner.Prepare(data, token);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken token) => inner.PrepareFile(path, token);
        public byte[] Read(VocabularyImageReference image) => inner.Read(image);
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image) => inner.Write(data, image);
        public void Remove(VocabularyImageReference image) => inner.Remove(image);
        public VocabularyWindowsFile.FileIdentity? WriteOwned(ReadOnlySpan<byte> data, VocabularyImageReference image) => inner.WriteOwned(data, image);
        public VocabularyWindowsFile.FileIdentity? CaptureIdentity(VocabularyImageReference image)
        { var identity = inner.CaptureIdentity(image); string path = Path.Combine(directory, image.Filename); File.Move(path, path + ".original"); File.Copy(path + ".original", path); return identity; }
        public void RemoveOwned(VocabularyImageReference image, VocabularyWindowsFile.FileIdentity identity) => inner.RemoveOwned(image, identity);
    }
    private static byte[] Input => Convert.FromBase64String(JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json"))).RootElement.GetProperty("input").GetString()!);
    private static VocabularyPreparedImage Image() { var bytes = Input; return new(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes); }
    private static ReviewLayoutTemplate Template(VocabularyImageReference image) => new(Guid.NewGuid(), "image", new ReviewLayout(
        [new ReviewCardPart(Guid.NewGuid(), ReviewContentSource.Custom, content: [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image))])],
        [ReviewCardPart.Create(ReviewContentSource.Meaning)]));
    private static Dictionary<string, VocabularyPreparedImage> Supplied(VocabularyPreparedImage image) => new() { [image.Reference.Filename] = image };
    [Fact]
    public Task CommitReusesImageAndLastRemovalCleansAfterReopen() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            var image = Image(); var a = Template(image.Reference); var b = Template(image.Reference);
            await using (var store = FrozenVocabularyStoreFactory.Create(file, false))
            {
                Assert.False(Directory.Exists(root));
                var first = await store.SaveTemplateAsync(a, Supplied(image));
                string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName, image.Reference.Filename);
                Assert.True(File.Exists(media));
                var second = await store.SaveTemplateAsync(b, Supplied(image));
                var snapshot = await store.ReadMediaSnapshotAsync();
                Assert.Equal(second, snapshot.Snapshot); Assert.Single(snapshot.Images); Assert.Equal(image.CopyBytes(), snapshot.Images[0].CopyBytes());
                var bytes = snapshot.Images[0].CopyBytes(); bytes[0] ^= 1; Assert.Equal(image.CopyBytes(), snapshot.Images[0].CopyBytes());
                Assert.Equal(first.Revision + 1, second.Revision);
                await store.RemoveTemplateAsync(a.Id); Assert.True(File.Exists(media));
            }
            await using (var reopened = await FrozenVocabularyStoreFactory.OpenAsync(file, installBundledClassification: false))
            {
                Assert.Equal(image.CopyBytes(), await reopened.ReadImageAsync(image.Reference));
                await reopened.RemoveTemplateAsync(b.Id);
                Assert.False(File.Exists(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName, image.Reference.Filename)));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task SqlFailureRollsBackNewFileWithoutDeletingReusedFile() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            var image = Image(); var a = Template(image.Reference);
            await using var store = FrozenVocabularyStoreFactory.Create(file, false);
            var before = await store.ReadAsync();
            using var connection = VocabularySQLiteConnection.OpenNamed(file);
            connection.Execute("CREATE TRIGGER reject_media BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
            await Assert.ThrowsAnyAsync<VocabularyException>(() => store.SaveTemplateAsync(a, Supplied(image)));
            string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName, image.Reference.Filename);
            Assert.False(File.Exists(media)); Assert.Equal(before, await store.ReadAsync());
            connection.Execute("DROP TRIGGER reject_media"); await store.SaveTemplateAsync(a, Supplied(image));
            connection.Execute("CREATE TRIGGER reject_media BEFORE UPDATE ON state BEGIN SELECT RAISE(ABORT,'injected'); END");
            await Assert.ThrowsAnyAsync<VocabularyException>(() => store.SaveTemplateAsync(Template(image.Reference), Supplied(image)));
            Assert.True(File.Exists(media)); Assert.Equal(image.CopyBytes(), await store.ReadImageAsync(image.Reference));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task CancellationAfterStagingRemovesOnlyNewFile() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            var image = Image(); using var cancellation = new CancellationTokenSource();
            var storage = WindowsVocabularyImageStorage.ForDirectory(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName));
            await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(file),
                images: new CancelAfterWrite(storage, cancellation));
            var before = await store.ReadAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveTemplateAsync(Template(image.Reference), Supplied(image), cancellation.Token));
            Assert.Equal(before, await store.ReadAsync());
            Assert.False(File.Exists(Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName, image.Reference.Filename)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task StagingNameCollisionPreservesForeignFileAndSql() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
        try
        {
            VocabularyWindowsFile.CreateParentDirectory(media); var identity = Guid.NewGuid();
            string foreign = Path.Combine(media, ".vocabulary-image-" + identity.ToString("N") + ".tmp");
            byte[] marker = [4, 5, 6]; File.WriteAllBytes(foreign, marker);
            var storage = WindowsVocabularyImageStorage.ForDirectory(media, () => identity);
            await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(file), images: storage);
            var before = await store.ReadAsync(); var image = Image();
            await Assert.ThrowsAsync<VocabularyException>(() => store.SaveTemplateAsync(Template(image.Reference), Supplied(image)));
            Assert.Equal(marker, File.ReadAllBytes(foreign)); Assert.Equal(before, await store.ReadAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ReplacedSameBytesLeafSurvivesRollback() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
        try
        {
            using var cancellation = new CancellationTokenSource(); var image = Image();
            var storage = WindowsVocabularyImageStorage.ForDirectory(media);
            await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(file), images: new ReplaceAfterWrite(storage, media, cancellation));
            var before = await store.ReadAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveTemplateAsync(Template(image.Reference), Supplied(image), cancellation.Token));
            Assert.Equal(before, await store.ReadAsync());
            Assert.Equal(image.CopyBytes(), File.ReadAllBytes(Path.Combine(media, image.Reference.Filename)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task DeletionPinsVerifiedHandleAgainstReplacement() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
        try
        {
            var image = Image(); var template = Template(image.Reference); var storage = WindowsVocabularyImageStorage.ForDirectory(media);
            await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(file), images: storage);
            await store.SaveTemplateAsync(template, Supplied(image)); bool denied = false;
            string path = Path.Combine(media, image.Reference.Filename);
            storage.BeforeOwnedDelete = () => { try { File.Move(path, path + ".moved"); } catch (IOException) { denied = true; } };
            await store.RemoveTemplateAsync(template.Id);
            Assert.True(denied); Assert.False(File.Exists(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task ReplacedSameBytesLeafSurvivesPostCommitCleanup() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
        try
        {
            var image = Image(); var template = Template(image.Reference); var storage = WindowsVocabularyImageStorage.ForDirectory(media);
            await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(file), images: new ReplaceAfterCapture(storage, media));
            await store.SaveTemplateAsync(template, Supplied(image));
            var after = await store.RemoveTemplateAsync(template.Id);
            Assert.Empty(after.Document.ReviewTemplates!);
            Assert.Equal(image.CopyBytes(), File.ReadAllBytes(Path.Combine(media, image.Reference.Filename)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task CorruptExistingLeafIsAtomicallyRepairedForAcknowledgedMetadata() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
        try
        {
            var image = Image(); var template = Template(image.Reference);
            await using var store = FrozenVocabularyStoreFactory.Create(file, false);
            var before = await store.SaveTemplateAsync(template, Supplied(image));
            string path = Path.Combine(media, image.Reference.Filename); byte[] corrupt = [7, 8, 9];
            File.WriteAllBytes(path, corrupt);
            var added = Template(image.Reference); var after = await store.SaveTemplateAsync(added, Supplied(image));
            Assert.Equal(image.CopyBytes(), File.ReadAllBytes(path)); Assert.True(after.Revision > before.Revision);
            Assert.Contains(after.Document.ReviewTemplates!, value => value.Id == added.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailureAfterPublishRollsBackOnlyOriginalIdentity(bool replace) => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-media-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        string media = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
        try
        {
            var image = Image(); var storage = WindowsVocabularyImageStorage.ForDirectory(media);
            string path = Path.Combine(media, image.Reference.Filename);
            storage.AfterOwnedPublish = () =>
            {
                if (replace) { File.Move(path, path + ".original"); File.Copy(path + ".original", path); }
                throw new IOException("injected post-publication verification failure");
            };
            await using var store = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(file), images: storage);
            var before = await store.ReadAsync();
            await Assert.ThrowsAsync<VocabularyException>(() => store.SaveTemplateAsync(Template(image.Reference), Supplied(image)));
            Assert.Equal(before, await store.ReadAsync());
            if (replace) Assert.Equal(image.CopyBytes(), File.ReadAllBytes(path)); else Assert.False(File.Exists(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
