using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

public sealed partial class VocabularyArchive
{
    /// <summary>
    /// Restore replaces the complete notebook after its caller confirms. Like the Mac archive,
    /// this serialized worker validates the package and every PNG before any staging. The existing
    /// media transaction then owns cancellation, rollback, SQL acknowledgement and orphan cleanup.
    /// Stable sense/review/Anki identities are restored together rather than reconstructed.
    /// </summary>
    public Task<VocabularySnapshot> RestoreAsync(string source, CancellationToken cancellationToken = default) => Run(() =>
    {
        var backup = VocabularyBackupCodec.Decode(WindowsVocabularyBackupInput.ReadFile(source));
        var references = VocabularyRestorePolicy.Validate(backup);
        var images = backup.CopyImages();
        var prepared = new Dictionary<string, VocabularyPreparedImage>(StringComparer.Ordinal);
        foreach (var image in references)
        {
            byte[] bytes = images[image.Filename];
            WindowsVocabularyImagePreparer.Validate(bytes, image);
            prepared[image.Filename] = new(image, bytes);
        }
        // Cancellation starts at the original staging boundary. A cancelled call still reports
        // malformed/version/media admission failures first, but can never start a cancelled SQL edit.
        return CommitWithImages(backup.Document, prepared, cancellationToken);
    });
}
