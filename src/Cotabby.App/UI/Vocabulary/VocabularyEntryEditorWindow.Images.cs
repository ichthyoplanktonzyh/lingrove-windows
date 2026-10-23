using System.Windows;
using System.Windows.Threading;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32;

namespace Cotabby.App.UI.Vocabulary;

public partial class VocabularyEntryEditorWindow
{
    // Only this editor owns these prepared bytes. Blocks keep stable references/captions; the
    // shared Store sees bytes only on Save. Deleting the last matching block prunes its draft.
    private readonly Dictionary<string, VocabularyPreparedImage> _importedImages = new(StringComparer.Ordinal);
    private readonly List<VocabularyImageView> _imageViews = [];
    private bool _importing;
    private Task _importPreparation = Task.CompletedTask;
    internal Task LastImport { get; private set; } = Task.CompletedTask;
    internal int ImportedImageCount => _importedImages.Count;
    private void OnAddImage(object sender, RoutedEventArgs args)
    {
        if (!CanImport()) return;
        var dialog = new OpenFileDialog { Title = "Add image", Multiselect = false, CheckFileExists = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif;*.ico;*.webp;*.heic;*.heif" };
        if (dialog.ShowDialog(this) == true) ImportImageFile(dialog.FileName);
    }
    private bool CanImport() => !_saving && !_retired && !_importing && Sense is not null && (Sense.Content?.Count ?? 0) < VocabularyContentPolicy.MaximumBlocks;
    internal bool ImportImageFile(string path) => BeginImageImport(async token => await _store.PrepareImageFileAsync(path, token).ConfigureAwait(false));
    internal bool ImportImageBytes(ReadOnlyMemory<byte> data)
    {
        Dispatcher.VerifyAccess(); if (!CanImport()) return false;
        // A borrowed memory caller may mutate as soon as this method returns. Store bounds and
        // detaches synchronously at admission. Clipboard paste already owns its private reply,
        // so that production path can perform the same Store admission away from WPF instead.
        var admitted = _store.PrepareImageAsync(data, _lifetime.Token);
        return BeginImageImport(async _ => await admitted.ConfigureAwait(false), admitted);
    }
    private bool BeginImageImport(Func<CancellationToken, Task<VocabularyPreparedImage?>> prepare, Task? alreadyAdmitted = null)
    {
        Dispatcher.VerifyAccess(); if (!CanImport()) return false;
        _importing = true; ImportFailure.Visibility = Visibility.Collapsed; ImportStatus.Visibility = Visibility.Visible; UpdateAdmission();
        LastImport = ImportAsync(prepare, _lifetime.Token, alreadyAdmitted); return true;
    }
    private async Task ImportAsync(Func<CancellationToken, Task<VocabularyPreparedImage?>> prepare, CancellationToken token, Task? alreadyAdmitted)
    {
        // File/clipboard factories run away from WPF, including the owned clipboard byte copy.
        // Presentation callbacks return to the dispatcher. Cancellation lets this task finish even
        // if the dispatcher has stopped or bounded normalization is already finishing elsewhere.
        try
        {
            var preparing = Task.Run(() => prepare(token), token);
            _importPreparation = alreadyAdmitted is null ? preparing : Task.WhenAll(preparing, alreadyAdmitted);
            _ = _importPreparation.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var prepared = await preparing.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); if (prepared is null) return;
            await PresentImportAsync(() =>
            {
                if (Sense is null) return;
                var block = VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference));
                var blocks = (Sense.Content ?? []).Append(block).ToArray(); VocabularyContentPolicy.Validate(blocks);
                _importedImages[prepared.Reference.Filename] = prepared;
                ChangeSense(sense => sense with { Content = blocks }); RenderBlocks();
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            await PresentImportAsync(() => { ImportFailure.Text = "Unable to add this image. Try a smaller image."; ImportFailure.Visibility = Visibility.Visible; }, token).ConfigureAwait(false);
        }
        finally
        {
            await PresentImportAsync(() => { _importing = false; ImportStatus.Visibility = Visibility.Collapsed; UpdateAdmission(); }, token).ConfigureAwait(false);
        }
    }
    private async Task PresentImportAsync(Action action, CancellationToken token)
    {
        if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(() => { if (!_retired) action(); }, DispatcherPriority.Normal, token).Task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
    private void PruneImports()
    {
        var retained = VocabularyContentPolicy.Images(Sense?.Content ?? []).Select(image => image.Filename).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _importedImages.Keys.Where(key => !retained.Contains(key)).ToArray()) _importedImages.Remove(key);
    }
    private void ClearImageViews() { foreach (var view in _imageViews) view.Dispose(); _imageViews.Clear(); }
    private void RetireImages() { ClearImageViews(); _importedImages.Clear(); }
}
