using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Cotabby.Core.Vocabulary.Resources;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyBackupStoreTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task FullGraphBackupRestoreAndProtectedPaths() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string database = Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalFileName), backup = Path.Combine(root, "backup.lingrove");
            await using var store = FrozenVocabularyStoreFactory.Create(database, false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money", Example: "bank opens")]); var initial = await store.ReadAsync();
            await store.BackupAsync(backup); Assert.True(File.Exists(backup)); Assert.Equal(initial.Revision, (await store.ReadAsync()).Revision);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "other", Meaning: "else")]); var changed = await store.ReadAsync();
            var restored = await store.RestoreAsync(backup); Assert.Equal(changed.Revision + 1, restored.Revision); Assert.Equal(initial.Document, restored.Document);
            Assert.Equal(restored.Document, (await store.ReadAsync()).Document);
            await Assert.ThrowsAsync<VocabularyException>(() => store.BackupAsync(database));
            string old = Path.Combine(root, "data", "vocabulary.sqlite"); byte[] sentinel = [1, 2, 3]; await File.WriteAllBytesAsync(old, sentinel);
            await Assert.ThrowsAsync<VocabularyException>(() => store.BackupAsync(old)); Assert.Equal(sentinel, await File.ReadAllBytesAsync(old));
            string media = Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName); Directory.CreateDirectory(media);
            await Assert.ThrowsAsync<VocabularyException>(() => store.BackupAsync(Path.Combine(media, "backup.lingrove")));
            byte[] malformed = "{ invalid"u8.ToArray(); string invalid = Path.Combine(root, "bad.lingrove"); await File.WriteAllBytesAsync(invalid, malformed);
            await Assert.ThrowsAsync<VocabularyException>(() => store.RestoreAsync(invalid)); var after = await store.ReadAsync(); Assert.Equal(restored.Revision, after.Revision); Assert.Equal(restored.Document, after.Document);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task RestoreStagesValidatedImageAndReopensNamedDatabase() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string database = Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalFileName), backup = Path.Combine(root, "backup.lingrove"); VocabularyImageReference image;
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!);
            var prepared = new VocabularyPreparedImage(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes); image = prepared.Reference;
            await using (var store = FrozenVocabularyStoreFactory.Create(database, false))
            {
                await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.Entries[0];
                var next = entry with { Senses = [entry.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image, "caption"))] }] };
                await store.UpdateEntryAsync(next, entry, new Dictionary<string, VocabularyPreparedImage> { [image.Filename] = prepared }); await store.BackupAsync(backup);
                await store.UpdateEntryAsync(entry, next); Assert.Empty(F.FrozenVocabularyRestorePolicy.References((await store.ReadAsync()).Document));
                await store.RestoreAsync(backup); Assert.Equal(bytes, await store.ReadImageAsync(image));
            }
            await using (var reopened = await FrozenVocabularyStoreFactory.OpenAsync(database, installBundledClassification: false))
            { Assert.Equal(bytes, await reopened.ReadImageAsync(image)); Assert.Single(F.FrozenVocabularyRestorePolicy.References((await reopened.ReadAsync()).Document)); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    [Fact]
    public Task RestoreRetainsResourceEvidenceAndCurrentCategoryGuidance() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory); while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "global.json"))) repo = repo.Parent;
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo!.FullName, "fixtures", "parity", "vocabulary-document-frozen-v1.json")));
            var input = fixture.RootElement.GetProperty("cases").EnumerateArray().First(test => test.GetProperty("id").GetString() == "optional-absence").GetProperty("input");
            var document = F.VocabularyDocumentCodec.Decode(Encoding.UTF8.GetBytes(input.GetRawText())); var categories = document.Classification.Categories.ToArray();
            categories[0] = categories[0] with { ScopeNote = "Guidance", Examples = [new VocabularyCategoryExample("Example", "en")], Localizations = [new VocabularyCategoryLocalization("fr", Name: "Nom", ScopeNote: "Portée")] };
            document = document with { Classification = document.Classification with { Categories = categories } };
            string path = Path.Combine(root, "current.lingrove"); await File.WriteAllBytesAsync(path, F.FrozenVocabularyBackupCodec.Encode(new(document, new Dictionary<string, byte[]>())));
            await using var store = FStore(root); var restored = await store.RestoreAsync(path);
            Assert.Equal(document.Sources, restored.Document.Sources); Assert.Equal(document.Occurrences, restored.Document.Occurrences); Assert.Equal(document.Interpretations, restored.Document.Interpretations);
            Assert.Equal(document.Semantics, restored.Document.Semantics); Assert.Equal(document.Classification, restored.Document.Classification); Assert.Equal(document.Collections[0].Review, restored.Document.Collections[0].Review);
            Assert.Equal(BitConverter.DoubleToInt64Bits(document.Sources[0].EncounteredAt.ReferenceSeconds), BitConverter.DoubleToInt64Bits(restored.Document.Sources[0].EncounteredAt.ReferenceSeconds));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
    private static F.ICanonicalVocabularyStore FStore(string root) => FrozenVocabularyStoreFactory.Create(Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalFileName), false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RestoreFailureOrCancellationRollsBackNewImageAndLeavesGraph(bool cancel) => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-backup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string sourceDatabase = Path.Combine(root, "source", FrozenVocabularyStoreFactory.CanonicalFileName), backup = Path.Combine(root, "backup.lingrove");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!); var prepared = new VocabularyPreparedImage(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes);
            await using (var source = FrozenVocabularyStoreFactory.Create(sourceDatabase, false))
            {
                await source.AddEntriesAsync([new VocabularyEntryInput(Expression: "source", Meaning: "value")]); var entry = (await source.ReadAsync()).Document.Entries[0];
                await source.UpdateEntryAsync(entry with { Senses = [entry.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference))] }] }, entry, new Dictionary<string, VocabularyPreparedImage> { [prepared.Reference.Filename] = prepared });
                await source.BackupAsync(backup);
            }
            string targetDatabase = Path.Combine(root, "target", FrozenVocabularyStoreFactory.CanonicalFileName), media = Path.Combine(root, "target", FrozenVocabularyStoreFactory.CanonicalMediaDirectoryName);
            var images = WindowsVocabularyImageStorage.ForDirectory(media); using var cancellation = new CancellationTokenSource();
            images.AfterOwnedPublish = () => { if (cancel) cancellation.Cancel(); else throw new IOException("injected after publication"); };
            await using var target = new FrozenVocabularyImportStore(() => FrozenVocabularySQLiteDatabase.OpenFile(targetDatabase), images: images, notebookPath: targetDatabase);
            await target.AddEntriesAsync([new VocabularyEntryInput(Expression: "target", Meaning: "original")]); var before = await target.ReadAsync();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => target.RestoreAsync(backup, cancellation.Token));
            else await Assert.ThrowsAsync<VocabularyException>(() => target.RestoreAsync(backup));
            var after = await target.ReadAsync(); Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Document, after.Document); Assert.False(File.Exists(Path.Combine(media, prepared.Reference.Filename)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

}
