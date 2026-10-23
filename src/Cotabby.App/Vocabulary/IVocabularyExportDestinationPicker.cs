using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>A window-owned explicit destination decision. Tests may control the response; the
/// production adapter owns the actual cancellable Shell save panel and performs no writing.</summary>
internal interface IVocabularyExportDestinationPicker
{
    Task<string?> ChooseAsync(Window owner, VocabularyExportPackage package, CancellationToken cancellationToken);
}

internal sealed class WindowsVocabularyExportDestinationPicker : IVocabularyExportDestinationPicker
{
    public Task<string?> ChooseAsync(Window owner, VocabularyExportPackage package, CancellationToken cancellationToken)
    {
        owner.Dispatcher.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new WindowsVocabularyExportDialog(package.Configuration);
        string? selected = dialog.Show(new WindowInteropHelper(owner).EnsureHandle(), action =>
        {
            if (owner.Dispatcher.CheckAccess()) action();
            else if (!owner.Dispatcher.HasShutdownStarted) _ = owner.Dispatcher.BeginInvoke(action, DispatcherPriority.Send);
        }, cancellationToken);
        return Task.FromResult(selected);
    }
}
