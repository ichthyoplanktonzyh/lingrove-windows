using System.IO;
using System.Windows.Threading;
using Cotabby.App.Learning;
using Cotabby.Core.Models;
using Cotabby.Win32.Models;

namespace Cotabby.App.Speech;

/// <summary>Speech has its own lazily scanned directory and its own transfer lifetime. The
/// existing bounded GGUF staging/import manager is reused with that directory, never the
/// completion or translation manager. Deletion rechecks the controller's active/resident
/// protection on its UI context immediately before the no-follow Windows unlink.</summary>
internal sealed class SpeechModelFiles(string directory, Dispatcher dispatcher) : ISpeechModelManagement, IAsyncDisposable
{
    private readonly LearningModelCatalogManager _files = new(directory);
    private bool _closed;
    public Action? DirectoryChanged { get; set; }
    public Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; } = () => new HashSet<string>();
    public IReadOnlyList<RuntimeModelOption> InstalledModelOptions()
    {
        dispatcher.VerifyAccess(); return !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*.gguf")
            .Select(path => new RuntimeModelOption(Path.GetFileName(path), new Uri(Path.GetFullPath(path))))
            .OrderBy(model => model.Filename, StringComparer.CurrentCulture).ToArray();
    }
    internal async Task<LearningModelImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken token)
    {
        dispatcher.VerifyAccess(); var result = await _files.ImportAsync(paths, token); token.ThrowIfCancellationRequested(); if (!_closed) DirectoryChanged?.Invoke(); return result;
    }
    internal Task<bool> DeleteAsync(string name, CancellationToken token)
    {
        dispatcher.VerifyAccess(); return _files.DeleteAsync(name, async _ =>
            await dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested(); if (_closed || IsProtected(name)) return false;
                WindowsModelFile.Delete(directory, name); DirectoryChanged?.Invoke(); return true;
            }).Task, token);
    }
    public ValueTask DisposeAsync() { dispatcher.VerifyAccess(); _closed = true; return _files.DisposeAsync(); }
    internal bool IsProtected(string name) { var names = ProtectedModelFilenames(); return names.Contains(name) || names.Any(value => StringComparer.OrdinalIgnoreCase.Equals(value, name)); }
}
