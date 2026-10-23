using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Actual WPF paste routing and real Store/WIC/SQLite, borrowing an explicit test clipboard offer.
/// The fixture represents only a detached transfer response; no test reads/changes the clipboard.
/// Native producer/keyboard/IME compatibility remains a desktop gate.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyImagePasteTests(WpfDispatcher dispatcher)
{
    private static byte[] Input => Convert.FromBase64String(JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-image-storage-v1.json"))).RootElement.GetProperty("input").GetString()!);
    private static VocabularyStore Store(VocabularyArchive archive) => new(archive, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    private static VocabularyCapture Capture() => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, "meaning");
    private static VocabularyEntryEditorWindow Editor(VocabularyStore store, Clipboard fixture)
    { var entry = store.Entries[0]; var editor = new VocabularyEntryEditorWindow(store, entry, entry.Senses[0].Id, fixture); editor.Show(); editor.UpdateLayout(); return editor; }
    private static T Named<T>(Window editor, string name) where T : FrameworkElement => Assert.IsType<T>(editor.FindName(name));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public Task BorrowedByteAdmissionDetachesBeforeTheCallerMayMutateItsBuffer() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture()); var editor = Editor(store, new Clipboard(Input));
        try
        {
            byte[] input = Input; Assert.True(editor.ImportImageBytes(input)); Array.Fill(input, (byte)0); await editor.LastImport;
            Assert.Single(editor.Draft.Senses[0].Content!); Assert.Equal(1, editor.ImportedImageCount);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(editor, "ImportFailure").Visibility);
        }
        finally { await editor.RetireAsync(); }
    });

    [Fact]
    public Task PasteButtonKeepsARealPreparedDraftUntilAcknowledgedSave() => dispatcher.RunAsync(async () =>
    {
        using var folder = new Folder(); await using var archive = new VocabularyArchive(folder.Notebook); await using var store = Store(archive);
        await store.SaveAsync(Capture()); var clipboard = new Clipboard(Input); var editor = Editor(store, clipboard); int uiThread = Environment.CurrentManagedThreadId;
        try
        {
            Click(Named<Button>(editor, "PasteImageButton")); await editor.LastImport;
            Assert.Equal(23u, clipboard.ReadSequence); Assert.NotEqual(uiThread, clipboard.ReadThread); Assert.Single(editor.Draft.Senses[0].Content!); Assert.Equal(1, editor.ImportedImageCount);
            var image = Assert.IsType<VocabularyBlockContent.Image>(editor.Draft.Senses[0].Content![0].Content); Assert.Equal("", image.Caption);
            Assert.Null(store.Entries[0].Senses[0].Content); Assert.False(Directory.Exists(folder.Notebook + ".media"));
            Click(Named<Button>(editor, "SaveButton")); await editor.LastOperation; Assert.False(editor.IsVisible);
            Assert.Single(store.Entries[0].Senses[0].Content!); Assert.Equal(image.Reference.ByteCount, (await archive.ReadImageAsync(image.Reference)).LongLength);
        }
        finally { await editor.RetireAsync(); }
    });
    [Fact]
    public Task ExplanationPasteCommandIsConsumedBeforeTheTextBoxCanReadForeignData() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture()); var clipboard = new Clipboard(Input); var editor = Editor(store, clipboard);
        try
        {
            Click(Named<Button>(editor, "AddTextButton")); var blocks = Named<StackPanel>(editor, "BlocksContent");
            var text = Find<TextBox>(blocks).Single(); text.Text = "existing explanation"; text.Focus();
            int fallback = 0; GuardDefaultPaste(text, () => ++fallback);
            Assert.True(ApplicationCommands.Paste.CanExecute(null, text)); Assert.Equal(0, clipboard.Reads);
            ApplicationCommands.Paste.Execute(null, text); await editor.LastImport;
            Assert.Equal(0, fallback); Assert.Equal(1, clipboard.Reads); Assert.Equal(2, editor.Draft.Senses[0].Content!.Count);
            Assert.Equal("existing explanation", Assert.IsType<VocabularyBlockContent.Text>(editor.Draft.Senses[0].Content![0].Content).Value.Text);
        }
        finally { await editor.RetireAsync(); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TextOnlyOffersAndFieldsOutsideTheExplanationKeepTheirNormalCommandRoute(bool outside) => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture());
        var clipboard = new Clipboard(Input) { Offer = new(23, outside) }; var editor = Editor(store, clipboard);
        try
        {
            Click(Named<Button>(editor, "AddTextButton"));
            var text = outside ? Named<TextBox>(editor, "ExpressionText") : Find<TextBox>(Named<StackPanel>(editor, "BlocksContent")).Single();
            int fallback = 0; GuardDefaultPaste(text, () => ++fallback); var original = editor.LastImport;
            Assert.True(ApplicationCommands.Paste.CanExecute(null, text)); ApplicationCommands.Paste.Execute(null, text);
            Assert.Equal(1, fallback); Assert.Equal(0, clipboard.Reads); Assert.Same(original, editor.LastImport);
            if (outside) Assert.Equal(0, clipboard.Captures);
        }
        finally { await editor.RetireAsync(); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NoOfferOrNoDetachedDataIsQuietAndCannotCreateABlock(bool offered) => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture()); var clipboard = new Clipboard(null) { Offer = new(23, offered) }; var editor = Editor(store, clipboard);
        try
        {
            Assert.Equal(offered, editor.PasteImage()); await editor.LastImport;
            Assert.Equal(offered ? 1 : 0, clipboard.Reads); Assert.Null(editor.Draft.Senses[0].Content); Assert.Equal(0, editor.ImportedImageCount);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(editor, "ImportFailure").Visibility); Assert.True(Named<Button>(editor, "SaveButton").IsEnabled);
        }
        finally { await editor.RetireAsync(); }
    });
    [Theory]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("transfer-failure")]
    public Task ASelectedInvalidRepresentationShowsOneFixedFailureWithoutAnotherRead(string mode) => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture());
        var clipboard = new Clipboard(mode == "empty" ? [] : [1, 2, 3]) { Failure = mode == "transfer-failure" ? new IOException("private provider detail") : null }; var editor = Editor(store, clipboard);
        try
        {
            editor.PasteImage(); await editor.LastImport; Assert.Equal(1, clipboard.Reads); Assert.Null(editor.Draft.Senses[0].Content); Assert.Equal(0, editor.ImportedImageCount);
            Assert.Equal("Unable to add this image. Try a smaller image.", Named<TextBlock>(editor, "ImportFailure").Text); Assert.Null(store.ErrorMessage);
            Assert.True(Named<Button>(editor, "PasteImageButton").IsEnabled); Assert.True(Named<Button>(editor, "SaveButton").IsEnabled);
        }
        finally { await editor.RetireAsync(); }
    });
    [Fact]
    public Task PendingReadBlocksSaveAndAddButCancelRetiresTheWaitBeforeALateProviderReply() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await store.SaveAsync(Capture()); var clipboard = new Clipboard(Input) { Block = true }; var editor = Editor(store, clipboard);
        try
        {
            editor.PasteImage(); await clipboard.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(Named<Button>(editor, "SaveButton").IsEnabled); Assert.False(Named<Button>(editor, "PasteImageButton").IsEnabled); Assert.False(Named<Button>(editor, "AddImageButton").IsEnabled);
            Assert.False(editor.PasteImage()); Assert.Equal(1, clipboard.Reads); Click(Named<Button>(editor, "CancelButton"));
            await editor.RetireAsync().WaitAsync(TimeSpan.FromSeconds(2)); Assert.True(clipboard.Token.IsCancellationRequested);
            clipboard.Release.TrySetResult(); await clipboard.Finished.Task; await Dispatcher.Yield();
            Assert.Null(editor.Draft.Senses[0].Content); Assert.Equal(0, editor.ImportedImageCount); Assert.Equal(1UL, store.Snapshot.Revision);
        }
        finally { clipboard.Release.TrySetResult(); await editor.RetireAsync(); }
    });
    [Fact]
    public Task FullContentRejectsPasteWithoutReadingAndDeletingABlockReopensAdmission() => dispatcher.RunAsync(async () =>
    {
        var archive = new VocabularyArchive((string?)null); await using var store = Store(archive); await store.SaveAsync(Capture()); var entry = store.Entries[0];
        var blocks = Enumerable.Range(0, 64).Select(_ => VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new()))).ToArray();
        store.Apply(await archive.CommitAsync(store.Snapshot.Document with { Entries = [entry with { Senses = [entry.Senses[0] with { Content = blocks }] }] }));
        var clipboard = new Clipboard(Input); var editor = Editor(store, clipboard);
        try
        {
            Assert.False(Named<Button>(editor, "PasteImageButton").IsEnabled); Assert.False(editor.PasteImage()); Assert.Equal(0, clipboard.Reads);
            Click(Find<Button>(Named<StackPanel>(editor, "BlocksContent").Children[0]).Single(button => button.Content as string == "Delete block"));
            Assert.True(Named<Button>(editor, "PasteImageButton").IsEnabled); Assert.True(editor.PasteImage()); await editor.LastImport;
            Assert.Equal(64, editor.Draft.Senses[0].Content!.Count); Assert.Equal(blocks.Skip(1).Select(block => block.Id), editor.Draft.Senses[0].Content!.Take(63).Select(block => block.Id));
            Assert.Equal(1, clipboard.Reads); Assert.Equal(1, editor.ImportedImageCount);
        }
        finally { await editor.RetireAsync(); }
    });
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T value) yield return value;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
    private static void GuardDefaultPaste(UIElement target, Action reached)
    {
        // Observe the normal route after the product's section preview and stop there: a routing
        // regression must fail the assertion, never accidentally touch the runner clipboard.
        CommandManager.AddPreviewCanExecuteHandler(target, (_, args) =>
        { if (args.Command == ApplicationCommands.Paste) { args.CanExecute = true; args.Handled = true; } });
        CommandManager.AddPreviewExecutedHandler(target, (_, args) =>
        { if (args.Command == ApplicationCommands.Paste) { reached(); args.Handled = true; } });
    }
    private sealed class Clipboard(byte[]? bytes) : IVocabularyClipboardImageTransfer
    {
        internal VocabularyClipboardImageOffer Offer = new(23, true);
        internal bool Block;
        internal Exception? Failure;
        internal int Reads, ReadThread, Captures;
        internal uint ReadSequence;
        internal CancellationToken Token;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public VocabularyClipboardImageOffer Capture() { ++Captures; return Offer; }
        public async Task<byte[]?> ReadAsync(uint expectedSequence, CancellationToken cancellationToken = default)
        {
            ++Reads; ReadThread = Environment.CurrentManagedThreadId; ReadSequence = expectedSequence; Token = cancellationToken; Entered.TrySetResult();
            try { if (Block) await Release.Task; if (Failure is not null) throw Failure; return bytes; }
            finally { Finished.TrySetResult(); }
        }
    }
    private sealed class Folder : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "lingrove-paste-ui-" + Guid.NewGuid().ToString("N"));
        internal string Notebook => Path.Combine(_path, "notebook.sqlite");
        internal Folder() => VocabularyWindowsFile.CreateParentDirectory(_path);
        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
