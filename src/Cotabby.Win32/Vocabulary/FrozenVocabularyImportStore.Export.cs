using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.Vocabulary;

internal sealed partial class FrozenVocabularyImportStore
{
    public Task<VocabularyExportPackage> PrepareExportAsync(IReadOnlyList<VocabularySelection> selections, VocabularyExportConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var ids = selections.ToArray(); var options = configuration with { };
        return Run(() =>
        {
            var snapshot = Database.Access(true, () =>
            {
                var current = LoadCurrent(cancellationToken).Document;
                var entries = FrozenVocabularyExportPolicy.Selected(current.CollectedEntries, ids, cancellationToken);
                var references = FrozenVocabularyExportPolicy.Images(entries, options);
                if (references.Sum(x => (long)x.ByteCount) > FrozenVocabularyRestorePolicy.MaximumMediaBytes) throw new VocabularyExportException(VocabularyExportFailure.TooLarge);
                var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var image in references)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { images.Add(image.Filename, ImageStorage.Read(image)); }
                    catch (OperationCanceledException) { throw; }
                    catch { throw new VocabularyExportException(VocabularyExportFailure.MissingImage); }
                }
                return (entries, current.Semantics, current.Classification, images);
            });
            var package = FrozenVocabularyExportPolicy.Package(snapshot.entries, options, snapshot.Semantics, snapshot.Classification, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); return package.WithImages(snapshot.images);
        }, cancellationToken);
    }
    public Task WriteExportAsync(VocabularyExportPackage package, string destination, CancellationToken cancellationToken = default) => Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? media = (_images as WindowsVocabularyImageStorage)?.DirectoryPath;
        using var file = new WindowsVocabularyExportFile(destination, _notebookPath, protectedMedia: media, protectLegacySibling: true);
        var data = package.Configuration.IncludesImages ? VocabularyExportZip.Data(package, cancellationToken) : package.CopyData();
        if (data.Length > VocabularyExportZip.MaximumBytes) throw new VocabularyExportException(VocabularyExportFailure.TooLarge);
        cancellationToken.ThrowIfCancellationRequested(); file.Write(data, cancellationToken); return true;
    }, cancellationToken);
}
