using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Mac archive export snapshot boundary. The archive's serialized worker and one shared storage
/// access span capture exact senses plus validated media before rendering. The returned package
/// owns all selected bytes; queued edits/GC and later source deletion cannot change that value.
/// Destination validation, writing and ZIP encoding are separate effects outside preparation.
/// </summary>
public sealed partial class VocabularyArchive
{
    public Task<VocabularyExportPackage> PrepareExportAsync(IReadOnlyList<VocabularySelection> selections,
        VocabularyExportConfiguration options, VocabularyExportTextLabels? labels = null, CancellationToken cancellationToken = default)
    {
        // Capture the sheet's offered IDs before queueing, adapting Swift Array value semantics.
        // Cancellation ordering belongs to selected/package, not to this synchronous admission.
        var captured = selections.ToArray();
        return Run(() =>
        {
            var (selected, media) = Persistence().Access(false, () => ExportSnapshot(captured, options, cancellationToken));
            var package = VocabularyExportPolicy.Package(selected, options, labels, cancellationToken).WithImages(media);
            cancellationToken.ThrowIfCancellationRequested();
            return package;
        });
    }
    private (IReadOnlyList<VocabularyEntry> Entries, IReadOnlyDictionary<string, byte[]> Media) ExportSnapshot(
        IReadOnlyList<VocabularySelection> selections, VocabularyExportConfiguration options, CancellationToken cancellationToken)
    {
        var selected = VocabularyExportPolicy.Selected(Load().Document.Entries, selections, cancellationToken);
        var references = VocabularyExportPolicy.Images(selected, options); long total = 0;
        foreach (var image in references)
        {
            if (image.ByteCount > MaximumMediaBytes - total) throw new VocabularyExportException(VocabularyExportFailure.TooLarge);
            total += image.ByteCount;
        }
        var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var image in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The image owner already validates complete reference metadata and returns detached
            // bytes. Mac maps reader failures to fixed missingImage; token checks stay outside it.
            try { media[image.Filename] = ImageStorage.Read(image); }
            catch { throw new VocabularyExportException(VocabularyExportFailure.MissingImage); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return (selected, media);
    }
}
