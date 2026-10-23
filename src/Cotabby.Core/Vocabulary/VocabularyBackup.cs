using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// One portable full-notebook envelope, adapted from Mac VocabularyBackup. Unlike learning-material
/// export it retains the complete immutable document, review identities/receipts and embedded PNG
/// bytes. A future archive snapshot owns this value; no file or Store is owned here. Byte maps copy
/// at admission and on public access so a caller cannot change a prepared restore/backup in flight.
/// Version/media/graph admission belongs to restore, not to the Codable representation itself.
/// </summary>
public sealed class VocabularyBackup
{
    private readonly IReadOnlyDictionary<string, byte[]> _images;
    public VocabularyBackup(VocabularyDocument document, IReadOnlyDictionary<string, byte[]> images, long version = 1)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(images);
        Document = document; Version = version;
        _images = CollectionValues.Map(images.Select(pair => new KeyValuePair<string, byte[]>(pair.Key,
            (pair.Value ?? throw new ArgumentException("Backup media bytes are required.", nameof(images))).ToArray())));
    }
    public long Version { get; }
    public VocabularyDocument Document { get; }
    public IReadOnlyDictionary<string, byte[]> CopyImages() => CollectionValues.Map(
        _images.Select(pair => new KeyValuePair<string, byte[]>(pair.Key, pair.Value.ToArray())));
    // Trusted codec/admission rules borrow private arrays synchronously; effect owners get copies.
    internal IReadOnlyDictionary<string, byte[]> ImageBytes => _images;
}
