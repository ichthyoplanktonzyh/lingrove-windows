using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.LanguageLearning;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Compiled editor/notebook controls with actual WIC, SQLite and private temporary files. Tests
/// use the file-picker's production callback rather than automating an OS modal dialog. Gated
/// storage delays actual prepared/read bytes to exercise cancellation and stale presentation;
/// it never fabricates an image, lookup or persistence acknowledgement.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyImageUiTests(WpfDispatcher dispatcher)
{
    private static byte[] Input => Convert.FromBase64String(JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-image-storage-v1.json"))).RootElement.GetProperty("input").GetString()!);
    private static VocabularyCapture Capture(string expression = "bank") => new(expression, expression, LearningLanguage.English, VocabularyEntryKind.Word, "meaning");
    private static VocabularyStore Store(VocabularyArchive archive) => new(archive, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    private static VocabularyEntryEditorWindow Editor(VocabularyStore store)
    { var entry = store.Entries[0]; var window = new VocabularyEntryEditorWindow(store, entry, entry.Senses[0].Id); window.Show(); window.UpdateLayout(); return window; }
    private static T Named<T>(Window window, string name) where T : FrameworkElement => Assert.IsType<T>(window.FindName(name));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T value) yield return value;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
    private static VocabularyImageReference Reference(VocabularyEntryEditorWindow editor) =>
        Assert.IsType<VocabularyBlockContent.Image>(editor.Draft.Senses[0].Content![0].Content).Reference;
    private static async Task<VocabularyImageView[]> Images(Window window)
    {
        window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var images = Children<VocabularyImageView>(window).ToArray(); await Task.WhenAll(images.Select(view => view.LastLoad)).WaitAsync(TimeSpan.FromSeconds(10)); return images;
    }
    private static async Task<bool> Available(VocabularyArchive archive, VocabularyImageReference reference)
    { try { _ = await archive.ReadImageAsync(reference); return true; } catch (VocabularyException) { return false; } }
    private static Button BlockButton(VocabularyEntryEditorWindow editor, int index, string label) =>
        Assert.Single(Children<Button>(Named<StackPanel>(editor, "BlocksContent").Children[index]), button => button.Content as string == label);

    [Fact]
    public Task FileImportPreviewsDraftCaptionAndCancelWritesNothing() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); var archive = new VocabularyArchive(folder.Notebook); await using var store = Store(archive);
        Assert.True(await store.SaveAsync(Capture())); var before = store.Snapshot; var editor = Editor(store);
        try
        {
            Assert.True(editor.ImportImageFile(folder.Source)); await editor.LastImport;
            var view = Assert.Single(await Images(editor)); Assert.NotNull(view.Bitmap); Assert.True(view.Bitmap.IsFrozen);
            Assert.Equal(80, view.Bitmap.PixelWidth); Assert.Equal(40, view.Bitmap.PixelHeight); Assert.Equal(1, editor.ImportedImageCount);
            var reference = Reference(editor); Assert.False(await Available(archive, reference)); Assert.False(Directory.Exists(folder.Media));
            var caption = Assert.Single(Children<TextBox>(editor), box => AutomationProperties.GetName(box) == "Caption"); caption.Text = "River bank";
            Assert.Equal("River bank", AutomationProperties.GetName(view));
            Assert.Equal(before.Revision, store.Snapshot.Revision); Assert.Equal(before.Document, store.Snapshot.Document);
            Click(Named<Button>(editor, "CancelButton")); Assert.False(editor.IsVisible); Assert.Equal(0, editor.ImportedImageCount);
            Assert.False(Directory.Exists(folder.Media)); Assert.Equal(Input, File.ReadAllBytes(folder.Source));
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task SavePublishesRealBytesAndNotebookEnlargementUsesTheSameFrozenImage() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); var archive = new VocabularyArchive(folder.Notebook); await using var store = Store(archive);
        Assert.True(await store.SaveAsync(Capture())); var editor = Editor(store);
        var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            Assert.True(editor.ImportImageFile(folder.Source)); await editor.LastImport; var reference = Reference(editor);
            Click(Named<Button>(editor, "SaveButton")); await editor.LastOperation; Assert.False(editor.IsVisible);
            byte[] persisted = await archive.ReadImageAsync(reference); Assert.Equal(reference.Digest, Convert.ToHexStringLower(SHA256.HashData(persisted)));
            Assert.Single(Directory.GetFiles(folder.Media)); var entry = store.Entries[0]; notebook.Reveal(new(entry.Id, entry.Senses[0].Id)); notebook.Show();
            await notebook.LastRefresh; var view = Assert.Single(await Images(notebook)); Assert.NotNull(view.Bitmap);
            Assert.Equal(80, view.Bitmap.PixelWidth); Assert.Equal(40, view.Bitmap.PixelHeight);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                Window? preview = null;
                try
                {
                    preview = Assert.Single(Application.Current.Windows.Cast<Window>(), window => window.Title == "Image");
                    Assert.Same(notebook, preview.Owner); Assert.Same(view.Bitmap, Assert.Single(Children<Image>(preview)).Source);
                    completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally { preview?.Close(); }
            }, DispatcherPriority.Background);
            Click(Assert.Single(Children<Button>(view))); await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await editor.RetireAsync(); await notebook.RetireAsync(); }
    });

    [Fact]
    public Task SameHashImportsKeepStableBlocksAndPruneBytesAfterTheLastDelete() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture())); var editor = Editor(store);
        try
        {
            Assert.True(editor.ImportImageBytes(Input)); await editor.LastImport;
            Assert.True(editor.ImportImageBytes(Input)); await editor.LastImport;
            var blocks = editor.Draft.Senses[0].Content!; var ids = blocks.Select(block => block.Id).ToArray(); Assert.NotEqual(ids[0], ids[1]);
            Assert.Equal(1, editor.ImportedImageCount);
            Click(BlockButton(editor, 1, "Move up")); Assert.Equal(new[] { ids[1], ids[0] }, editor.Draft.Senses[0].Content!.Select(block => block.Id));
            Click(BlockButton(editor, 0, "Delete block")); Assert.Equal(1, editor.ImportedImageCount); Assert.Equal(ids[0], editor.Draft.Senses[0].Content![0].Id);
            Click(BlockButton(editor, 0, "Delete block")); Assert.Equal(0, editor.ImportedImageCount);
            Click(Named<Button>(editor, "SaveButton")); await editor.LastOperation; Assert.Empty(store.Entries[0].Senses[0].Content!);
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task ClosingDuringUncooperativePreparationCancelsTheWaitAndDropsTheLateResult() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); using var gate = new MediaGate(); gate.BlockPrepare = true;
        var archive = new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, images: gate); await using var store = Store(archive);
        Assert.True(await store.SaveAsync(Capture())); var editor = Editor(store);
        try
        {
            Assert.True(editor.ImportImageFile(folder.Source)); Assert.False(editor.ImportImageFile(folder.Source));
            Assert.False(Named<Button>(editor, "SaveButton").IsEnabled); Assert.False(Named<Button>(editor, "AddImageButton").IsEnabled);
            Assert.True(await Task.Run(() => gate.Entered.Wait(TimeSpan.FromSeconds(5))));
            Click(Named<Button>(editor, "CancelButton")); await editor.LastImport.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(editor.IsVisible); Assert.Null(editor.Draft.Senses[0].Content); Assert.Equal(0, editor.ImportedImageCount);
            gate.Release.Set(); await archive.LoadAsync(); await Dispatcher.Yield();
            Assert.Null(editor.Draft.Senses[0].Content); Assert.Equal(1UL, store.Snapshot.Revision); Assert.False(Directory.Exists(folder.Media));
        }
        finally { gate.Release.Set(); await editor.RetireAsync(); }
    });

    [Fact]
    public Task InvalidInputShowsOnlyTheFixedImportFailureAndLeavesTheDraftAvailable() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture())); var editor = Editor(store);
        try
        {
            Assert.True(editor.ImportImageBytes("private invalid source"u8.ToArray())); await editor.LastImport;
            Assert.Equal("Unable to add this image. Try a smaller image.", Named<TextBlock>(editor, "ImportFailure").Text);
            Assert.Null(editor.Draft.Senses[0].Content); Assert.Equal(0, editor.ImportedImageCount); Assert.Null(store.ErrorMessage);
            Assert.True(Named<Button>(editor, "SaveButton").IsEnabled); Assert.True(Named<Button>(editor, "AddImageButton").IsEnabled);
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task TheSixtyFourthBlockDisablesImageAndTextAdmissionUntilOneIsDeleted() => dispatcher.RunAsync(async () =>
    {
        var archive = new VocabularyArchive((string?)null); await using var store = Store(archive); Assert.True(await store.SaveAsync(Capture()));
        var entry = store.Entries[0]; var blocks = Enumerable.Range(0, 63).Select(_ => VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new()))).ToArray();
        store.Apply(await archive.CommitAsync(store.Snapshot.Document with { Entries = [entry with { Senses = [entry.Senses[0] with { Content = blocks }] }] }));
        var editor = Editor(store);
        try
        {
            Assert.True(editor.ImportImageBytes(Input)); await editor.LastImport; Assert.Equal(64, editor.Draft.Senses[0].Content!.Count);
            Assert.False(Named<Button>(editor, "AddTextButton").IsEnabled); Assert.False(Named<Button>(editor, "AddImageButton").IsEnabled); Assert.False(editor.ImportImageBytes(Input));
            Assert.True(Named<Button>(editor, "SaveButton").IsEnabled); Click(BlockButton(editor, 63, "Delete block"));
            Assert.Equal(blocks.Select(block => block.Id), editor.Draft.Senses[0].Content!.Select(block => block.Id)); Assert.Equal(0, editor.ImportedImageCount);
            Assert.True(Named<Button>(editor, "AddImageButton").IsEnabled);
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task RepeatedActualPngsCountEveryBlockAgainstTheSenseByteBudget() => dispatcher.RunAsync(async () =>
    {
        byte[] pixels = new byte[1024 * 1000 * 4]; new Random(848).NextBytes(pixels);
        var source = BitmapSource.Create(1024, 1000, 96, 96, PixelFormats.Bgra32, null, pixels, 1024 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var encoded = new MemoryStream(); encoder.Save(encoded); byte[] input = encoded.ToArray();
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture())); var editor = Editor(store);
        try
        {
            for (int count = 0; count < 4; count++) { Assert.True(editor.ImportImageBytes(input)); await editor.LastImport; Assert.Equal(count + 1, editor.Draft.Senses[0].Content!.Count); }
            Assert.InRange(Reference(editor).ByteCount, VocabularyContentPolicy.MaximumSenseImageBytes / 5 + 1L, VocabularyContentPolicy.MaximumImageBytes);
            var ids = editor.Draft.Senses[0].Content!.Select(block => block.Id).ToArray(); Assert.Equal(1, editor.ImportedImageCount);
            Assert.True(editor.ImportImageBytes(input)); await editor.LastImport;
            Assert.Equal(ids, editor.Draft.Senses[0].Content!.Select(block => block.Id)); Assert.Equal(1, editor.ImportedImageCount);
            Assert.Equal(Visibility.Visible, Named<TextBlock>(editor, "ImportFailure").Visibility); Assert.True(Named<Button>(editor, "SaveButton").IsEnabled);
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task FailedSaveKeepsDraftBytesForRetryAndRollbackLeavesNoNewFile() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); var archive = new VocabularyArchive(folder.Notebook); await using var store = Store(archive);
        Assert.True(await store.SaveAsync(Capture())); var editor = Editor(store);
        try
        {
            editor.ImportImageFile(folder.Source); await editor.LastImport; var reference = Reference(editor); var id = editor.Draft.Senses[0].Content![0].Id;
            using (var connection = VocabularySQLiteConnection.OpenNamed(folder.Notebook)) connection.Execute("CREATE TRIGGER fail_sense BEFORE UPDATE ON senses BEGIN SELECT RAISE(ABORT,'fixture'); END");
            Click(Named<Button>(editor, "SaveButton")); await editor.LastOperation;
            Assert.True(editor.IsVisible); Assert.Equal(1, editor.ImportedImageCount); Assert.False(await Available(archive, reference));
            Assert.True(Named<Button>(editor, "SaveButton").IsEnabled); Assert.True(Named<Button>(editor, "CancelButton").IsEnabled);
            using (var connection = VocabularySQLiteConnection.OpenNamed(folder.Notebook)) connection.Execute("DROP TRIGGER fail_sense");
            Click(Named<Button>(editor, "SaveButton")); await editor.LastOperation; Assert.False(editor.IsVisible);
            Assert.Equal(id, store.Entries[0].Senses[0].Content![0].Id); Assert.True(await Available(archive, reference));
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task ParentRetirementDrainsTheAdmittedImageSaveAfterTheDraftMapIsCleared() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); var clock = new ClockGate(); var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), clock, new WindowsVocabularyImageStorage(folder.Notebook));
        await using var store = Store(archive); Assert.True(await store.SaveAsync(Capture())); var editor = Editor(store);
        try
        {
            editor.ImportImageFile(folder.Source); await editor.LastImport; var reference = Reference(editor); clock.Block = true;
            Click(Named<Button>(editor, "SaveButton")); await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Named<Button>(editor, "CancelButton").IsEnabled); Assert.False(Named<StackPanel>(editor, "FormContent").IsEnabled); Assert.False(editor.ImportImageBytes(Input));
            editor.Close(); Assert.True(editor.IsVisible); var retiring = editor.RetireAsync(); Assert.False(retiring.IsCompleted); Assert.Equal(0, editor.ImportedImageCount);
            clock.Release.TrySetResult(); await retiring; Assert.False(editor.IsVisible); Assert.True(await Available(archive, reference));
            Assert.Single(store.Entries[0].Senses[0].Content!);
        }
        finally { clock.Release.TrySetResult(); await editor.RetireAsync(); }
    });

    [Fact]
    public Task ChangingNotebookSelectionCancelsAVisibleReadAndCannotPublishItsLateBitmap() => dispatcher.RunAsync(async () =>
    {
        using var gate = new MediaGate(); var archive = new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, images: gate); await using var store = Store(archive);
        Assert.True(await store.SaveAsync(Capture())); var bank = store.Entries[0]; var prepared = await archive.PrepareImageAsync(Input);
        Assert.True(await store.UpdateAsync(bank with { Senses = [bank.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference))] }] }, bank,
            new Dictionary<string, VocabularyPreparedImage> { [prepared.Reference.Filename] = prepared }));
        Assert.True(await store.SaveAsync(Capture("coffee"))); bank = store.Entries.Single(entry => entry.Expression == "bank"); var coffee = store.Entries.Single(entry => entry.Expression == "coffee");
        var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch()); gate.BlockRead = true;
        try
        {
            Assert.Equal(0, gate.Reads); notebook.Reveal(new(bank.Id, bank.Senses[0].Id)); Assert.Equal(0, gate.Reads);
            notebook.Show(); notebook.UpdateLayout(); Assert.True(await Task.Run(() => gate.Entered.Wait(TimeSpan.FromSeconds(5))));
            var old = Assert.Single(Children<VocabularyImageView>(notebook)); notebook.Reveal(new(coffee.Id, coffee.Senses[0].Id));
            await old.LastLoad.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Null(old.Bitmap);
            gate.Release.Set(); await archive.LoadAsync(); await notebook.LastRefresh; await Dispatcher.Yield();
            Assert.Empty(Children<VocabularyImageView>(notebook)); Assert.Equal(coffee.Senses[0].Id, notebook.Selection!.Value.SenseId); Assert.Null(old.Bitmap);
        }
        finally { gate.Release.Set(); await notebook.RetireAsync(); }
    });

    [Fact]
    public Task HiddenRowsCancelTheirWaitAndReloadWhenShownAgain() => dispatcher.RunAsync(async () =>
    {
        using var gate = new MediaGate(); var archive = new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, images: gate); await using var store = Store(archive);
        await store.SaveAsync(Capture()); var prepared = await archive.PrepareImageAsync(Input); var entry = store.Entries[0];
        await store.UpdateAsync(entry with { Senses = [entry.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference))] }] }, entry,
            new Dictionary<string, VocabularyPreparedImage> { [prepared.Reference.Filename] = prepared });
        var image = new VocabularyImageView(store, prepared.Reference); var window = new Window { Content = image, Width = 200, Height = 150 }; gate.BlockRead = true;
        try
        {
            window.Show(); window.UpdateLayout(); Assert.True(await Task.Run(() => gate.Entered.Wait(TimeSpan.FromSeconds(5))));
            window.Hide(); await image.LastLoad.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Null(image.Bitmap);
            gate.BlockRead = false; gate.Release.Set(); await archive.LoadAsync(); window.Show(); Assert.Single(await Images(window));
            Assert.NotNull(image.Bitmap); Assert.Equal(2, gate.Reads);
        }
        finally { gate.Release.Set(); image.Dispose(); window.Close(); }
    });

    [Fact]
    public Task MissingMediaShowsUnavailableWithoutChangingSharedStoreError() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await store.LoadAsync();
        var view = new VocabularyImageView(store, new(new string('a', 64), 1, 1, 1)); var window = new Window { Content = view, Width = 200, Height = 150 };
        try
        {
            window.Show(); await Images(window); Assert.Null(view.Bitmap);
            Assert.Contains(Children<TextBlock>(view), text => text.Text == "Image unavailable" && text.IsVisible); Assert.Null(store.ErrorMessage);
        }
        finally { view.Dispose(); window.Close(); }
    });

    [Theory]
    [InlineData("directory")]
    [InlineData("symlink")]
    [InlineData("oversized")]
    public Task FilePreparationRejectsUnsafeOrOversizedSourcesWithoutOpeningTheNotebook(string mode) => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); string source = Path.Combine(folder.Path, "invalid");
        if (mode == "directory") Directory.CreateDirectory(source);
        else if (mode == "symlink") File.CreateSymbolicLink(source, folder.Source);
        else File.WriteAllBytes(source, new byte[24 * 1024 * 1024 + 1]);
        await using var archive = new VocabularyArchive(folder.Notebook);
        Assert.Equal(VocabularyFailure.InvalidContent, (await Assert.ThrowsAsync<VocabularyException>(() => archive.PrepareImageFileAsync(source))).Failure);
        Assert.False(Directory.Exists(Path.GetDirectoryName(folder.Notebook))); Assert.Equal(Input, File.ReadAllBytes(folder.Source));
    });

    [Fact]
    public Task ExplicitSourceMayHaveSharedReadPermissionsWhileTheNormalizedDraftRemainsDetached() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); var source = new FileInfo(folder.Source); var security = source.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow)); source.SetAccessControl(security);
        await using var archive = new VocabularyArchive(folder.Notebook); var draft = await archive.PrepareImageFileAsync(folder.Source);
        byte[] data = draft.CopyBytes(); Assert.Equal(draft.Reference.Digest, Convert.ToHexStringLower(SHA256.HashData(data)));
        var bitmap = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(data, draft.Reference)); Assert.True(bitmap.IsFrozen); Assert.Equal(80, bitmap.PixelWidth);
        File.WriteAllBytes(folder.Source, [7]); Assert.Equal(draft.Reference.Digest, Convert.ToHexStringLower(SHA256.HashData(draft.CopyBytes())));
        Assert.False(Directory.Exists(Path.GetDirectoryName(folder.Notebook)));
    });

    private sealed class MediaGate : IVocabularyImageStorage, IDisposable
    {
        private readonly WindowsVocabularyImageStorage _real = new(null);
        internal bool BlockPrepare, BlockRead;
        internal int Reads;
        internal ManualResetEventSlim Entered { get; } = new();
        internal ManualResetEventSlim Release { get; } = new();
        private VocabularyPreparedImage Prepared(VocabularyPreparedImage value) { if (BlockPrepare) Wait(); return value; }
        private void Wait() { Entered.Set(); if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); }
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> data, CancellationToken cancellationToken) => Prepared(_real.Prepare(data, cancellationToken));
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken cancellationToken) => Prepared(_real.PrepareFile(path, cancellationToken));
        public byte[] Read(VocabularyImageReference image) { Interlocked.Increment(ref Reads); byte[] data = _real.Read(image); if (BlockRead) Wait(); return data; }
        public bool Write(ReadOnlySpan<byte> data, VocabularyImageReference image) => _real.Write(data, image);
        public void Remove(VocabularyImageReference image) => _real.Remove(image);
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
    private sealed class ClockGate : TimeProvider
    {
        internal bool Block;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() { if (Block) { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); } return DateTimeOffset.UnixEpoch; }
    }
    private sealed class Folder : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lingrove-image-ui-" + Guid.NewGuid().ToString("N"));
        internal string Notebook => System.IO.Path.Combine(Path, "notebook", "notebook.sqlite");
        internal string Media => Notebook + ".media";
        internal string Source => System.IO.Path.Combine(Path, "selected.png");
        internal Folder() { VocabularyWindowsFile.CreateParentDirectory(Path); File.WriteAllBytes(Source, Input); }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
