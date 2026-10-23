using Cotabby.Core.Vocabulary;

namespace Cotabby.Win32.Vocabulary;

/// <summary>One export sheet fixes the captured package's filename/type. The shared native owner
/// handles STA Show/cancellation/release and returns a path; the archive protects and writes it.</summary>
public sealed class WindowsVocabularyExportDialog : IDisposable
{
    private readonly WindowsVocabularyFileDialog _dialog;
    public WindowsVocabularyExportDialog(VocabularyExportConfiguration options)
    {
        string extension = options.IncludesImages ? "zip" : options.Format.FileExtension();
        _dialog = new(extension, "Lingrove-vocabulary." + extension, opening: false);
    }
    public string? Show(IntPtr owner, Action<Action> postToOwner, CancellationToken cancellationToken = default) =>
        _dialog.Show(owner, postToOwner, cancellationToken);
    internal (string Filename, uint Options, uint TypeIndex) ReadConfiguration()
    {
        var value = _dialog.ReadConfiguration();
        return (value.Filename ?? throw new InvalidOperationException("No configured export filename."), value.Options, value.TypeIndex);
    }
    public void Dispose() => _dialog.Dispose();
}
