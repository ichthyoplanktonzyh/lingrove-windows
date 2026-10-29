namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Committed snapshot and the stable destination chosen for a create or move.</summary>
public sealed record CanonicalSenseReceipt(FrozenVocabularyImportSnapshot Snapshot, Guid SenseId);
public interface ICanonicalVocabularySenseStore
{
    Task<CanonicalSenseReceipt> CreateSenseAsync(Guid entryId, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> MergeSensesAsync(Guid entryId, Guid sourceId, Guid targetId, CancellationToken cancellationToken = default);
    Task<FrozenVocabularyImportSnapshot> RemoveSenseAsync(Guid entryId, Guid senseId, CancellationToken cancellationToken = default);
    Task<CanonicalSenseReceipt> MoveExampleAsync(Guid entryId, Guid exampleId, Guid sourceId, Guid? targetId, CancellationToken cancellationToken = default);
}
