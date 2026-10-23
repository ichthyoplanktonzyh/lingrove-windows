using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Compiled notebook/confirmation windows and actual Store/file/media/SQLite effects.
/// File-panel replies are controlled human input; no native Shell modal picker is shown. Owned
/// WPF confirmations are exercised by routed buttons, including Cancel as the default action.</summary>
[Collection("WPF views")]
public sealed class VocabularyNotebookBackupTests(WpfDispatcher dispatcher)
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-restore-v1.json"))).RootElement.Clone();
    private static byte[] Package => Convert.FromBase64String(Corpus.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "all")
        .GetProperty("expected").GetProperty("package").GetString()!);
    private static VocabularyDocument Restored => VocabularyBackupCodec.Decode(Package).Document;
    private static async Task Refresh(VocabularyNotebookWindow window)
    { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; }
    private static async Task Until(Func<bool> ready)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!ready()) { Assert.True(DateTime.UtcNow < limit, "Expected owned UI state did not arrive."); await Task.Delay(10); }
    }
    private static async Task<VocabularyStore> Store(string notebook)
    { var store = new VocabularyStore(new(notebook), SynchronizationContext.Current!); await store.LoadAsync(); return store; }
    [Fact]
    public Task LoadedEmptyNotebookCanBackupAndRestoreWhileColdNotebookCannot() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = new VocabularyStore(new(folder.Notebook), SynchronizationContext.Current!);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch()) { ChooseBackupFile = (_, _) => Task.FromResult<string?>(null) };
        try
        {
            window.Show(); await Refresh(window);
            Assert.False(Find<Button>(window, "BackupButton").IsEnabled); Assert.False(Find<Button>(window, "RestoreNotebookButton").IsEnabled);
            Click(Find<Button>(window, "BackupButton")); Assert.True(window.LastBackupOperation.IsCompleted);
            await store.LoadAsync(); await Refresh(window);
            Assert.Empty(store.Entries); Assert.True(Find<Button>(window, "BackupButton").IsEnabled); Assert.True(Find<Button>(window, "RestoreNotebookButton").IsEnabled);
            Assert.False(Find<Button>(window, "ExportButton").IsEnabled);
        }
        finally { await CloseNotebook(window); }
    });
    [Fact]
    public Task BackupPickerCancellationDoesNotWriteAndRetryUsesTheWholeCurrentNotebook() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook);
        int choices = 0, confirmations = 0; var offered = new List<bool>(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch())
        { ChooseBackupFile = (restore, _) => { offered.Add(restore); return Task.FromResult<string?>(++choices == 1 ? null : folder.Export); },
          ConfirmRestore = (_, _) => { confirmations++; return Task.FromResult(true); } };
        try
        {
            window.Show(); await Refresh(window); var before = store.Snapshot; Guid media = store.MediaRevision;
            Click(Find<Button>(window, "BackupButton")); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(File.Exists(folder.Export)); Assert.Equal(before.Revision, store.Snapshot.Revision); Assert.Equal(media, store.MediaRevision);
            File.WriteAllBytes(folder.Export, Package); Assert.True(await store.RestoreAsync(folder.Export)); File.Delete(folder.Export);
            await Refresh(window); Find<TextBox>(window, "SearchText").Text = "unmatched filter"; await Refresh(window);
            Click(Find<Button>(window, "BackupButton")); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Restored, VocabularyBackupCodec.Decode(File.ReadAllBytes(folder.Export)).Document); Assert.Equal(0, confirmations); Assert.Equal(2, choices); Assert.Equal(new[] { false, false }, offered);
        }
        finally { await CloseNotebook(window); }
    });
    [Fact]
    public Task RestorePickerCancellationNeverAsksForConfirmationOrChangesTheNotebook() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook); int confirmations = 0; bool? offered = null;
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch())
        { ChooseBackupFile = (restore, _) => { offered = restore; return Task.FromResult<string?>(null); },
          ConfirmRestore = (_, _) => { confirmations++; return Task.FromResult(true); } };
        try
        {
            window.Show(); await Refresh(window); Guid media = store.MediaRevision; ulong revision = store.Snapshot.Revision;
            Click(Find<Button>(window, "RestoreNotebookButton")); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(offered is true); Assert.Equal(0, confirmations); Assert.Equal(revision, store.Snapshot.Revision); Assert.Equal(media, store.MediaRevision); Assert.Empty(store.Entries);
        }
        finally { await CloseNotebook(window); }
    });
    [Fact]
    public Task RestoreRequiresTheExactChosenPathAndExplicitConfirmationBeforeRetry() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook); File.WriteAllBytes(folder.Export, Package);
        int confirmations = 0; var paths = new List<string>(); var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch())
        { ChooseBackupFile = (_, _) => Task.FromResult<string?>(folder.Export),
          ConfirmRestore = (path, _) => { paths.Add(path); return Task.FromResult(++confirmations == 2); } };
        try
        {
            window.Show(); await Refresh(window); Guid media = store.MediaRevision; ulong revision = store.Snapshot.Revision;
            Click(Find<Button>(window, "RestoreNotebookButton")); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(revision, store.Snapshot.Revision); Assert.Equal(media, store.MediaRevision); Assert.Empty(store.Entries);
            Click(Find<Button>(window, "RestoreNotebookButton")); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Restored, store.Snapshot.Document); Assert.NotEqual(media, store.MediaRevision); Assert.Equal(Package, File.ReadAllBytes(folder.Export));
            Assert.Equal(new[] { folder.Export, folder.Export }, paths);
        }
        finally { await CloseNotebook(window); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ActualOwnedReplacementConfirmationDefaultsToCancel(bool restore) => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook); File.WriteAllBytes(folder.Export, Package);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch()) { ChooseBackupFile = (_, _) => Task.FromResult<string?>(folder.Export) };
        try
        {
            window.Show(); await Refresh(window); Click(Find<Button>(window, "RestoreNotebookButton"));
            await Until(() => window.OwnedWindows.OfType<VocabularyRestoreConfirmationWindow>().Any(value => value.IsVisible));
            var child = Assert.Single(window.OwnedWindows.OfType<VocabularyRestoreConfirmationWindow>());
            Assert.Same(window, child.Owner); Assert.True(Find<Button>(child, "CancelButton").IsDefault); Assert.True(Find<Button>(child, "CancelButton").IsCancel);
            Assert.False(Find<Button>(child, "RestoreButton").IsDefault);
            Click(Find<Button>(child, restore ? "RestoreButton" : "CancelButton"));
            if (restore) window.Close(); // Acceptance must already belong to Store before this close.
            await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(restore ? Restored : new VocabularyDocument(), store.Snapshot.Document); Assert.False(child.IsVisible);
        }
        finally { await CloseNotebook(window); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AcceptedSynchronousReplySurvivesTheNextOwnerHideMessage(bool restore) => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook);
        if (restore) File.WriteAllBytes(folder.Export, Package);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        window.ChooseBackupFile = (_, _) =>
        {
            if (!restore) _ = window.Dispatcher.BeginInvoke(new Action(window.Hide), DispatcherPriority.Send);
            return Task.FromResult<string?>(folder.Export);
        };
        window.ConfirmRestore = (_, _) =>
        {
            _ = window.Dispatcher.BeginInvoke(new Action(window.Hide), DispatcherPriority.Send); return Task.FromResult(true);
        };
        try
        {
            window.Show(); await Refresh(window); Click(Find<Button>(window, restore ? "RestoreNotebookButton" : "BackupButton"));
            await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(window.IsVisible);
            if (restore) Assert.Equal(Restored, store.Snapshot.Document);
            else Assert.Equal(new VocabularyDocument(), VocabularyBackupCodec.Decode(File.ReadAllBytes(folder.Export)).Document);
        }
        finally { await CloseNotebook(window); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HiddenNotebookRejectsLatePickerReplyEvenAfterReopen(bool restore) => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook);
        if (restore) File.WriteAllBytes(folder.Export, Package);
        var choice = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken offered = default; int calls = 0, confirmations = 0;
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch())
        { ChooseBackupFile = (_, token) => { calls++; offered = token; entered.TrySetResult(); return choice.Task; },
          ConfirmRestore = (_, _) => { confirmations++; return Task.FromResult(true); } };
        try
        {
            window.Show(); await Refresh(window); string button = restore ? "RestoreNotebookButton" : "BackupButton";
            Click(Find<Button>(window, button)); Task operation = window.LastBackupOperation; await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Click(Find<Button>(window, button)); Assert.Equal(1, calls);
            window.Close(); Assert.True(offered.IsCancellationRequested); window.Reveal(null); window.Show();
            Assert.False(Find<Button>(window, button).IsEnabled); choice.TrySetResult(folder.Export); await operation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, confirmations); Assert.Empty(store.Entries); Assert.True(Find<Button>(window, button).IsEnabled);
            if (!restore) Assert.False(File.Exists(folder.Export)); else Assert.Equal(Package, File.ReadAllBytes(folder.Export));
        }
        finally { choice.TrySetResult(null); await CloseNotebook(window); }
    });
    [Fact]
    public Task HiddenNotebookRejectsLatePositiveConfirmation() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook); File.WriteAllBytes(folder.Export, Package);
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch())
        { ChooseBackupFile = (_, _) => Task.FromResult<string?>(folder.Export), ConfirmRestore = (_, _) => { entered.TrySetResult(); return reply.Task; } };
        try
        {
            window.Show(); await Refresh(window); Guid media = store.MediaRevision;
            Click(Find<Button>(window, "RestoreNotebookButton")); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); window.Close(); reply.TrySetResult(true); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(store.Entries); Assert.Equal(media, store.MediaRevision);
        }
        finally { reply.TrySetResult(false); await CloseNotebook(window); }
    });
    [Fact]
    public Task RetirementClosesAndDrainsTheActualOwnedConfirmation() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook); File.WriteAllBytes(folder.Export, Package);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch()) { ChooseBackupFile = (_, _) => Task.FromResult<string?>(folder.Export) };
        try
        {
            window.Show(); await Refresh(window); Click(Find<Button>(window, "RestoreNotebookButton"));
            await Until(() => window.OwnedWindows.OfType<VocabularyRestoreConfirmationWindow>().Any(value => value.IsVisible));
            var child = Assert.Single(window.OwnedWindows.OfType<VocabularyRestoreConfirmationWindow>());
            await window.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(window.LastBackupOperation.IsCompleted); Assert.False(child.IsVisible); Assert.Empty(store.Entries);
        }
        finally { await CloseNotebook(window); }
    });
    [Fact]
    public Task RetiringWindowAndStoreDrainTheAlreadyConfirmedNativeRestore() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); var images = new GatedImages(new(folder.Notebook));
        var archive = new VocabularyArchive(() => VocabularySQLiteDatabase.OpenFile(folder.Notebook), images: images);
        var store = new VocabularyStore(archive, SynchronizationContext.Current!); await store.LoadAsync(); File.WriteAllBytes(folder.Export, Package);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch())
        { ChooseBackupFile = (_, _) => Task.FromResult<string?>(folder.Export), ConfirmRestore = (_, _) => Task.FromResult(true) };
        try
        {
            window.Show(); await Refresh(window); Click(Find<Button>(window, "RestoreNotebookButton")); await images.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(Find<Button>(window, "BackupButton").IsEnabled);
            Task retiring = window.RetireAsync(), stopping = store.DisposeAsync().AsTask(); Assert.False(retiring.IsCompleted); Assert.False(stopping.IsCompleted);
            images.Release.TrySetResult(); await Task.WhenAll(retiring, stopping).WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(Restored, store.Snapshot.Document);
            await using var reopened = new VocabularyArchive(folder.Notebook); Assert.Equal(Restored, (await reopened.LoadAsync()).Document);
            var image = Assert.Single(VocabularyArchiveMediaPolicy.References(Restored).Distinct());
            Assert.Equal(VocabularyBackupCodec.Decode(Package).CopyImages()[image.Filename], await reopened.ReadImageAsync(image));
        }
        finally { images.Release.TrySetResult(); await CloseNotebook(window); await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
    });
    [Fact]
    public Task PanelStartupFailureIsLocalSanitizedAndCanRetry() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(folder.Notebook);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch())
        { ChooseBackupFile = (_, _) => throw new InvalidOperationException("private filesystem detail") };
        try
        {
            window.Show(); await Refresh(window); Click(Find<Button>(window, "BackupButton")); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(store.ErrorMessage); Assert.Equal("The backup file panel could not be opened.", Find<TextBlock>(window, "StorageError").Text);
            window.ChooseBackupFile = (_, _) => Task.FromResult<string?>(folder.Export);
            Click(Find<Button>(window, "BackupButton")); await window.LastBackupOperation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(File.Exists(folder.Export)); Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "StorageError").Visibility);
        }
        finally { await CloseNotebook(window); }
    });
    private static async Task CloseNotebook(VocabularyNotebookWindow window)
    {
        // Defensive test teardown closes the child even if the production retirement path
        // regresses. This lets a failed milestone unwind the shared STA's nested modal frame.
        foreach (var child in window.OwnedWindows.OfType<VocabularyRestoreConfirmationWindow>().ToArray()) child.Close();
        await window.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
    private sealed class GatedImages(WindowsVocabularyImageStorage actual) : IVocabularyImageStorage
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public VocabularyPreparedImage Prepare(ReadOnlySpan<byte> bytes, CancellationToken token) => actual.Prepare(bytes, token);
        public VocabularyPreparedImage PrepareFile(string path, CancellationToken token) => actual.PrepareFile(path, token);
        public bool Write(ReadOnlySpan<byte> bytes, VocabularyImageReference image)
        { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); return actual.Write(bytes, image); }
        public byte[] Read(VocabularyImageReference image) => actual.Read(image);
        public void Remove(VocabularyImageReference image) => actual.Remove(image);
    }
}
