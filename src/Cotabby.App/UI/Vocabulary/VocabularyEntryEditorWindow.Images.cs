using System.Windows;
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
    internal bool ImportImageFile(string path) => BeginImageImport(token => _store.PrepareImageFileAsync(path, token));
    internal bool ImportImageBytes(ReadOnlyMemory<byte> data) => BeginImageImport(token => _store.PrepareImageAsync(data, token));
    private bool BeginImageImport(Func<CancellationToken, Task<VocabularyPreparedImage>> prepare)
    {
        Dispatcher.VerifyAccess(); if (!CanImport()) return false;
        _importing = true; ImportFailure.Visibility = Visibility.Collapsed; ImportStatus.Visibility = Visibility.Visible; UpdateAdmission();
        LastImport = ImportAsync(prepare, _lifetime.Token); return true;
    }
    private async Task ImportAsync(Func<CancellationToken, Task<VocabularyPreparedImage>> prepare, CancellationToken token)
    {
        // This runs on the dispatcher so append+map admission is one atomic UI transition. The
        // archive work is off-thread; cancelling the wait prevents a late result resurrecting a
        // closed editor even if the underlying bounded normalization is already finishing.
        try
        {
            var prepared = await prepare(token).WaitAsync(token);
            token.ThrowIfCancellationRequested(); if (_retired || Sense is null) return;
            var block = VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference));
            var blocks = (Sense.Content ?? []).Append(block).ToArray(); VocabularyContentPolicy.Validate(blocks);
            _importedImages[prepared.Reference.Filename] = prepared;
            ChangeSense(sense => sense with { Content = blocks }); RenderBlocks();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_retired) { ImportFailure.Text = "Unable to add this image. Try a smaller image."; ImportFailure.Visibility = Visibility.Visible; }
        }
        finally
        {
            _importing = false;
            if (!_retired) { ImportStatus.Visibility = Visibility.Collapsed; UpdateAdmission(); }
        }
    }
    private void PruneImports()
    {
        var retained = VocabularyContentPolicy.Images(Sense?.Content ?? []).Select(image => image.Filename).ToHashSet(StringComparer.Ordinal);
        foreach (string key in _importedImages.Keys.Where(key => !retained.Contains(key)).ToArray()) _importedImages.Remove(key);
    }
    private void ClearImageViews() { foreach (var view in _imageViews) view.Dispose(); _imageViews.Clear(); }
    private void RetireImages() { ClearImageViews(); _importedImages.Clear(); }
}
