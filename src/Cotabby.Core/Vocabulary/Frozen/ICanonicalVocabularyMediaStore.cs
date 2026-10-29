using Cotabby.Core.Support;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>One lock-consistent graph and detached, validated media for export preparation.</summary>
public sealed record CanonicalVocabularyMediaSnapshot
{
    public CanonicalVocabularyMediaSnapshot(FrozenVocabularyImportSnapshot snapshot, IReadOnlyList<VocabularyPreparedImage> images)
    { Snapshot = snapshot; Images = CollectionValues.List(images); }
    public FrozenVocabularyImportSnapshot Snapshot { get; }
    public IReadOnlyList<VocabularyPreparedImage> Images { get; }
}
public interface ICanonicalVocabularyMediaStore
{
    Task<VocabularyPreparedImage> PrepareImageAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    Task<VocabularyPreparedImage> PrepareImageFileAsync(string path, CancellationToken cancellationToken = default);
    Task<byte[]> ReadImageAsync(VocabularyImageReference reference, CancellationToken cancellationToken = default);
    Task<CanonicalVocabularyMediaSnapshot> ReadMediaSnapshotAsync(CancellationToken cancellationToken = default);
}
