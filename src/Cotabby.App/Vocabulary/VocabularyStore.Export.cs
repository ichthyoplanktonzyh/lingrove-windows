using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>Export borrows the app-owned archive without mutating the Store's acknowledged graph
/// or shared error state. Track preparation and save for shutdown just like image operations;
/// failures belong to the one export sheet and propagate to its session.</summary>
internal sealed partial class VocabularyStore
{
    internal Task<VocabularyExportPackage> PrepareExportAsync(IReadOnlyList<VocabularySelection> selections,
        VocabularyExportConfiguration options, CancellationToken cancellationToken = default) =>
        MediaOperation(() => archive.PrepareExportAsync(selections, options, cancellationToken: cancellationToken));
    internal Task WriteExportAsync(VocabularyExportPackage package, string destination, CancellationToken cancellationToken = default) =>
        MediaOperation(async () => { await archive.WriteExportAsync(package, destination, cancellationToken).ConfigureAwait(false); return true; });
}
