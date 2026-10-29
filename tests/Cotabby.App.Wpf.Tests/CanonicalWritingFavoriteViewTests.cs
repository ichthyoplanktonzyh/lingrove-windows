using System.IO;
using CanonicalWritingPolicy = Cotabby.Core.Vocabulary.Frozen.CanonicalWritingPolicy;
using Frozen = Cotabby.Core.Vocabulary.Frozen;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.UI.Learning;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Compiled writing cards and the real isolated canonical Store, without AppHost/editor/model.</summary>
[Collection("WPF views")]
public sealed class CanonicalWritingFavoriteViewTests(WpfDispatcher dispatcher)
{
    private static VocabularyCapture Capture() => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
        Example: new(Guid.NewGuid(), "The bank.", "银行", [new(4, 4)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch));
    private static async Task Settle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static void ClickMenu(MenuItem menu) => menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    [Fact]
    public Task RealCanonicalFavoriteAndImportShareAcknowledgedState() => dispatcher.RunAsync(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "lingrove-writing-view-" + Guid.NewGuid().ToString("N"));
        VocabularyWindowsFile.CreateParentDirectory(root); string path = Path.Combine(root, FrozenVocabularyStoreFactory.CanonicalFileName);
        try
        {
            await using var backend = await FrozenVocabularyStoreFactory.OpenAsync(path);
            await using var viewStore = new CanonicalWritingViewStore(backend, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var capture = Capture(); var window = new LearningPreviewWindow();
            try
            {
                window.ShowState(Preview, Anchor, false); window.BindVocabulary(viewStore, capture, Preview.RequestId, () => true);
                var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
                Assert.Equal("Not saved", AutomationProperties.GetItemStatus(button)); Click(button); await button.LastOperation; await Settle();
                Assert.True(viewStore.Contains(capture)); Assert.Equal("Saved", AutomationProperties.GetItemStatus(button));
                AssertNonactivating(window); AssertPaints(window);
                var snapshot = await backend.ReadAsync(); Assert.Equal(capture.Example!.Id, snapshot.Document.Interpretations.Single().Id);
                var input = LearningExplanationPolicy.Input(capture);
                var record = new LearningExplanationRecord(Guid.NewGuid(), input, LearningLanguage.Chinese, new("银行", "A financial institution."), "local", DateTimeOffset.UtcNow);
                Assert.True(await viewStore.SaveExplanationAsync(record)); Assert.Equal(record.Id, viewStore.Explanation(input, LearningLanguage.Chinese)!.Id);
                var destination = CanonicalWritingPolicy.SavedDestination(capture, (await backend.ReadAsync()).Document)!.Value;
                Assert.True(await viewStore.AdoptExplanationAsync(record, destination));
                Assert.Equal(VocabularyMeaningOrigin.User, (await backend.ReadAsync()).Document.Interpretations.Single().MeaningOrigin);
                var imported = await backend.AddEntriesAsync([new VocabularyEntryInput(Expression: "world", Meaning: "earth")]); Assert.True(imported.HasChanges);
                VocabularySelection? opened = null; viewStore.OpenSelection = selection => opened = selection;
                Click(button); var items = button.ContextMenu!.Items.OfType<MenuItem>().ToArray(); Assert.Equal(2, items.Length);
                ClickMenu(items[0]); Assert.Equal(new VocabularySelection(destination.EntryId, destination.SenseId), opened);
                ClickMenu(items[1]); await button.LastOperation; await Settle();
                Assert.False(viewStore.Contains(capture)); var after = await backend.ReadAsync();
                Assert.Equal(2, after.Document.Catalog.Count); Assert.Single(after.Document.Sources); Assert.Single(after.Document.Interpretations);
                Assert.Equal("Not saved", AutomationProperties.GetItemStatus(button));
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    });
    [Fact]
    public Task InvalidCanonicalCaptureShowsFixedErrorAndNeverClaimsSaved() => dispatcher.RunAsync(async () =>
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory(); await using var backend = new FrozenVocabularyImportStore(database);
        await using var viewStore = new CanonicalWritingViewStore(backend, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new LearningPreviewWindow(); var capture = Capture() with { Expression = "", DictionaryForm = "" };
        try
        {
            window.ShowState(Preview, Anchor, false); window.BindVocabulary(viewStore, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            Click(button); await button.LastOperation; await Settle();
            Assert.Equal("Not saved", AutomationProperties.GetItemStatus(button)); Assert.False(viewStore.Contains(capture));
            Assert.NotNull(viewStore.ErrorMessage); Assert.Equal(viewStore.ErrorMessage, AutomationProperties.GetHelpText(button));
            Assert.Empty((await backend.ReadAsync()).Document.Catalog);
        }
        finally { window.Close(); }
    });
    // A test-only fault wrapper keeps the actual canonical write and file owner; only the
    // follow-up read/notification are withheld to exercise ambiguous presentation timing.
    private sealed class ReadFaultStore(Frozen.ICanonicalVocabularyStore inner) : Frozen.ICanonicalVocabularyStore
    {
        private int _withhold;
        private Action<Frozen.FrozenVocabularyImportSnapshot>? _subscriber;
        internal bool Withhold { get => Volatile.Read(ref _withhold) != 0; set => Volatile.Write(ref _withhold, value ? 1 : 0); }
        internal void Deliver(Frozen.FrozenVocabularyImportSnapshot value) => Volatile.Read(ref _subscriber)?.Invoke(value);
        public IDisposable Subscribe(Action<Frozen.FrozenVocabularyImportSnapshot> changed)
        {
            Volatile.Write(ref _subscriber, changed);
            return inner.Subscribe(value => { if (!Withhold) changed(value); });
        }
        public Task<Frozen.FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken cancellationToken = default) => Withhold
            ? Task.FromException<Frozen.FrozenVocabularyImportSnapshot>(new IOException("injected")) : inner.ReadAsync(cancellationToken);
        public Task<Frozen.FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList t, VocabularyWordListConfiguration c, CancellationToken token = default) => inner.PreviewWordListAsync(t, c, token);
        public Task<Frozen.VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> i, CancellationToken token = default) => inner.AddEntriesAsync(i, token);
        public Task<Frozen.CanonicalWritingReceipt> SaveCaptureAsync(VocabularyCapture c, CancellationToken token = default) => inner.SaveCaptureAsync(c, token);
        public Task<Frozen.CanonicalWritingReceipt> RemoveCaptureAsync(VocabularyCapture c, CancellationToken token = default) => inner.RemoveCaptureAsync(c, token);
        public Task<LearningExplanationRecord?> FindExplanationAsync(LearningExplanationInput i, LearningLanguage l, CancellationToken token = default) => inner.FindExplanationAsync(i, l, token);
        public Task<Frozen.CanonicalWritingReceipt> SaveExplanationAsync(LearningExplanationRecord r, CancellationToken token = default) => inner.SaveExplanationAsync(r, token);
        public Task<Frozen.CanonicalWritingReceipt> AdoptExplanationAsync(LearningExplanationRecord r, VocabularyExplanationDestination d, CancellationToken token = default) => inner.AdoptExplanationAsync(r, d, token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    [Fact]
    public Task SuccessfulCommitWithFailedRefreshIsNotReportedAsFailedOrUnsaved() => dispatcher.RunAsync(async () =>
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory(); await using var backend = new FrozenVocabularyImportStore(database);
        var fault = new ReadFaultStore(backend); await using var viewStore = new CanonicalWritingViewStore(fault, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new LearningPreviewWindow(); var capture = Capture();
        try
        {
            window.ShowState(Preview, Anchor, false); window.BindVocabulary(viewStore, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            fault.Withhold = true; Click(button); await button.LastOperation; await Settle();
            Assert.Single((await backend.ReadAsync()).Document.Interpretations); Assert.False(button.IsEnabled);
            Assert.Equal("Unavailable", AutomationProperties.GetItemStatus(button));
            Assert.Equal(viewStore.ErrorMessage, Find<TextBlock>(window, "VocabularyStatus").Text);
            fault.Withhold = false; await viewStore.LoadAsync(); await Settle();
            Assert.True(viewStore.Contains(capture)); Assert.Equal("Saved", AutomationProperties.GetItemStatus(button));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task OlderNotificationCannotReviveStaleSavedStateAfterAcknowledgedRemove() => dispatcher.RunAsync(async () =>
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory(); await using var backend = new FrozenVocabularyImportStore(database);
        var fault = new ReadFaultStore(backend); await using var viewStore = new CanonicalWritingViewStore(fault, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new LearningPreviewWindow(); var capture = Capture();
        try
        {
            window.ShowState(Preview, Anchor, false); window.BindVocabulary(viewStore, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            Click(button); await button.LastOperation; await Settle();
            var input = LearningExplanationPolicy.Input(capture);
            var record = new LearningExplanationRecord(Guid.NewGuid(), input, LearningLanguage.English, new("bank", "A financial institution."), "local", DateTimeOffset.UtcNow);
            Assert.True(await viewStore.SaveExplanationAsync(record)); Assert.NotNull(viewStore.Explanation(input, LearningLanguage.English));
            var old = await backend.ReadAsync(); Assert.True(viewStore.Contains(capture));
            fault.Withhold = true; Click(button); ClickMenu(Assert.Single(button.ContextMenu!.Items.OfType<MenuItem>()));
            await button.LastOperation; await Settle(); var newest = await backend.ReadAsync(); Assert.True(newest.Revision > old.Revision);
            Assert.False(viewStore.IsLoaded); Assert.False(viewStore.Contains(capture)); Assert.Null(viewStore.Explanation(input, LearningLanguage.English));
            fault.Deliver(old); await Settle();
            Assert.False(viewStore.IsLoaded); Assert.False(button.IsEnabled); Assert.Equal("Unavailable", AutomationProperties.GetItemStatus(button));
            Assert.False(viewStore.Contains(capture)); Assert.Null(viewStore.Explanation(input, LearningLanguage.English));
            fault.Deliver(newest); await Settle(); Assert.True(viewStore.IsLoaded); Assert.False(viewStore.Contains(capture));
            Assert.Equal("Not saved", AutomationProperties.GetItemStatus(button));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task BridgeDisposalLeavesTheBorrowedBackendUsable() => dispatcher.RunAsync(async () =>
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory(); await using var backend = new FrozenVocabularyImportStore(database);
        var viewStore = new CanonicalWritingViewStore(backend, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        await viewStore.LoadAsync(); Assert.True(await viewStore.SaveAsync(Capture())); await viewStore.DisposeAsync();
        Assert.Single((await backend.ReadAsync()).Document.Interpretations);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => viewStore.LoadAsync());
    });
    [Fact]
    public Task DismissedCardRetiresUiButAdmittedRealWriteCompletes() => dispatcher.RunAsync(async () =>
    {
        var database = FrozenVocabularySQLiteDatabase.CreateMemory(); await using var backend = new FrozenVocabularyImportStore(database);
        await using var viewStore = new CanonicalWritingViewStore(backend, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new LearningPreviewWindow(); var capture = Capture();
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); Task? blocker = null;
        try
        {
            window.ShowState(Preview, Anchor, false); window.BindVocabulary(viewStore, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            blocker = Task.Run(() => database.Access(true, () => { held.Set(); release.Wait(); return true; }));
            Assert.True(held.Wait(TimeSpan.FromSeconds(10))); Click(button); await Settle();
            Assert.False(button.IsEnabled); Assert.IsType<ProgressBar>(button.Content); var operation = button.LastOperation;
            window.HidePreview(); Assert.Equal(Visibility.Collapsed, button.Visibility); release.Set();
            await operation; await blocker; await Settle();
            Assert.Single((await backend.ReadAsync()).Document.Interpretations); Assert.Equal(Visibility.Collapsed, button.Visibility);
        }
        finally { release.Set(); if (blocker is not null) await blocker; window.Close(); }
    });
}
