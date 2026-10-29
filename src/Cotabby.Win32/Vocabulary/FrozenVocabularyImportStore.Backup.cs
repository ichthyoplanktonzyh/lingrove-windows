using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

/// <summary>Full current-format backup and restore run through the sole serialized Store. File
/// publication, owned media and SQL acknowledgement keep the same identity and lock boundaries.</summary>
internal sealed partial class FrozenVocabularyImportStore
{
    public Task BackupAsync(string destination, CancellationToken cancellationToken = default) => Run(() =>
    {
        string? media = (_images as WindowsVocabularyImageStorage)?.DirectoryPath;
        using var file = new WindowsVocabularyExportFile(destination, _notebookPath, protectedMedia: media, protectLegacySibling: true);
        var backup = Database.Access(true, () =>
        {
            var current = LoadCurrent(cancellationToken); var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var image in FrozenVocabularyRestorePolicy.References(current.Document)) images[image.Filename] = ImageStorage.Read(image);
            if (images.Values.Sum(bytes => (long)bytes.Length) > FrozenVocabularyRestorePolicy.MaximumMediaBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
            return new FrozenVocabularyBackup(current.Document, images);
        });
        byte[] encoded = FrozenVocabularyBackupCodec.Encode(backup);
        file.Write(encoded, CancellationToken.None); return true;
    }, cancellationToken);

    public Task<FrozenVocabularyImportSnapshot> RestoreAsync(string source, CancellationToken cancellationToken = default) => Run(() =>
    {
        var backup = FrozenVocabularyBackupCodec.Decode(WindowsVocabularyBackupInput.ReadFile(source));
        var references = FrozenVocabularyRestorePolicy.Validate(backup); var supplied = backup.CopyImages();
        var prepared = new Dictionary<string, VocabularyPreparedImage>(StringComparer.Ordinal);
        foreach (var image in references)
        {
            var bytes = supplied[image.Filename]; WindowsVocabularyImagePreparer.Validate(bytes, image); prepared.Add(image.Filename, new(image, bytes));
        }
        return Database.Access(true, () =>
        {
            cancellationToken.ThrowIfCancellationRequested(); var before = LoadCurrent(cancellationToken); var document = backup.Document;
            if (_installClassification)
            {
                var catalog = _catalog ?? _loadCatalog() ?? throw new InvalidOperationException("Catalog loader returned null.");
                var installation = VocabularyClassificationInstaller.Install(document, catalog); document = installation.Document; _catalog = catalog;
            }
            ValidateMediaContent(document); var existing = ImageReferences(before.Document); var next = ImageReferences(document); ValidateMediaBudget(next);
            var retained = next.Select(image => image.Filename).ToHashSet(StringComparer.Ordinal);
            var retired = new List<(VocabularyImageReference Image, VocabularyWindowsFile.FileIdentity Identity)>();
            foreach (var image in existing.Where(image => !retained.Contains(image.Filename))) if (CaptureCleanupIdentity(image) is { } id) retired.Add((image, id));
            var written = new List<(VocabularyImageReference Image, VocabularyWindowsFile.FileIdentity Identity)>();
            try
            {
                foreach (var image in next)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!prepared.TryGetValue(image.Filename, out var value)) throw new VocabularyException(VocabularyFailure.InvalidArchive);
                    if (OwnedImageStorage.WriteOwned(value.CopyBytes(), image) is { } id) written.Add((image, id));
                }
                cancellationToken.ThrowIfCancellationRequested(); var committed = CanonicalWritingPolicy.PrepareRestore(document);
                Cotabby.Core.Vocabulary.Frozen.VocabularyArchiveValidation.Validate(committed);
                ulong revision = Database.Write(committed, before.Document, before.Revision, cancellationToken: cancellationToken);
                foreach (var item in retired) RemoveOwnedImage(item.Image, item.Identity);
                var result = new FrozenVocabularyImportSnapshot(revision, committed); _notifications.Writer.TryWrite(result); return result;
            }
            catch { foreach (var item in written) RemoveOwnedImage(item.Image, item.Identity); throw; }
        });
    }, cancellationToken);
}
