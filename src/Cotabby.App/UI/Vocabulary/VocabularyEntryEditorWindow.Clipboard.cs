using System.Windows;
using System.Windows.Input;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

public partial class VocabularyEntryEditorWindow
{
    private void AttachImagePaste()
    {
        // Tunnel inside the explanation section before TextBox's own paste handler can request
        // a foreign IDataObject. Other fields and text-only clipboard offers keep normal paste.
        CommandManager.AddPreviewCanExecuteHandler(ExplanationDropArea, (_, args) =>
        {
            if (args.Command != ApplicationCommands.Paste || _clipboard?.Capture().HasImage != true) return;
            args.CanExecute = CanImport(); args.Handled = true;
        });
        CommandManager.AddPreviewExecutedHandler(ExplanationDropArea, (_, args) =>
        {
            if (args.Command != ApplicationCommands.Paste || _clipboard?.Capture() is not { HasImage: true } offer) return;
            args.Handled = true; ImportClipboard(offer);
        });
    }
    private void OnPasteImage(object sender, RoutedEventArgs args) => PasteImage();
    internal bool PasteImage()
    {
        Dispatcher.VerifyAccess();
        return _clipboard?.Capture() is { HasImage: true } offer && ImportClipboard(offer);
    }
    private bool ImportClipboard(VocabularyClipboardImageOffer offer) => BeginImageImport(async token =>
    {
        byte[]? bytes = await _clipboard!.ReadAsync(offer.Sequence, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return bytes is null ? null : await _store.PrepareImageAsync(bytes, token).ConfigureAwait(false);
    });
}
