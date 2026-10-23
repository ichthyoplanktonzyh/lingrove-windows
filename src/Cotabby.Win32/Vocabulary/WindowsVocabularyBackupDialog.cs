namespace Cotabby.Win32.Vocabulary;

/// <summary>
/// Windows counterpart of the notebook's NSSavePanel/NSOpenPanel, fixed to the original .lingrove
/// backup type. A notebook command owns one invocation; selecting a restore path does not admit
/// restore until the separate replacement confirmation. This adapter never reads or writes it.
/// </summary>
public sealed class WindowsVocabularyBackupDialog : IDisposable
{
    private readonly WindowsVocabularyFileDialog _dialog;
    public WindowsVocabularyBackupDialog(bool restore) =>
        _dialog = new("lingrove", restore ? null : "Lingrove-vocabulary.lingrove", opening: restore);
    public string? Show(IntPtr owner, Action<Action> postToOwner, CancellationToken cancellationToken = default) =>
        _dialog.Show(owner, postToOwner, cancellationToken);
    internal (string? Filename, uint Options, uint TypeIndex) ReadConfiguration() => _dialog.ReadConfiguration();
    public void Dispose() => _dialog.Dispose();
}
