using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyEntryInputIntegrationTests(WpfDispatcher dispatcher)
{
    private static readonly SourceDate Time = new(814759445.12345672);
    private static FrozenVocabularyImportStore Store() => new(FrozenVocabularySQLiteDatabase.CreateMemory(), () => Time);
    [Fact]
    public Task CreationImportDuplicateAndEvidenceSurviveRealAcknowledgement() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var owner = new FrozenVocabularyEntryInputOwner(store);
        var create = new VocabularyEntryCreationWindow(owner); create.Show();
        Find<TextBox>(create, "vocabularyNewExpression").Text = "bank"; Find<TextBox>(create, "vocabularyNewMeaning").Text = "shore";
        Find<Button>(create, "vocabularyCreateEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await create.LastOperation!;
        var first = create.Result!; Assert.Equal(1, first.AddedEntries); Assert.Equal(1, first.AddedSenses); Assert.NotNull(first.Selection);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            await File.WriteAllTextAsync(path, "expression,meaning,example,translation\nbank,shore,river bank,河岸\n");
            var window = new VocabularyWordListImportWindow(owner, new WindowsVocabularyWordListReader(), path); window.Show(); await window.LastOperation!;
            Assert.Equal(1, window.Session.Preview!.Plan.Result.AddedExamples);
            Find<Button>(window, "vocabularyConfirmImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
            Assert.Equal(first.Selection, window.Result!.Selection); Assert.Equal(1, window.Result.AddedExamples);
            var before = await store.ReadAsync(); Assert.Equal(2UL, before.Revision);
            Assert.Single(before.Document.Sources); Assert.Single(before.Document.Occurrences); Assert.Single(before.Document.Interpretations);
            Assert.Equal(Time, before.Document.Catalog[0].CreatedAt); Assert.Equal(Time, before.Document.Sources[0].EncounteredAt);
            var duplicate = await owner.AddEntriesAsync([new(Expression: "bank", Meaning: "shore", Example: "river bank", ExampleTranslation: "河岸")], default);
            var after = await store.ReadAsync(); Assert.False(duplicate.HasChanges); Assert.Equal(first.Selection, duplicate.Selection);
            Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.Document, after.Document);
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task RealStoreRevisionRefreshAndSubscriptionRetirement() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var tracking = new TrackingStore(store); var owner = new FrozenVocabularyEntryInputOwner(tracking);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"); await File.WriteAllTextAsync(path, "hello");
        try
        {
            var session = new VocabularyEntryImportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, owner, new WindowsVocabularyWordListReader());
            await session.LoadAsync(path, LearningLanguage.English); Assert.Equal(0UL, session.Preview!.Plan.Revision);
            var added = await store.AddEntriesAsync([new(Expression: "hello")]);
            await Until(() => session.Preview?.Plan.Revision == 1);
            Assert.False(session.Preview!.Plan.Result.HasChanges); Assert.Equal(added.Selection!.Value.EntryId, session.Preview.Plan.Result.Selection!.EntryId);
            session.Cancel(); session.Cancel(); Assert.Equal(1, tracking.Disposals); var detached = session.Preview;
            tracking.DeliverLate(await store.ReadAsync());
            await store.AddEntriesAsync([new(Expression: "other")]); await Task.Delay(100);
            Assert.Same(detached, session.Preview); Assert.False(session.Active);
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task StaleRealPreviewCommitReplansAgainstConcurrentStoreWrite() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var tracking = new TrackingStore(store) { SuppressDelivery = true }; var owner = new FrozenVocabularyEntryInputOwner(tracking);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"); await File.WriteAllTextAsync(path, "same");
        try
        {
            var session = new VocabularyEntryImportSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, owner, new WindowsVocabularyWordListReader());
            await session.LoadAsync(path, LearningLanguage.English); Assert.True(session.Preview!.Plan.Result.HasChanges);
            var actual = await store.AddEntriesAsync([new(Expression: "same")]); var result = await session.CommitAsync();
            Assert.NotNull(result); Assert.False(result!.HasChanges); Assert.Equal(actual.Selection!.Value.EntryId, result.Selection!.EntryId);
            Assert.Equal(actual.Selection.Value.SenseId, result.Selection.SenseId); Assert.Equal(1UL, (await store.ReadAsync()).Revision);
            session.Cancel(); Assert.Equal(1, tracking.Disposals);
        }
        finally { File.Delete(path); }
    });
    [Fact]
    public Task WindowRetiresOnlyAfterRealAcceptedWriteDrains() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(); var tracking = new TrackingStore(store) { HoldAcknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var owner = new FrozenVocabularyEntryInputOwner(tracking); string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(path, "held");
        try
        {
            var window = new VocabularyWordListImportWindow(owner, new WindowsVocabularyWordListReader(), path); window.Show(); await window.LastOperation!;
            Find<Button>(window, "vocabularyConfirmImport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await tracking.Written.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1UL, (await store.ReadAsync()).Revision); window.Close(); Assert.True(window.IsVisible); Assert.Equal(0, tracking.Disposals);
            tracking.HoldAcknowledgement.SetResult(); await window.LastOperation!;
            Assert.NotNull(window.Result); Assert.Equal(1, tracking.Disposals); Assert.False(window.IsVisible);
            // Closing the window retires only its subscription; the borrowed owner remains usable.
            Assert.Equal(1UL, (await store.ReadAsync()).Revision);
        }
        finally { tracking.HoldAcknowledgement.TrySetResult(); File.Delete(path); }
    });
    private sealed class TrackingStore(IVocabularyEntryImportStore inner) : IVocabularyEntryImportStore
    {
        internal bool SuppressDelivery; internal int Disposals; internal TaskCompletionSource? HoldAcknowledgement;
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<FrozenVocabularyImportSnapshot>? _callback;
        internal void DeliverLate(FrozenVocabularyImportSnapshot snapshot) => Task.Run(() => _callback?.Invoke(snapshot)).GetAwaiter().GetResult();
        public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) => inner.ReadAsync(token);
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => inner.PreviewWordListAsync(table, config, token);
        public async Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default)
        { var result = await inner.AddEntriesAsync(inputs, token); Written.TrySetResult(); if (HoldAcknowledgement is { } gate) await gate.Task; return result; }
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed)
        { _callback = changed; return new Subscription(this, inner.Subscribe(value => { if (!SuppressDelivery) changed(value); })); }
        private sealed class Subscription(TrackingStore owner, IDisposable inner) : IDisposable { public void Dispose() { owner.Disposals++; inner.Dispose(); } }
    }
    private static async Task Until(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(10); while (!predicate()) { if (DateTime.UtcNow >= until) throw new TimeoutException(); await Task.Delay(10); }
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
