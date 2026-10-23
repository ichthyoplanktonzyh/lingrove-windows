using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Original restore's deterministic admission, before any file staging or SQL edit. The archive
/// supplies one decoded owned envelope; version, complete graph, exact media keys and actual-byte
/// aggregate are checked in that order. Returned references identify the native PNG validations
/// still required. A decoded/structurally admitted backup is never itself proof of valid image bytes.
/// </summary>
public static class VocabularyRestorePolicy
{
    public const int MaximumBackupBytes = 192 * 1024 * 1024;
    public const long MaximumMediaBytes = 128L * 1024 * 1024;
    public static IReadOnlyList<VocabularyImageReference> Validate(VocabularyBackup backup)
    {
        if (backup.Version != 1) throw new VocabularyException(VocabularyFailure.UnsupportedVersion);
        VocabularyArchiveValidation.Validate(backup.Document);
        var references = VocabularyArchiveMediaPolicy.References(backup.Document).ToHashSet();
        if (!references.Select(image => image.Filename).ToHashSet(StringComparer.Ordinal).SetEquals(backup.ImageBytes.Keys)
            || backup.ImageBytes.Values.Sum(bytes => (long)bytes.Length) > MaximumMediaBytes)
            throw new VocabularyException(VocabularyFailure.InvalidArchive);
        return CollectionValues.List(references);
    }
}
