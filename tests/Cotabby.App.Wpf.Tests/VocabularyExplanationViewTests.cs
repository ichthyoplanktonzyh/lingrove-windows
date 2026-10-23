using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Actual notebook windows, stable child sessions and memory SQLite exercise Mac contextual
/// explanation/adoption. Only external model replies are controlled; Store/archive and real
/// routed buttons perform the mutations. No editor, clipboard, model or endpoint is used.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyExplanationViewTests(WpfDispatcher dispatcher)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static VocabularyCapture Capture(string text = "The bank.") => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
        Example: new(Guid.NewGuid(), text, "原句", [new(text.IndexOf("bank", StringComparison.Ordinal), 4)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch));
    private static async Task<(VocabularyStore Store, VocabularyEntry Entry)> Seed(bool standalone = true, bool userMeaning = true, VocabularyArchive? archive = null)
    {
        var store = new VocabularyStore(archive ?? new VocabularyArchive((string?)null), new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var origin = userMeaning ? VocabularyMeaningOrigin.User : VocabularyMeaningOrigin.Ai;
        var capture = Capture(); capture = capture with { Example = capture.Example! with
            { Meaning = "authored context", MeaningLanguage = LearningLanguage.Chinese, MeaningOrigin = origin } };
        Assert.True(await store.SaveAsync(capture)); var entry = Assert.Single(store.Entries); var sense = Assert.Single(entry.Senses);
        // Standalone membership is capture identity, not an editable text field. Exercise the
        // actual Save path; the three-way editor correctly ignores a draft toggling this flag.
        if (standalone) Assert.True(await store.SaveAsync(new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word, SenseId: sense.Id)));
        entry = Assert.Single(store.Entries); sense = Assert.Single(entry.Senses);
        Assert.True(await store.UpdateAsync(entry with { Senses = [sense with { Meaning = "authored general", MeaningLanguage = LearningLanguage.Chinese }] }, entry));
        entry = Assert.Single(store.Entries); Assert.Equal(standalone, entry.Senses[0].IsSavedWithoutContext); Assert.Equal(origin, entry.Examples[0].MeaningOrigin);
        return (store, entry);
    }
    private static VocabularyNotebookWindow Window(VocabularyStore store, VocabularyNotebookSession session, VocabularyEntry entry, Action? configure = null)
    { var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), session, configure); window.Reveal(new(entry.Id, entry.Senses[0].Id)); window.Show(); return window; }
    private static async Task Refresh(VocabularyNotebookWindow window)
    { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; window.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index); if (child is T result) yield return result; foreach (var value in Descendants<T>(child)) yield return value; }
    }
    private static VocabularyExplanationRow Row(VocabularyNotebookWindow window, bool standalone = false) => Descendants<VocabularyExplanationRow>(window)
        .Single(row => standalone ? row.Session.State.Input!.ContextKind == LearningExplanationContext.None : row.Session.State.Input!.ContextKind != LearningExplanationContext.None);
    private static Button Generate(VocabularyExplanationRow row) => Descendants<Button>(row).Single(button => button.Width == 28);
    // Collapsed WPF content need not have realized a visual template yet. Inspect the real
    // logical controls for its state, then use IsVisible after expansion for rendering checks.
    private static Button Replace(VocabularyExplanationRow row) => Assert.Single(((StackPanel)Details(row).Content).Children.OfType<Button>());
    private static Expander Details(VocabularyExplanationRow row) => Assert.Single(row.Children.OfType<Expander>());
    private static TextBox[] Texts(VocabularyExplanationRow row) => Descendants<TextBox>(row).ToArray();

    [Fact]
    public Task NavigationIsIdleAndTheExplicitButtonStartsStopsAndActivatesOnlyItsOwnOccurrence() => dispatcher.RunAsync(async () =>
    {
        var seeded = await Seed(); await using var store = seeded.Store; var service = new Service(); var provider = new Provider();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new Text(), service); var window = Window(store, session, seeded.Entry);
        try
        {
            await Refresh(window); Assert.Equal(2, Descendants<VocabularyExplanationRow>(window).Count()); Assert.Equal(0, service.Generations); Assert.Equal(0, provider.Calls);
            var example = Row(window); var general = Row(window, true);
            Click(Generate(example)); var first = await service.Next(); Assert.True(example.Session.State.IsGenerating);
            Click(Generate(general)); var second = await service.Next(); Assert.False(example.Session.State.IsGenerating); Assert.True(first.Token.IsCancellationRequested);
            Click(Generate(general)); Assert.False(general.Session.State.IsGenerating); Assert.True(second.Token.IsCancellationRequested);
            service.Release(); await example.LastGeneration.WaitAsync(Timeout); await general.LastGeneration.WaitAsync(Timeout);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.Null(example.Session.State.Record); Assert.Null(general.Session.State.Record); Assert.Equal(0, service.Displays);
            Assert.Equal(2, service.Generations); Assert.Equal(0, provider.Calls);
        }
        finally { service.Release(); await window.RetireAsync(); }
    });

    [Fact]
    public Task UserMeaningLeadsAndDetailsContainAProposalUntilReplaceAcknowledgesTheExactUsage() => dispatcher.RunAsync(async () =>
    {
        var seeded = await Seed(); await using var store = seeded.Store; var service = new Service();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new Text(), service); var window = Window(store, session, seeded.Entry);
        try
        {
            await Refresh(window); var row = Row(window); var selection = window.Selection;
            Click(Generate(row)); var call = await service.Next(); call.Complete(); await Settled(row); await Refresh(window);
            Assert.Contains(Texts(row), box => box.Text == "authored context" && box.Visibility == Visibility.Visible);
            Assert.False(Details(row).IsExpanded); Assert.Equal("authored context", store.Entries[0].Examples[0].Meaning);
            Details(row).IsExpanded = true; window.UpdateLayout();
            Assert.Contains(Texts(row), box => box.Text == "generated" && box.Visibility == Visibility.Visible);
            Assert.Contains(Texts(row), box => box.Text == "literal <b>prose</b>" && box.IsReadOnly); Assert.True(Replace(row).IsVisible);
            Click(Replace(row)); await row.LastAdoption.WaitAsync(Timeout); await Refresh(window);
            Assert.Equal("generated", store.Entries[0].Examples[0].Meaning); Assert.Equal(VocabularyMeaningOrigin.User, store.Entries[0].Examples[0].MeaningOrigin);
            Assert.Equal("authored general", store.Entries[0].Senses[0].Meaning); Assert.Equal(selection, window.Selection); Assert.Same(row, Row(window)); Assert.True(Details(row).IsExpanded);
            Assert.Equal(Visibility.Collapsed, Replace(row).Visibility); Assert.Equal(1, service.Generations);
        }
        finally { service.Release(); await window.RetireAsync(); }
    });

    [Fact]
    public Task SameInputStoreAnnotationsKeepDetailsAndSelectionWhileAChangedContextCollapsesDetails() => dispatcher.RunAsync(async () =>
    {
        var seeded = await Seed(); await using var store = seeded.Store; var service = new Service();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new Text(), service); var window = Window(store, session, seeded.Entry);
        try
        {
            await Refresh(window); var row = Row(window); Click(Generate(row)); (await service.Next()).Complete(); await Settled(row); window.UpdateLayout();
            Details(row).IsExpanded = true; window.UpdateLayout(); var prose = Texts(row).Single(box => box.Text == "literal <b>prose</b>"); prose.Select(0, 7);
            var baseline = store.Entries[0]; var sense = baseline.Senses[0];
            Assert.True(await store.UpdateAsync(baseline with { Senses = [sense with { Note = "later note" }] }, baseline)); await Refresh(window);
            Assert.Same(row, Row(window)); Assert.True(Details(row).IsExpanded); Assert.Equal("literal", prose.SelectedText);
            baseline = store.Entries[0]; sense = baseline.Senses[0];
            Assert.True(await store.UpdateAsync(baseline with { Senses = [sense with { Examples = [sense.Examples[0] with { Text = "The bank opens." }] }] }, baseline)); await Refresh(window);
            Assert.Same(row, Row(window)); Assert.False(Details(row).IsExpanded); Assert.Null(row.Session.State.Record); Assert.Equal(1, service.Generations);
        }
        finally { service.Release(); await window.RetireAsync(); }
    });

    [Fact]
    public Task GeneratedExampleMeaningCanLeadButStandaloneMeaningAlwaysRemainsAuthoredUntilAdopted() => dispatcher.RunAsync(async () =>
    {
        var seeded = await Seed(userMeaning: false); await using var store = seeded.Store; var service = new Service();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new Text(), service); var window = Window(store, session, seeded.Entry);
        try
        {
            await Refresh(window); var example = Row(window); var general = Row(window, true);
            Click(Generate(example)); (await service.Next()).Complete(); await Settled(example); window.UpdateLayout();
            Assert.Contains(Texts(example), box => box.Text == "generated" && box.IsVisible); Assert.Equal(Visibility.Collapsed, Replace(example).Visibility);
            Click(Generate(general)); (await service.Next()).Complete(); await Settled(general); window.UpdateLayout();
            Assert.Contains(Texts(general), box => box.Text == "authored general" && box.IsVisible); Details(general).IsExpanded = true; window.UpdateLayout(); Assert.True(Replace(general).IsVisible);
            Assert.Equal("authored context", store.Entries[0].Examples[0].Meaning); Assert.Equal("authored general", store.Entries[0].Senses[0].Meaning);
        }
        finally { service.Release(); await window.RetireAsync(); }
    });

    [Fact]
    public Task OrdinaryCloseCancelsWorkPreservesRowsAndExplicitShowResetsPresentationWithoutStartingWork() => dispatcher.RunAsync(async () =>
    {
        var seeded = await Seed(); await using var store = seeded.Store; var service = new Service();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new Text(), service); var window = Window(store, session, seeded.Entry);
        try
        {
            await Refresh(window); var row = Row(window); Click(Generate(row)); (await service.Next()).Complete(); await Settled(row); Details(row).IsExpanded = true;
            window.Close(); Assert.False(window.IsVisible); window.Reveal(null); window.Show(); await Refresh(window); Assert.Same(row, Row(window)); Assert.True(Details(row).IsExpanded);
            Click(Generate(row)); var late = await service.Next(); window.Close(); Assert.True(late.Token.IsCancellationRequested); late.Complete(); await Settled(row); Assert.Equal(1, service.Displays);
            window.Reveal(new(seeded.Entry.Id, seeded.Entry.Senses[0].Id)); window.Show(); await Refresh(window);
            Assert.NotSame(row, Row(window)); Assert.False(Details(Row(window)).IsExpanded); Assert.Equal(2, service.Generations); Assert.Equal(2, service.Subscribers);
            // A detached button is no longer authority to affect the newly presented row.
            Click(Generate(row)); Assert.Equal(2, service.Generations);
            await window.RetireAsync(); Assert.Equal(0, service.Subscribers);
        }
        finally { service.Release(); await window.RetireAsync(); }
    });

    [Fact]
    public Task ExplicitNavigationRetiresTheOldGenerationBeforeTheAsynchronousListRefreshCanApply() => dispatcher.RunAsync(async () =>
    {
        var seeded = await Seed(); await using var store = seeded.Store; var service = new Service();
        var other = await store.CreateSenseAsync(seeded.Entry.Id); Assert.NotNull(other);
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new Text(), service); var window = Window(store, session, seeded.Entry);
        try
        {
            await Refresh(window); var old = Row(window); Click(Generate(old)); var late = await service.Next();
            var target = new VocabularySelection(seeded.Entry.Id, other.Value); window.Reveal(target);
            Assert.False(window.LastRefresh.IsCompleted); Assert.True(late.Token.IsCancellationRequested);
            Assert.Equal(target, window.Selection); Assert.DoesNotContain(old, Descendants<VocabularyExplanationRow>(window));
            late.Complete(); await old.LastGeneration.WaitAsync(Timeout); await Refresh(window);
            Assert.Equal(0, service.Displays); Assert.Equal(target, window.Selection); Assert.Equal(1, service.Generations);
        }
        finally { service.Release(); await window.RetireAsync(); }
    });

    [Fact]
    public Task RetirementDrainsAnAdmittedMeaningAdoptionAndRejectsARepeatedClick() => dispatcher.RunAsync(async () =>
    {
        var clock = new ClockGate(); var seeded = await Seed(archive: new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, clock));
        await using var store = seeded.Store; var service = new Service();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new Text(), service); var window = Window(store, session, seeded.Entry);
        try
        {
            await Refresh(window); var row = Row(window); Click(Generate(row)); (await service.Next()).Complete(); await Settled(row);
            Details(row).IsExpanded = true; window.UpdateLayout(); ulong revision = store.Snapshot.Revision; clock.Block = true;
            Click(Replace(row)); await clock.Entered.Task.WaitAsync(Timeout); Assert.False(Replace(row).IsEnabled); Click(Replace(row));
            var closing = window.RetireAsync(); Assert.False(closing.IsCompleted); Assert.False(window.IsVisible); Assert.Equal(0, service.Subscribers);
            clock.Release.TrySetResult(); await closing.WaitAsync(Timeout);
            Assert.Equal(revision + 1, store.Snapshot.Revision); Assert.Equal("generated", store.Entries[0].Examples[0].Meaning);
        }
        finally { clock.Release.TrySetResult(); service.Release(); await window.RetireAsync(); }
    });

    private sealed class ClockGate : TimeProvider
    {
        public bool Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow()
        { if (Block) { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); } return DateTimeOffset.UnixEpoch; }
    }

    [Fact]
    public Task UnconfiguredButtonOpensConfigurationAndALateReplyCannotRecreateARemovedUsage() => dispatcher.RunAsync(async () =>
    {
        var seeded = await Seed(); await using var store = seeded.Store; var service = new Service { IsConfigured = false }; int configured = 0;
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, new Provider(), new Text(), service); var window = Window(store, session, seeded.Entry, () => configured++);
        try
        {
            await Refresh(window); var row = Row(window); Click(Generate(row)); Assert.Equal(1, configured); Assert.Equal(0, service.Generations);
            service.IsConfigured = true; service.Change(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Click(Generate(row)); var late = await service.Next();
            var baseline = store.Entries[0]; Assert.True(await store.UpdateAsync(baseline with { Senses = [baseline.Senses[0] with { Examples = [] }] }, baseline)); await Refresh(window);
            Assert.Single(Descendants<VocabularyExplanationRow>(window)); Assert.True(late.Token.IsCancellationRequested); late.Complete(); await row.LastGeneration.WaitAsync(Timeout);
            Assert.Empty(store.Entries[0].Examples); Assert.Equal(0, service.Displays); Assert.Equal(1, service.Subscribers); Click(Generate(row)); Assert.Equal(1, service.Generations);
        }
        finally { service.Release(); await window.RetireAsync(); }
    });

    private static async Task Settled(VocabularyExplanationRow row)
    {
        await row.LastGeneration.WaitAsync(Timeout);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private sealed class Provider : ILearningDictionaryProviding
    { public int Calls; public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken = default) { Interlocked.Increment(ref Calls); return Task.FromResult(LearningDictionaryResult.NotFound); } }
    private sealed class Text : ILearningLexicalText
    {
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language) => throw new NotSupportedException();
        public string Key(string value) => throw new NotSupportedException(); public bool IsPatternText(string value) => throw new NotSupportedException(); public bool FirstIsUppercase(string value) => throw new NotSupportedException();
    }
    private sealed record Call(LearningExplanationInput Input, CancellationToken Token)
    {
        public TaskCompletionSource<LearningExplanationRecord> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => Reply.TrySetResult(new(Guid.NewGuid(), Input, LearningLanguage.Chinese, new("generated", "literal <b>prose</b>"), "fixture", DateTimeOffset.UnixEpoch));
    }
    private sealed class Service : ILearningExplanationGenerating
    {
        public event Action? Changed; public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public LearningLanguage Language => LearningLanguage.Chinese; public bool IsConfigured { get; set; } = true;
        public int Generations, Displays;
        private readonly Channel<Call> _calls = Channel.CreateUnbounded<Call>(); private readonly ConcurrentQueue<Call> _pending = new();
        public Task<Call> Next() => _calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        public async Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Generations); var call = new Call(input, cancellationToken); _pending.Enqueue(call); _calls.Writer.TryWrite(call);
            try { return await call.Reply.Task.ConfigureAwait(false); } finally { call.Finished.TrySetResult(); }
        }
        public void RecordDisplay(string requestId) => Interlocked.Increment(ref Displays);
        public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) => null;
        public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => Task.FromResult<LearningExplanationRecord?>(null);
        public void Release() { while (_pending.TryDequeue(out var call)) call.Complete(); }
        public void Change() => Changed?.Invoke();
    }
}
