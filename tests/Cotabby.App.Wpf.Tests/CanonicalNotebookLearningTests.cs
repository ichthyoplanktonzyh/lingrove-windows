using LearningExplanationRecord = Cotabby.Core.LanguageLearning.LearningExplanationRecord;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class CanonicalNotebookLearningTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task UnknownLanguageKeepsStableContextAndUsesCanonicalStoreAndUnknownDictionary() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Language: null, Meaning: "shore", Example: "river bank")]);
        var snapshot = await store.ReadAsync(); var entry = Assert.Single(snapshot.Document.CollectedEntries); var sense = Assert.Single(entry.Senses); var example = Assert.Single(sense.Examples);
        var service = new Service(store); var dictionary = new Dictionary(); int unknownWords = 0;
        await using var learning = new CanonicalNotebookLearningSession(Dispatcher.CurrentDispatcher, dictionary, new Text(), service, q => { unknownWords++; return []; });
        learning.Select(entry, sense.Id); Assert.Equal(0, service.Calls);
        var child = learning.Explanations[CanonicalNotebookTargetPolicy.Key(example)]; Assert.Null(child.State.Input!.Language);
        await child.ExplainAsync(); Assert.NotNull(child.State.Record); Assert.Equal(1, service.Calls);
        var saved = await store.ReadAsync(); var reopened = Assert.Single(Assert.Single(saved.Document.CollectedEntries).Senses).Examples.Single();
        Assert.Equal(example.Id, reopened.Id); Assert.Equal(example.CreatedAt, reopened.CreatedAt); Assert.Single(reopened.Explanations);
        var destination = new VocabularyExplanationDestination(entry.Id, sense.Id, example.Id);
        Assert.True((await store.AdoptExplanationAsync(child.State.Record!, destination)).Changed);
        Assert.Equal("generated", Assert.Single(Assert.Single((await store.ReadAsync()).Document.CollectedEntries).Senses).Examples.Single().Meaning);
        await learning.ShowDictionaryAsync(entry); Assert.Equal(1, dictionary.UnknownCalls); Assert.Equal(1, unknownWords);
    });

    [Fact]
    public Task RealWindowShowsIdleRowsAndExplicitDictionaryThenPersistsExplanation() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Language: null, Meaning: "shore", Example: "river bank")]);
        var entry = Assert.Single((await store.ReadAsync()).Document.CollectedEntries); var sense = Assert.Single(entry.Senses);
        var service = new Service(store); var dictionary = new Dictionary();
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader());
        window.AttachLearning(service, dictionary, new Text(), _ => [], () => { });
        try
        {
            window.Show(); await window.LastOperation!; window.Reveal(new(entry.Id, sense.Id)); await window.LastOperation!; window.UpdateLayout();
            var rows = Descendants(window).OfType<CanonicalVocabularyExplanationRow>().ToArray(); Assert.NotEmpty(rows); Assert.Equal(0, service.Calls);
            var exampleRow = rows.Single(x => x.Session.State.Input?.Context == "river bank");
            var button = Descendants(exampleRow).OfType<Button>().Single(b => Equals(b.Content, "✦"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await exampleRow.LastGeneration;
            Assert.Equal(1, service.Calls); Assert.NotNull(exampleRow.Session.State.Record);
            window.UpdateLayout(); var disclosure = Descendants(window).OfType<CanonicalVocabularyDictionarySection>().Single(); disclosure.IsExpanded = true;
            await disclosure.LastLookup; Assert.Equal(1, dictionary.UnknownCalls);
            window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetTempPath(), "canonical-notebook-learning.png"))) encoder.Save(file);
            window.Hide(); window.Show(); window.UpdateLayout();
            var reopenedDisclosure = Descendants(window).OfType<CanonicalVocabularyDictionarySection>().Single();
            Assert.Same(disclosure, reopenedDisclosure); Assert.True(reopenedDisclosure.IsExpanded);
            window.Reveal(new(entry.Id, sense.Id)); await window.LastOperation!; window.UpdateLayout();
            var nextDisclosure = Descendants(window).OfType<CanonicalVocabularyDictionarySection>().Single();
            Assert.NotSame(disclosure, nextDisclosure); Assert.False(nextDisclosure.IsExpanded);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task GraphSenseStartsLearningAndCloseRetiresGenerationAndDictionary() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var added = await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Language: null, Meaning: "shore", Example: "river bank")]);
        var service = new GatedService(); var dictionary = new Dictionary();
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader());
        window.AttachLearning(service, dictionary, new Text(), _ => [], () => { }); window.AttachGraphPreferences(new GraphPreferences());
        try
        {
            window.Show(); await window.LastOperation!;
            FindButton(window, "canonicalGraphMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.CanonicalGraphView is { IsVisible: true } graph && graph.Canvas.Positions.Count > 0);
            var canvas = window.CanonicalGraphView!.Canvas; var selection = added.Selection!.Value;
            canvas.Activate(VocabularyGraphNodeId.Headword(selection.EntryId));
            await Until(() => canvas.Positions.ContainsKey(VocabularyGraphNodeId.Sense(selection)));
            canvas.Activate(VocabularyGraphNodeId.Sense(selection)); window.UpdateLayout();
            var row = Descendants(window).OfType<CanonicalVocabularyExplanationRow>().Single(x => x.Session.State.Input?.Context == "river bank");
            Descendants(row).OfType<Button>().Single(b => Equals(b.Content, "✦")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var input = await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var dictionaryView = Descendants(window).OfType<CanonicalVocabularyDictionarySection>().Single(); dictionaryView.IsExpanded = true;
            await dictionaryView.LastLookup; Assert.Equal(1, dictionary.UnknownCalls); Assert.True(row.Session.State.IsGenerating);
            FindButton(window, "canonicalGraphCloseDetail").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(row.Session.State.IsGenerating); Assert.Null(window.Session.Selection);
            service.Release.TrySetResult(new(Guid.NewGuid(), input, LearningLanguage.Chinese, new("late", "details"), "fake", DateTimeOffset.UtcNow));
            await row.LastGeneration.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Null(row.Session.State.Record);
            window.UpdateLayout(); Assert.Empty(Descendants(window).OfType<CanonicalVocabularyDictionarySection>());
        }
        finally { service.Release.TrySetCanceled(); await window.RetireAsync(); }
    });

    [Fact]
    public Task StandaloneShowsGeneratedMeaningWithoutRepeatingAuthoredOrOfferingReplacement() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var added = await store.AddEntriesAsync([new VocabularyEntryInput(Expression: "bank", Language: null, Meaning: "authored")]);
        var service = new Service(store); var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader());
        window.AttachLearning(service, new Dictionary(), new Text(), _ => [], () => { });
        try
        {
            window.Show(); await window.LastOperation!; window.Reveal(added.Selection); await window.LastOperation!; window.UpdateLayout();
            var row = Descendants(window).OfType<CanonicalVocabularyExplanationRow>().Single();
            Assert.DoesNotContain(Descendants(row).OfType<TextBox>(), x => x.Text == "authored");
            Descendants(row).OfType<Button>().Single(b => Equals(b.Content, "✦")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await row.LastGeneration; await Until(() => { window.UpdateLayout(); return Descendants(row).OfType<TextBox>().Any(x => x.Text == "generated" && x.Visibility == Visibility.Visible); });
            Assert.Contains(Descendants(row).OfType<TextBox>(), x => x.Text == "generated" && x.Visibility == Visibility.Visible);
            Assert.DoesNotContain(Descendants(row).OfType<Button>(), x => Equals(x.Content, "Replace contextual meaning") && x.Visibility == Visibility.Visible);
        }
        finally { await window.RetireAsync(); }
    });
    private sealed class GraphPreferences : IVocabularyGraphPreferences
    {
        public string BrowserMode { get; set; } = "list"; private byte[]? _data;
        public byte[]? ReadSettings() => _data; public void WriteSettings(ReadOnlySpan<byte> data) => _data = data.ToArray();
    }
    private static Button FindButton(DependencyObject root, string name)
    { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<Button>().Single(x => x.Name == name); }
    private static async Task Until(Func<bool> condition)
    { for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [Fact]
    public Task UnrepresentableSavedDateIsReportedWithoutChangingSourceValue() => dispatcher.RunAsync(async () =>
    {
        var now = new SourceDate(0); var sense = new F.VocabularySense(Guid.NewGuid(), "shore", now, now) { IsSavedWithoutContext = true };
        var entry = new F.VocabularyEntry(Guid.NewGuid(), "bank", "bank", null, VocabularyEntryKind.Word, [sense], now, now);
        var input = CanonicalNotebookTargetPolicy.Input(entry, sense, null)!;
        var record = CanonicalWritingPolicy.FromLive(new LearningExplanationRecord(Guid.NewGuid(), input, LearningLanguage.Chinese, new("shore", "details"), "fake", DateTimeOffset.UtcNow)) with { CreatedAt = new SourceDate(1e20) };
        sense = sense with { Explanations = [record] }; entry = entry with { Senses = [sense] };
        await using var session = new CanonicalNotebookLearningSession(Dispatcher.CurrentDispatcher, new Dictionary(), new Text(), null, _ => []);
        session.Select(entry, sense.Id); Assert.True(session.HasUnavailableSavedRecord("standalone"));
        Assert.Equal(new SourceDate(1e20), entry.Senses[0].Explanations[0].CreatedAt);
    });

    [Fact]
    public Task NavigationCancelsHeldGenerationAndRejectsLateResult() => dispatcher.RunAsync(async () =>
    {
        var now = new SourceDate(0); var usage = new F.VocabularyExample(Guid.NewGuid(), "bank", "", [new(0, 4)], R.VocabularyExampleCompleteness.Sentence, "", now);
        var sense = new F.VocabularySense(Guid.NewGuid(), "shore", now, now) { Examples = [usage] };
        var entry = new F.VocabularyEntry(Guid.NewGuid(), "bank", "bank", null, VocabularyEntryKind.Word, [sense], now, now);
        var service = new GatedService(); await using var learning = new CanonicalNotebookLearningSession(Dispatcher.CurrentDispatcher, new Dictionary(), new Text(), service, _ => []);
        learning.Select(entry, sense.Id); var child = learning.Explanations[CanonicalNotebookTargetPolicy.Key(usage)];
        var work = child.ExplainAsync(); var input = await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        learning.Select(null); Assert.False(child.State.IsGenerating);
        service.Release.TrySetResult(new(Guid.NewGuid(), input, LearningLanguage.Chinese, new("late", "details"), "fake", DateTimeOffset.UtcNow));
        await work.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Empty(learning.Explanations); Assert.Null(child.State.Record);
    });

    [Fact]
    public Task CreatedWithoutContextHasIndependentStandaloneAndUsageChildren() => dispatcher.RunAsync(async () =>
    {
        var now = new SourceDate(0); var usage = new F.VocabularyExample(Guid.NewGuid(), "bank", "", [new(0, 4)], R.VocabularyExampleCompleteness.Unknown, "", now);
        var sense = new F.VocabularySense(Guid.NewGuid(), "shore", now, now) { CreatedWithoutContext = true, Examples = [usage] };
        var entry = new F.VocabularyEntry(Guid.NewGuid(), "bank", "bank", null, VocabularyEntryKind.Word, [sense], now, now);
        var dictionary = new Dictionary(); await using var learning = new CanonicalNotebookLearningSession(Dispatcher.CurrentDispatcher, dictionary, new Text(), null, _ => []);
        learning.Select(entry, sense.Id); Assert.Equal(2, learning.Explanations.Count);
        Assert.Equal("sense:" + sense.Id.ToString("D").ToUpperInvariant(), learning.Explanations["standalone"].State.Input!.OccurrenceIdentity);
        Assert.Equal(LearningExplanationContext.Unknown, learning.Explanations[CanonicalNotebookTargetPolicy.Key(usage)].State.Input!.ContextKind);
        var old = learning.Explanations[CanonicalNotebookTargetPolicy.Key(usage)]; learning.Select(entry, sense.Id); Assert.Same(old, learning.Explanations[CanonicalNotebookTargetPolicy.Key(usage)]);
    });
    private sealed class GatedService : ILearningExplanationGenerating
    {
        public event Action? Changed { add { } remove { } }
        public LearningLanguage Language => LearningLanguage.Chinese; public bool IsConfigured => true;
        public TaskCompletionSource<LearningExplanationInput> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<LearningExplanationRecord> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
        { Entered.TrySetResult(input); return Release.Task; }
        public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => Task.FromResult<LearningExplanationRecord?>(null);
        public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) => null; public void RecordDisplay(string requestId) { }
    }
    private sealed class Service(ICanonicalVocabularyStore store) : ILearningExplanationGenerating
    {
        public event Action? Changed { add { } remove { } }
        public int Calls; public LearningLanguage Language => LearningLanguage.Chinese; public bool IsConfigured => true;
        public async Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
        { Calls++; var result = new LearningExplanationRecord(Guid.NewGuid(), input, Language, new("generated", "details"), "fake", DateTimeOffset.UtcNow); await store.SaveExplanationAsync(result, cancellationToken); return result; }
        public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => store.FindExplanationAsync(input, Language);
        public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) => null;
        public void RecordDisplay(string requestId) { }
    }
    private sealed class Dictionary : ILearningDictionaryProviding, ILearningUnknownDictionaryProviding
    {
        public int UnknownCalls;
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken token = default) => throw new InvalidOperationException();
        public Task<LearningDictionaryResult> LookupUnknownAsync(string query, bool expression, CancellationToken token = default) { UnknownCalls++; return Task.FromResult(LearningDictionaryResult.NotFound); }
    }
    private sealed class Text : ILearningLexicalText
    {
        public string Key(string value) => value; public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language) => [];
        public bool IsPatternText(string value) => true; public bool FirstIsUppercase(string value) => false;
    }
}
