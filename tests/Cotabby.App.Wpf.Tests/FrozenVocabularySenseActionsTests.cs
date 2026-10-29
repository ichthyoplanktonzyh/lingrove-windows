using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularySenseActionsTests(WpfDispatcher dispatcher)
{
    private sealed class HeldStore(IVocabularyEntryImportStore inner, ICanonicalVocabularySenseStore senses) : IVocabularyEntryImportStore, ICanonicalVocabularySenseStore
    {
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously), Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals; internal bool Fail;
        public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) => inner.ReadAsync(token);
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => inner.PreviewWordListAsync(table, config, token);
        public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default) => inner.AddEntriesAsync(inputs, token);
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) => new Subscription(inner.Subscribe(changed), this);
        private sealed class Subscription(IDisposable subscription, HeldStore owner) : IDisposable { public void Dispose() { subscription.Dispose(); owner.Disposals++; } }
        public async Task<CanonicalSenseReceipt> CreateSenseAsync(Guid entryId, CancellationToken token = default)
        { if (Fail) throw new VocabularyException(VocabularyFailure.EditConflict); var receipt = await senses.CreateSenseAsync(entryId, token); Written.TrySetResult(); await Gate.Task; return receipt; }
        public Task<FrozenVocabularyImportSnapshot> MergeSensesAsync(Guid entryId, Guid sourceId, Guid targetId, CancellationToken token = default) => senses.MergeSensesAsync(entryId, sourceId, targetId, token);
        public Task<FrozenVocabularyImportSnapshot> RemoveSenseAsync(Guid entryId, Guid senseId, CancellationToken token = default) => senses.RemoveSenseAsync(entryId, senseId, token);
        public Task<CanonicalSenseReceipt> MoveExampleAsync(Guid entryId, Guid exampleId, Guid sourceId, Guid? targetId, CancellationToken token = default) => senses.MoveExampleAsync(entryId, exampleId, sourceId, targetId, token);
    }
    [Fact]
    public Task LateAcknowledgementDoesNotOverrideNewSelection() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var first = await store.AddEntriesAsync([new(Expression: "first")]); var second = await store.AddEntriesAsync([new(Expression: "second")]); var held = new HeldStore(store, store);
        var window = new FrozenVocabularyNotebookWindow(held, new WindowsVocabularyWordListReader(), hideOnClose: true); window.Show(); await window.LastOperation!; window.Reveal(first.Selection); await window.LastOperation!;
        Find<Button>(window, "frozenNewSense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var pending = window.LastOperation!; await held.Written.Task;
        Assert.False(Find<Button>(window, "frozenNewSense").IsEnabled); window.Reveal(second.Selection); await window.LastOperation!; held.Gate.SetResult(); await pending;
        Assert.Equal(second.Selection, window.Session.Selection); await window.RetireAsync(); Assert.Equal(1, held.Disposals);
    });
    [Fact]
    public Task RetirementJoinsAcceptedWriteBeforeUnsubscribe() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var first = await store.AddEntriesAsync([new(Expression: "first")]); var held = new HeldStore(store, store);
        var window = new FrozenVocabularyNotebookWindow(held, new WindowsVocabularyWordListReader(), hideOnClose: true); window.Show(); await window.LastOperation!; window.Reveal(first.Selection); await window.LastOperation!;
        Find<Button>(window, "frozenNewSense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await held.Written.Task; var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted); Assert.Equal(0, held.Disposals);
        held.Gate.SetResult(); await retirement; Assert.Equal(1, held.Disposals); Assert.Equal(2, (await store.ReadAsync()).Document.CollectedEntries.Single().Senses.Count);
    });
    [Fact]
    public Task StoreFailureShowsErrorAndReenablesCommands() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var first = await store.AddEntriesAsync([new(Expression: "first")]); var held = new HeldStore(store, store) { Fail = true };
        var window = new FrozenVocabularyNotebookWindow(held, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; window.Reveal(first.Selection); await window.LastOperation!;
        Find<Button>(window, "frozenNewSense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; Assert.True(Find<Button>(window, "frozenNewSense").IsEnabled); Assert.Contains("changed while", Find<TextBlock>(window, "frozenSenseActionError").Text);
        VocabularyThemeRender.Capture(window, "sense-action-error", 1100, 720); window.Close(); Assert.Equal(1, held.Disposals);
    });
    [Fact]
    public Task StaleMenuCannotMutateAfterNavigation() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var first = await store.AddEntriesAsync([new(Expression: "first", Example: "first example")]); var second = await store.AddEntriesAsync([new(Expression: "second")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; window.Reveal(first.Selection); await window.LastOperation!;
        var move = Find<Button>(window, "frozenMoveUsage"); move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var stale = move.ContextMenu!.Items.Cast<MenuItem>().Single(); var revision = (await store.ReadAsync()).Revision;
        window.Reveal(second.Selection); await window.LastOperation!; stale.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal(revision, (await store.ReadAsync()).Revision); Assert.Equal(second.Selection, window.Session.Selection); window.Close();
    });
    [Fact]
    public Task RetirementClosesPendingConfirmationWithoutDeleting() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var first = await store.AddEntriesAsync([new(Expression: "first")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), hideOnClose: true); window.Show(); await window.LastOperation!; window.Reveal(first.Selection); await window.LastOperation!;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () => { try { Assert.Single(Application.Current.Windows.OfType<FrozenVocabularyDeleteSenseWindow>()); await window.RetireAsync(); done.SetResult(); } catch (Exception e) { done.SetException(e); } });
        Find<Button>(window, "frozenDeleteSense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await done.Task; Assert.Single((await store.ReadAsync()).Document.CollectedEntries);
    });
    [Fact]
    public Task RealStoreCreateMoveMergeAndDeletePreserveEvidence() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory(), () => new SourceDate(123));
        var first = await store.AddEntriesAsync([new(Expression: "bank", Meaning: "shore", Example: "river bank")]); var source = first.Selection!.Value;
        var before = await store.ReadAsync(); var usage = before.Document.CollectedEntries.Single().Senses.Single().Examples.Single();
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; await window.Session.RevealSelectionAsync(source);
        VocabularyThemeRender.Capture(window, "sense-actions", 1100, 720);
        Find<Button>(window, "frozenNewSense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        var created = window.Session.Selection!.Value; Assert.Equal(source.EntryId, created.EntryId); Assert.NotEqual(source.SenseId, created.SenseId);
        await window.Session.RevealSelectionAsync(source); var move = Find<Button>(window, "frozenMoveUsage"); move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var target = move.ContextMenu!.Items.Cast<MenuItem>().Single(x => Equals(x.Tag, created.SenseId)); target.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await window.LastOperation!;
        Assert.Equal(source, window.Session.Selection); var afterMove = await store.ReadAsync(); var projected = afterMove.Document.CollectedEntries.Single(); Assert.Empty(projected.Senses.Single(s => s.Id == source.SenseId).Examples); Assert.Equal(usage.Id, projected.Senses.Single(s => s.Id == created.SenseId).Examples.Single().Id);
        var merge = Find<Button>(window, "frozenMergeSense"); merge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); CaptureMenu(window, merge.ContextMenu!); merge.ContextMenu!.Items.Cast<MenuItem>().Single(x => Equals(x.Tag, created.SenseId)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await window.LastOperation!;
        Assert.Equal(created, window.Session.Selection); var merged = await store.ReadAsync(); Assert.Contains(source.SenseId, merged.Document.Entries.Single().Senses.Single().MergedFrom); Assert.Equal(usage.Id, merged.Document.CollectedEntries.Single().Senses.Single().Examples.Single().Id);
        _ = window.Dispatcher.BeginInvoke(() => { var confirm = Application.Current.Windows.OfType<FrozenVocabularyDeleteSenseWindow>().Single(); VocabularyThemeRender.Capture(confirm, "sense-delete-confirm", 410, (int)Math.Ceiling(confirm.ActualHeight)); Find<Button>(confirm, "frozenDeleteCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Find<Button>(window, "frozenDeleteSense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; Assert.Equal(merged.Revision, (await store.ReadAsync()).Revision);
        _ = window.Dispatcher.BeginInvoke(() => Find<Button>(Application.Current.Windows.OfType<FrozenVocabularyDeleteSenseWindow>().Single(), "frozenDeleteConfirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Find<Button>(window, "frozenDeleteSense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        var removed = await store.ReadAsync(); Assert.Empty(removed.Document.CollectedEntries); Assert.Single(removed.Document.Sources); Assert.Equal(usage.SourceId, removed.Document.Sources.Single().Id); Assert.Contains(removed.Document.Catalog.Single().Senses, s => s.Id == created.SenseId); Assert.Null(window.Session.Selection); window.Close();
    });
    [Fact]
    public Task MoveToNewSenseKeepsSourceSelected() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var result = await store.AddEntriesAsync([new(Expression: "hello", Example: "hello there")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; await window.Session.RevealSelectionAsync(result.Selection);
        var move = Find<Button>(window, "frozenMoveUsage"); move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var item = move.ContextMenu!.Items.Cast<MenuItem>().Single(i => i.Tag is null);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Input); Assert.True(item.Focus()); var source = PresentationSource.FromVisual(item)!; item.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent }); await window.LastOperation!;
        Assert.Equal(result.Selection, window.Session.Selection); var entry = (await store.ReadAsync()).Document.CollectedEntries.Single(); Assert.Equal(2, entry.Senses.Count); Assert.Empty(entry.Senses.Single(s => s.Id == result.Selection!.Value.SenseId).Examples); Assert.Single(entry.Senses.Single(s => s.Id != result.Selection!.Value.SenseId).Examples); window.Close();
    });
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<T>().First(x => x.Name == name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var value in Descendants(VisualTreeHelper.GetChild(root, i))) yield return value; }
    private static void CaptureMenu(Window window, ContextMenu menu)
    {
        if (Environment.GetEnvironmentVariable("LINGROVE_TEST_RENDER_DIR") is not { Length: > 0 } dir) return; System.IO.Directory.CreateDirectory(dir);
        foreach (var (suffix, dark, high) in new[] { ("light", false, false), ("dark", true, false), ("highcontrast-simulated", true, true) })
        {
            VocabularyNotebookTheme.ApplyPalette(window, dark, high);
            if (high) { window.Resources["CardBg"] = Brushes.Black; window.Resources["CardBorder"] = Brushes.White; window.Resources["TextPrimary"] = Brushes.White; }
            menu.Measure(new Size(240, double.PositiveInfinity)); menu.Arrange(new Rect(0, 0, 240, menu.DesiredSize.Height)); menu.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(240, (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using var stream = System.IO.File.Create(System.IO.Path.Combine(dir, "sense-merge-menu-" + suffix + ".png")); encoder.Save(stream);
        }
        VocabularyNotebookTheme.ApplyPalette(window, Cotabby.App.UI.SystemTheme.IsDark, SystemParameters.HighContrast);
    }
}
