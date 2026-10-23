using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// The archive serializes save effects over an already-owned prepared package. Choosing a file is
/// presentation's job; this worker validates it before encoding, writes captured bytes only, and
/// acknowledges the atomic rename without a postcommit cancellation failure. Closing drains the
/// whole save through the existing archive lifetime owner. It never reloads live entries/media.
/// </summary>
public sealed partial class VocabularyArchive
{
    private readonly string? _exportNotebookPath;
    public Task WriteExportAsync(VocabularyExportPackage package, string destination, CancellationToken cancellationToken = default) => Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var file = new WindowsVocabularyExportFile(destination, _exportNotebookPath);
        byte[] data = package.Configuration.IncludesImages ? VocabularyExportZip.Data(package, cancellationToken) : package.CopyData();
        if (data.Length > VocabularyExportZip.MaximumBytes) throw new VocabularyExportException(VocabularyExportFailure.TooLarge);
        file.Write(data, cancellationToken);
        return true;
    });
}
