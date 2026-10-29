using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyWindowControllerTests(WpfDispatcher dispatcher)
{
    private sealed class Preferences : IVocabularyNotebookListPreferences { public string Density { get; set; } = "detailed"; public bool ShowUnexplored { get; set; } }
    [Fact]
    public Task LazyHideReopenAndExactShowPreservePreferencesAndBorrowedOwner() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var one = await store.AddEntriesAsync([new(Expression: "bank", Meaning: "shore")]);
        var two = await store.AddEntriesAsync([new(Expression: "other", Meaning: "other meaning", Language: LearningLanguage.French)]);
        var prefs = new Preferences(); var tracked = new TrackingStore(store);
        var controller = new FrozenVocabularyWindowController(tracked, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), prefs, [LearningLanguage.English]);
        Assert.Null(controller.Window); Assert.Equal(0, tracked.Reads); Assert.Equal(0, tracked.Subscriptions);
        controller.Show(); var window = controller.Window!; await window.LastOperation!;
        Assert.True(window.IsVisible); Assert.True(window.Session.Detailed); Assert.Equal(1, tracked.Subscriptions);
        Find<ComboBox>(window, "frozenNotebookOrganization").SelectedIndex = 1; window.UpdateLayout(); var unexplored = Find<CheckBox>(window, "canonicalShowUnexplored"); Assert.False(unexplored.IsChecked); unexplored.IsChecked = true; unexplored.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Assert.True(prefs.ShowUnexplored); Find<ComboBox>(window, "frozenNotebookOrganization").SelectedIndex = 0;
        Find<TextBox>(window, "frozenNotebookSearch").Text = "other"; Find<ComboBox>(window, "frozenNotebookLanguage").SelectedIndex = 3;
        Find<ComboBox>(window, "frozenNotebookDensity").SelectedIndex = 0; Assert.Equal("compact", prefs.Density);
        window.Close(); Assert.False(window.IsVisible); Assert.True(window.Session.Active);
        controller.Show(); Assert.Same(window, controller.Window); Assert.Equal("other", window.Session.Query); Assert.Equal(LearningLanguage.French, window.Session.Language);
        controller.SetPreferredLanguages([LearningLanguage.Chinese]); Assert.Equal(LearningLanguage.Chinese, window.Session.PreferredLanguages[0]);
        controller.Show(one.Selection); await window.LastOperation!;
        Assert.Equal(one.Selection, window.Session.Selection); Assert.Equal("", window.Session.Query); Assert.Null(window.Session.Language); Assert.False(window.Session.MeaningMode);
        controller.Show(two.Selection); await window.LastOperation!; Assert.Equal(two.Selection, window.Session.Selection);
        await controller.DisposeAsync(); Assert.False(window.Session.Active); Assert.Equal(1, tracked.Disposals); Assert.Equal(2ul, (await store.ReadAsync()).Revision);
        controller.Show(); Assert.Null(controller.Window);
    });
    [Fact]
    public Task ColdShowResolvesExactSelectionAfterLoading() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var result = await store.AddEntriesAsync([new(Expression: "bank", Meaning: "shore")]);
        var tracked = new TrackingStore(store) { ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var controller = new FrozenVocabularyWindowController(tracked, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
        controller.Show(result.Selection); await tracked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var window = controller.Window!; Assert.Null(window.Session.Selection); tracked.ReadGate.SetResult();
        await Until(() => window.Session.Selection == result.Selection); Assert.Equal(result.Selection, window.Session.Selection);
        await controller.DisposeAsync(); Assert.Equal(1, tracked.Disposals);
    });
    [Fact]
    public Task RetirementWaitsForLateReadBeforeUnsubscribing() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var tracked = new TrackingStore(store) { ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var controller = new FrozenVocabularyWindowController(tracked, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
        controller.Show(); await tracked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)); var retirement = controller.DisposeAsync().AsTask();
        Assert.False(retirement.IsCompleted); Assert.Equal(0, tracked.Disposals); controller.Show();
        tracked.ReadGate.SetResult(); await retirement.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, tracked.Disposals);
        Assert.Equal(0ul, (await store.ReadAsync()).Revision);
    });
    [Fact]
    public Task ShutdownDrainsAcceptedModalWriteAndClosesAdmission() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var tracked = new TrackingStore(store) { AddGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var controller = new FrozenVocabularyWindowController(tracked, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
        controller.Show(); var window = controller.Window!; await window.LastOperation!;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var child = Application.Current.Windows.OfType<VocabularyEntryCreationWindow>().Single();
                Find<TextBox>(child, "vocabularyNewExpression").Text = "saved";
                Find<Button>(child, "vocabularyCreateEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await tracked.AddWritten.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var retiring = controller.DisposeAsync().AsTask(); Assert.False(retiring.IsCompleted); Assert.Equal(0, tracked.Disposals);
                controller.Show(); tracked.AddGate!.SetResult(); await retiring.WaitAsync(TimeSpan.FromSeconds(10)); completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
        });
        Find<Button>(window, "frozenNotebookAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Null(controller.Window); Assert.Equal(1, tracked.Disposals);
        Assert.Equal("saved", (await store.ReadAsync()).Document.CollectedEntries.Single().Expression);
    });
    [Fact]
    public Task ShutdownClosesSheetAfterFailedAcceptedWrite() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var tracked = new TrackingStore(store) { FailAdd = true, AddGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var controller = new FrozenVocabularyWindowController(tracked, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
        controller.Show(); var window = controller.Window!; await window.LastOperation!;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var child = Application.Current.Windows.OfType<VocabularyEntryCreationWindow>().Single();
                Find<TextBox>(child, "vocabularyNewExpression").Text = "not saved"; Find<Button>(child, "vocabularyCreateEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await tracked.AddWritten.Task.WaitAsync(TimeSpan.FromSeconds(10)); var retirement = controller.DisposeAsync().AsTask(); Assert.False(retirement.IsCompleted);
                tracked.AddGate!.SetResult(); await retirement.WaitAsync(TimeSpan.FromSeconds(10)); completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
        });
        Find<Button>(window, "frozenNotebookAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0ul, (await store.ReadAsync()).Revision); Assert.Equal(1, tracked.Disposals);
    });
    [Fact]
    public Task ExplicitShowDuringSheetDefersNavigationUntilSheetCloses() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var selected = await store.AddEntriesAsync([new(Expression: "target")]);
        var controller = new FrozenVocabularyWindowController(store, System.Windows.Threading.Dispatcher.CurrentDispatcher, new WindowsVocabularyWordListReader(), new Preferences());
        controller.Show(); var window = controller.Window!; await window.LastOperation!;
        Find<TextBox>(window, "frozenNotebookSearch").Text = "hidden";
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            var child = Application.Current.Windows.OfType<VocabularyEntryCreationWindow>().Single();
            controller.Show(selected.Selection); Assert.Equal("hidden", window.Session.Query); Assert.True(child.IsVisible); window.Close(); Assert.True(window.IsVisible); child.Close();
        });
        Find<Button>(window, "frozenNotebookAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        Assert.Equal(selected.Selection, window.Session.Selection); Assert.Equal("", window.Session.Query); Assert.False(window.IsVisible); controller.Show(); Assert.Same(window, controller.Window); await controller.DisposeAsync();
    });
    [Fact]
    public Task RetirementCancelsAndDrainsEveryImportPreview() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var tracked = new TrackingStore(store) { PreviewGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"); await File.WriteAllTextAsync(path, "expression,meaning\nbank,shore\n");
        var window = new FrozenVocabularyNotebookWindow(tracked, new WindowsVocabularyWordListReader(), pickFile: () => path, hideOnClose: true); window.Show(); await window.LastOperation!;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var child = Application.Current.Windows.OfType<VocabularyWordListImportWindow>().Single();
                await tracked.PreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                // Reconfiguration starts another preview while the first provider ignores cancellation.
                Find<CheckBox>(child, "vocabularyImportHeader").IsChecked = false;
                await Until(() => Volatile.Read(ref tracked.PreviewCalls) >= 2);
                var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted); Assert.True(tracked.LastPreviewToken.IsCancellationRequested);
                tracked.PreviewGate!.SetResult(); await retirement.WaitAsync(TimeSpan.FromSeconds(10)); completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
        });
        try
        {
            Find<Button>(window, "frozenNotebookImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(window.Session.Active); Assert.Equal(0ul, (await store.ReadAsync()).Revision);
        }
        finally { tracked.PreviewGate!.TrySetResult(); File.Delete(path); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NormalImportCancelStillDrainsLateReaderOrPreviewBeforeShutdown(bool holdPreview) => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var tracked = new TrackingStore(store) { PreviewGate = holdPreview ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null };
        var lateReader = new DelayedReader(); string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        await File.WriteAllTextAsync(path, "expression,meaning\nbank,shore\n");
        IVocabularyWordListReader reader = holdPreview ? new WindowsVocabularyWordListReader() : lateReader;
        var window = new FrozenVocabularyNotebookWindow(tracked, reader, pickFile: () => path, hideOnClose: true); window.Show(); await window.LastOperation!;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var child = Application.Current.Windows.OfType<VocabularyWordListImportWindow>().Single();
                await (holdPreview ? tracked.PreviewStarted.Task : lateReader.Started.Task).WaitAsync(TimeSpan.FromSeconds(10));
                child.Close(); Assert.False(child.IsVisible);
                // The import modal has returned normally; let its parent resume before shutdown.
                _ = window.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted); Assert.Equal(2, tracked.Subscriptions); Assert.Equal(1, tracked.Disposals);
                        if (holdPreview) { Assert.True(tracked.LastPreviewToken.IsCancellationRequested); tracked.PreviewGate!.SetResult(); }
                        else { Assert.True(lateReader.Token.IsCancellationRequested); lateReader.Gate.SetResult(); }
                        await retirement.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(window.Session.Active); Assert.False(child.Session.Active); Assert.Null(child.Session.Preview); Assert.Equal(2, tracked.Disposals); done.SetResult();
                    }
                    catch (Exception error) { done.SetException(error); }
                });
            }
            catch (Exception error) { done.SetException(error); }
        });
        try
        {
            Find<Button>(window, "frozenNotebookImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(0ul, (await store.ReadAsync()).Revision);
            if (window.LastOperation is { } operation) await operation;
        }
        finally { lateReader.Gate.TrySetResult(); tracked.PreviewGate?.TrySetResult(); File.Delete(path); }
    });
    private sealed class DelayedReader : IVocabularyWordListReader
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token;
        public async Task<VocabularyWordList> ReadAsync(string path, CancellationToken token)
        { Token = token; Started.TrySetResult(); await Gate.Task; return new(VocabularyWordListFormat.Text, [new(1, ["bank"])]); }
    }
    private static async Task Until(Func<bool> predicate)
    { var deadline = DateTime.UtcNow.AddSeconds(10); while (!predicate()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException(); await Task.Delay(10); } }
    private sealed class TrackingStore(IVocabularyEntryImportStore inner) : IVocabularyEntryImportStore
    {
        internal int Reads, Subscriptions, Disposals, PreviewCalls; internal bool FailAdd; internal CancellationToken LastPreviewToken; internal TaskCompletionSource? ReadGate, AddGate, PreviewGate;
        internal readonly TaskCompletionSource PreviewStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), AddWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default)
        { Reads++; ReadStarted.TrySetResult(); if (ReadGate is { } gate) await gate.Task; return await inner.ReadAsync(CancellationToken.None); }
        public async Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default)
        { Interlocked.Increment(ref PreviewCalls); LastPreviewToken = token; var result = await inner.PreviewWordListAsync(table, config, CancellationToken.None); PreviewStarted.TrySetResult(); if (PreviewGate is { } gate) await gate.Task; return result; }
        public async Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default)
        { if (FailAdd) { AddWritten.TrySetResult(); if (AddGate is { } failedGate) await failedGate.Task; throw new IOException("Failed write"); }
          var result = await inner.AddEntriesAsync(inputs, token); AddWritten.TrySetResult(); if (AddGate is { } gate) await gate.Task; return result; }
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) { Subscriptions++; return new Subscription(this, inner.Subscribe(changed)); }
        private sealed class Subscription(TrackingStore owner, IDisposable inner) : IDisposable { public void Dispose() { owner.Disposals++; inner.Dispose(); } }
    }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T value && value.Name == name) return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindOrNull<T>(VisualTreeHelper.GetChild(root, i), name) is { } result) return result;
        throw new InvalidOperationException(name);
    }
    private static T? FindOrNull<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T value && value.Name == name) return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindOrNull<T>(VisualTreeHelper.GetChild(root, i), name) is { } result) return result;
        return null;
    }
}
