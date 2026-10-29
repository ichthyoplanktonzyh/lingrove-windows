namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Full canonical notebook operations borrowed from the single Store. Backup freezes an
/// owned snapshot; restore publishes only an acknowledged complete graph and media transaction.</summary>
public interface ICanonicalVocabularyBackupStore
{
    Task BackupAsync(string destination, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> RestoreAsync(string source, CancellationToken cancellationToken = default);
}
