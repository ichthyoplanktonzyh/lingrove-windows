using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.App.Settings;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyExportStoreTests(WpfDispatcher dispatcher)
{
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root) { yield return root; for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Nodes(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class HeldPicker : IVocabularyExportDestinationPicker
    {
        internal readonly TaskCompletionSource<string?> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? OnCancel;
        public Task<string?> ChooseAsync(Window owner, VocabularyExportPackage package, CancellationToken token)
        { token.Register(() => { OnCancel?.Invoke(); throw new InvalidOperationException("injected cancellation"); }); return Reply.Task; }
    }
    private sealed class PathPicker(string path) : IVocabularyExportDestinationPicker
    { public Task<string?> ChooseAsync(Window owner, VocabularyExportPackage package, CancellationToken token) => Task.FromResult<string?>(path); }
    [Fact]
    public Task CanonicalWindowSavesExactlyItsPreviewPackage() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-export-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var id = new VocabularySelection(entry.Id, entry.Senses[0].Id); string path = Path.Combine(root, "export.txt");
            var window = new VocabularyExportWindow(store, new VocabularyExportRequest([id], [id], [], VocabularyExportScope.All), new PathPicker(path)); window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastPreparation; var expected = window.Session.Package!.CopyData();
            Nodes(window).OfType<Button>().Single(x => x.Name == "ExportButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation;
            Assert.Equal(expected, await File.ReadAllBytesAsync(path)); await window.RetireAsync();
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public Task RetirementJoinsPickerEvenWhenCancellationThrowsAndReenters() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-export-close-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var id = new VocabularySelection(entry.Id, entry.Senses[0].Id); var picker = new HeldPicker();
            var window = new VocabularyExportWindow(store, new VocabularyExportRequest([id], [id], [], VocabularyExportScope.All), picker); window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastPreparation;
            Nodes(window).OfType<Button>().Single(x => x.Name == "ExportButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Task? reentered = null; picker.OnCancel = () => reentered = window.RetireAsync();
            var retiring = window.RetireAsync(); Assert.Same(retiring, reentered); Assert.False(retiring.IsCompleted);
            picker.Reply.TrySetResult(null); await Assert.ThrowsAsync<AggregateException>(() => retiring);
            Assert.False(window.IsVisible);
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public Task NotebookExportOpensCanonicalPreviewAndDrains() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-export-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await using var store = FrozenVocabularyStoreFactory.Create(Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalFileName), false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]);
            var settings = new AppSettings(); var controller = new FrozenVocabularyWindowController(store, Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new VocabularyNotebookListPreferences(() => settings, () => { }));
            controller.Show(); var notebook = controller.Window!; if (notebook.LastOperation is { } load) await load; notebook.UpdateLayout();
            VocabularyExportWindow? child = null; Exception? error = null;
            _ = notebook.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    child = Assert.Single(notebook.OwnedWindows.OfType<VocabularyExportWindow>());
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await child.LastPreparation;
                    Assert.Contains("bank", child.Session.Package!.Preview); Assert.Equal(1, child.Session.Package.SenseCount);
                    VocabularyThemeRender.Capture(child, "canonical-export-window", 700, 710);
                }
                catch (Exception e) { error = e; }
                finally { if (child is not null) await child.RetireAsync(); }
            }, DispatcherPriority.ApplicationIdle);
            var button = Nodes(notebook).OfType<Button>().Single(x => x.Name == "canonicalExport"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (notebook.LastOperation is { } operation) await operation; Assert.Null(error); await controller.DisposeAsync();
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public Task CapturesExactSnapshotAndMediaAndProtectsDestination() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "canonical-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root, "data", FrozenVocabularyStoreFactory.CanonicalFileName), destination = Path.Combine(root, "out.zip");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "vocabulary-media-transactions-v1.json")));
            byte[] bytes = Convert.FromBase64String(json.RootElement.GetProperty("input").GetString()!);
            var prepared = new VocabularyPreparedImage(new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 80, 40, bytes.Length), bytes);
            await using var store = FrozenVocabularyStoreFactory.Create(db, false);
            await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Meaning: "money")]); var entry = (await store.ReadAsync()).Document.CollectedEntries[0];
            var withImage = entry with { Senses = [entry.Senses[0] with { Content = [VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference, "caption"))] }] };
            await store.UpdateEntryAsync(withImage, entry, new Dictionary<string, VocabularyPreparedImage> { [prepared.Reference.Filename] = prepared });
            var id = new VocabularySelection(entry.Id, entry.Senses[0].Id);
            var package = await store.PrepareExportAsync([id], new VocabularyExportConfiguration { Format = VocabularyExportFormat.Json, Images = true });
            await store.UpdateEntryAsync(entry, withImage);
            Assert.Equal(bytes, package.CopyImages()[prepared.Reference.Filename]);
            Assert.Contains("caption", Encoding.UTF8.GetString(package.CopyData()));
            await store.WriteExportAsync(package, destination); Assert.True(File.Exists(destination));
            using var archive = System.IO.Compression.ZipFile.OpenRead(destination); Assert.NotNull(archive.GetEntry("images/" + prepared.Reference.Filename));
            await Assert.ThrowsAsync<VocabularyException>(() => store.WriteExportAsync(package, db));
            await Assert.ThrowsAsync<VocabularyExportException>(() => store.PrepareExportAsync([new(entry.Id, Guid.NewGuid())], new()));
        }
        finally { Directory.Delete(root, true); }
    });
}
