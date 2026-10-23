using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Complete Mac backup effect on the existing archive worker. One shared storage span freezes the
/// acknowledged graph and every referenced media byte, including review layouts/templates. Once
/// owned, encoding and fsync release that cross-owner lock. The existing destination lease protects
/// live storage and publishes privately staged bytes; backup never edits SQL or advances revision.
/// Like the original backup operation, admission is drained through completion rather than cancelled
/// halfway through publication. Learning-material exports retain their separate cancellable session.
/// </summary>
public sealed partial class VocabularyArchive
{
    private const int MaximumBackupBytes = 192 * 1024 * 1024;

    public Task BackupAsync(string destination) => Run(() =>
    {
        // Destination protection precedes even loading the notebook. A corrupt live database
        // cannot turn a protected-path refusal into a read, overwrite or partial backup attempt.
        using var file = new WindowsVocabularyExportFile(destination, _exportNotebookPath);
        var snapshot = Persistence().Access(false, BackupSnapshot);
        byte[] data = VocabularyBackupCodec.Encode(snapshot);
        if (data.Length > MaximumBackupBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
        file.Write(data, CancellationToken.None);
        return true;
    });

    private VocabularyBackup BackupSnapshot()
    {
        var document = Load().Document;
        var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var image in Images(document).ToHashSet()) media[image.Filename] = ImageStorage.Read(image);
        // Preserve Mac's ordering: actual media reads fail before the aggregate byte gate.
        // Images() includes sense content, all retained card plans and global templates.
        long total = media.Values.Sum(bytes => (long)bytes.Length);
        if (total > MaximumMediaBytes) throw new VocabularyException(VocabularyFailure.OversizedArchive);
        return new(document, media);
    }
}
