using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Text;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Real notebook disclosure, shared definition renderer, session, memory archive and native Windows
/// ICU. Only the dictionary provider and explicit scheduling/failure gates are controlled. These
/// tests neither open external links nor construct AppHost, a model, clipboard or editor.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyDictionaryViewTests(WpfDispatcher dispatcher)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static async Task<VocabularyStore> Seed(bool expression = false)
    {
        var store = new VocabularyStore(new VocabularyArchive((string?)null), new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Assert.True(await store.SaveAsync(expression
            ? new("keep me posted", "keep someone posted", LearningLanguage.English, VocabularyEntryKind.Pattern)
            : new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word)));
        return store;
    }
    private static VocabularyNotebookWindow Open(VocabularyStore store, VocabularyNotebookSession session)
    {
        var entry = store.Entries[0]; var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch(), session);
        window.Reveal(new(entry.Id, entry.Senses[0].Id)); window.Show(); return window;
    }
    private static async Task Refresh(VocabularyNotebookWindow window)
    { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; window.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found; foreach (var value in Descendants<T>(child)) yield return value; }
    }
    private static VocabularyDictionarySection Section(VocabularyNotebookWindow window) => Assert.Single(Descendants<VocabularyDictionarySection>(window));
    private static async Task Lookup(VocabularyDictionarySection section)
    { await section.LastLookup.WaitAsync(Timeout); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); section.UpdateLayout(); }
    private static Button Word(VocabularyDictionarySection section, string text) => Descendants<Button>(section).Single(button => Equals(button.Content, text));

    [Fact]
    public Task BrowsingDoesNotQueryAndExpandedDefinitionsAreLiteralAttributedAndBounded() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(); var provider = new Provider(); var service = new Service();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new IcuLearningLexicalText(), service); var window = Open(store, session);
        try
        {
            await Refresh(window); var section = Section(window); Assert.False(section.IsExpanded); Assert.Equal(0, provider.Calls); Assert.Equal(0, service.Generations);
            section.IsExpanded = true; await Lookup(section); window.UpdateLayout(); Assert.Equal(1, provider.Calls); Assert.Equal("bank", session.Dictionary.State.Query);
            Assert.Contains(Descendants<TextBlock>(section), block => block.Text == "1. <b>literal definition</b>");
            Assert.Contains(Descendants<TextBlock>(section), block => block.Text == "native fixture example");
            Assert.Contains(Descendants<TextBlock>(section).SelectMany(block => block.Inlines.OfType<Hyperlink>()), link => new TextRange(link.ContentStart, link.ContentEnd).Text == "Fixture source");
            Assert.InRange(Assert.Single(Descendants<ScrollViewer>(section)).Height, 120, 300);
            section.IsExpanded = false; Assert.Null(session.Dictionary.State.Unit); Assert.Equal(0, service.Generations);
            section.IsExpanded = true; await Lookup(section); Assert.Equal(2, provider.Calls); Assert.Equal(0, service.Generations);
        }
        finally { await window.RetireAsync(); }
    });

    [Fact]
    public Task ExpressionWordsAndBackUseTheRealSessionCacheAndDetachedButtonsCannotQueryAfterCollapse() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(expression: true); var provider = new Provider();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new IcuLearningLexicalText()); var window = Open(store, session);
        try
        {
            await Refresh(window); var section = Section(window); section.IsExpanded = true; await Lookup(section);
            Assert.Equal(LearningUnitKind.Expression, session.Dictionary.State.Unit!.Kind); var word = Word(section, "keep");
            Click(word); await section.LastNavigation.WaitAsync(Timeout); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Assert.Equal("keep", session.Dictionary.State.Query); Assert.Equal(2, provider.Calls);
            Click(Word(section, "Back to expression")); await section.LastNavigation.WaitAsync(Timeout); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("keep someone posted", session.Dictionary.State.Query); Assert.Equal(2, provider.Calls);
            section.IsExpanded = false; Click(word); await section.LastNavigation; Assert.Equal(2, provider.Calls); Assert.Null(session.Dictionary.State.Unit);
        }
        finally { await window.RetireAsync(); }
    });

    [Fact]
    public Task SameSenseStoreChangesKeepExpansionAndOnlyAnEditedFormRequeriesTheDictionary() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(); var provider = new Provider();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new IcuLearningLexicalText()); var window = Open(store, session);
        try
        {
            await Refresh(window); var section = Section(window); section.IsExpanded = true; await Lookup(section);
            var original = store.Entries[0]; Assert.True(await store.UpdateAsync(original with { Senses = [original.Senses[0] with { Note = "new note" }] }, original)); await Refresh(window);
            Assert.Same(section, Section(window)); Assert.True(section.IsExpanded); Assert.Equal(1, provider.Calls);
            original = store.Entries[0]; Assert.True(await store.UpdateAsync(original with { DictionaryForm = "riverbank" }, original)); await Refresh(window); await Lookup(section);
            Assert.Same(section, Section(window)); Assert.True(section.IsExpanded); Assert.Equal("riverbank", session.Dictionary.State.Query); Assert.Equal(2, provider.Calls);
        }
        finally { await window.RetireAsync(); }
    });

    [Fact]
    public Task CollapseRejectsALateProviderWhileOrdinaryReopenResumesOnlyTheExplicitExpandedDictionary() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<LearningDictionaryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider { Lookup = (_, _) => { entered.TrySetResult(); return pending.Task; } }; var service = new Service();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new IcuLearningLexicalText(), service); var window = Open(store, session);
        try
        {
            await Refresh(window); var section = Section(window); section.IsExpanded = true; var old = section.LastLookup; await entered.Task.WaitAsync(Timeout);
            section.IsExpanded = false; Assert.Null(session.Dictionary.State.Unit);
            provider.Lookup = (query, language) => Task.FromResult(Result(query, language)); section.IsExpanded = true; await Lookup(section);
            var accepted = session.Dictionary.State; pending.TrySetResult(Result("late headword", LearningLanguage.English)); await old.WaitAsync(Timeout); Assert.Same(accepted, session.Dictionary.State);
            window.Close(); Assert.Null(session.Dictionary.State.Unit); Assert.True(section.IsExpanded);
            window.Reveal(null); window.Show(); await Refresh(window); await Lookup(section); Assert.Same(section, Section(window)); Assert.Equal(3, provider.Calls); Assert.True(section.IsExpanded);
            var entry = store.Entries[0]; window.Reveal(new(entry.Id, entry.Senses[0].Id)); await Refresh(window);
            Assert.NotSame(section, Section(window)); Assert.False(Section(window).IsExpanded); Assert.Null(session.Dictionary.State.Unit); Assert.Equal(3, provider.Calls); Assert.Equal(0, service.Generations);
        }
        finally { pending.TrySetResult(Result("late", LearningLanguage.English)); await window.RetireAsync(); }
    });

    [Fact]
    public Task ExplicitShowRetiresEvenAnUncooperativePendingNativeWordPreparationBeforeTheNewCollapsedSectionAppears() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var text = new TextGate { Prepare = (_, _) => { entered.Set(); Assert.True(release.Wait(Timeout)); return []; } }; var provider = new Provider();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, text); var window = Open(store, session);
        Task? old = null;
        try
        {
            await Refresh(window); var section = Section(window); section.IsExpanded = true; old = section.LastLookup; Assert.True(entered.Wait(Timeout));
            var entry = store.Entries[0]; window.Reveal(new(entry.Id, entry.Senses[0].Id)); Assert.False(Section(window).IsExpanded);
            release.Set(); await old.WaitAsync(Timeout); await Refresh(window); Assert.Null(session.Dictionary.State.Unit); Assert.Equal(0, provider.Calls);
            window.Close(); window.Reveal(null); window.Show(); await Refresh(window); Assert.Equal(0, provider.Calls);
        }
        finally { release.Set(); if (old is not null) await old.WaitAsync(Timeout); await window.RetireAsync(); }
    });

    [Fact]
    public Task EditingAnEmptyFormExpressionDuringNativePreparationRequeriesTheExpandedSectionWithoutKeepingAnIdleSpinner() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(); var baseline = store.Entries[0]; Assert.True(await store.UpdateAsync(baseline with { DictionaryForm = "" }, baseline));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var native = new IcuLearningLexicalText();
        var text = new TextGate { Prepare = (query, language) => { if (query == "bank") { entered.Set(); Assert.True(release.Wait(Timeout)); } return native.Words(query, language); } };
        var provider = new Provider(); var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, text); var window = Open(store, session); Task? old = null;
        try
        {
            await Refresh(window); var section = Section(window); section.IsExpanded = true; old = section.LastLookup; Assert.True(entered.Wait(Timeout));
            baseline = store.Entries[0]; Assert.True(await store.UpdateAsync(baseline with { Expression = "coffee" }, baseline)); await Refresh(window); await Lookup(section);
            Assert.Same(section, Section(window)); Assert.True(section.IsExpanded); Assert.Equal("coffee", session.Dictionary.State.Query); Assert.Equal(1, provider.Calls);
            release.Set(); await old.WaitAsync(Timeout); Assert.Equal("coffee", session.Dictionary.State.Query); Assert.False(session.IsPreparingDictionary);
            Assert.DoesNotContain(Descendants<TextBlock>(section), block => block.IsVisible && block.Text == "Looking up…");
        }
        finally { release.Set(); if (old is not null) await old.WaitAsync(Timeout); await window.RetireAsync(); }
    });

    [Fact]
    public Task OrdinaryReopenUsesTheLatestExpandedEntryAfterAnAcknowledgedEditWhileHidden() => HiddenEdit("form");
    [Fact]
    public Task OrdinaryReopenUsesTheLatestEmptyFormExpressionAfterAnAcknowledgedEditWhileHidden() => HiddenEdit("expression");
    private Task HiddenEdit(string mode) => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(); var baseline = store.Entries[0];
        if (mode == "expression") Assert.True(await store.UpdateAsync(baseline with { DictionaryForm = "" }, baseline));
        var provider = new Provider(); var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, new IcuLearningLexicalText()); var window = Open(store, session);
        try
        {
            await Refresh(window); var section = Section(window); section.IsExpanded = true; await Lookup(section); window.Close();
            baseline = store.Entries[0]; Assert.True(await store.UpdateAsync(mode == "form" ? baseline with { DictionaryForm = "riverbank" } : baseline with { Expression = "coffee" }, baseline));
            await Refresh(window); Assert.Equal(1, provider.Calls); Assert.True(section.IsExpanded);
            window.Reveal(null); window.Show(); await Refresh(window); await Lookup(section);
            Assert.Same(section, Section(window)); Assert.True(section.IsExpanded); Assert.Equal(mode == "form" ? "riverbank" : "coffee", session.Dictionary.State.Query); Assert.Equal(2, provider.Calls);
        }
        finally { await window.RetireAsync(); }
    });

    [Fact]
    public Task WordPreparationFailureIsFixedAndCollapseReexpandAllowsAnExplicitRetry() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Seed(); var text = new TextGate { Prepare = (_, _) => throw new IOException("private provider path") }; var provider = new Provider();
        var session = new VocabularyNotebookSession(Dispatcher.CurrentDispatcher, provider, text); var window = Open(store, session);
        try
        {
            await Refresh(window); var section = Section(window); section.IsExpanded = true; await Lookup(section);
            Assert.Contains(Descendants<TextBlock>(section), block => block.Text == "Lexical text processing is unavailable on this system."); Assert.Equal(0, provider.Calls);
            section.IsExpanded = false; text.Prepare = null; section.IsExpanded = true; await Lookup(section); Assert.Null(session.DictionaryError); Assert.Equal(1, provider.Calls);
        }
        finally { await window.RetireAsync(); }
    });

    private sealed class TextGate : ILearningLexicalText
    {
        private readonly IcuLearningLexicalText _native = new();
        public Func<string, LearningLanguage, IReadOnlyList<LearningDictionaryWord>>? Prepare;
        public IReadOnlyList<LearningDictionaryWord> Words(string text, LearningLanguage language) => Prepare?.Invoke(text, language) ?? _native.Words(text, language);
        public string Key(string value) => _native.Key(value); public bool IsPatternText(string value) => _native.IsPatternText(value); public bool FirstIsUppercase(string value) => _native.FirstIsUppercase(value);
    }
    private static LearningDictionaryResult Result(string query, LearningLanguage language) => new LearningDictionaryResult.Entries(
        [new("fixture", language.Code(), query, [], [new("noun", ["<b>literal definition</b>"], [])], "https://example.com/definition", examples: ["native fixture example"], sourceName: "Fixture source", licenseUrl: "https://creativecommons.org/licenses/by-sa/4.0/", licenseName: "Fixture license")]);
    private sealed class Provider : ILearningDictionaryProviding
    {
        private readonly ConcurrentQueue<string> _calls = new(); public int Calls => _calls.Count;
        public Func<string, LearningLanguage, Task<LearningDictionaryResult>>? Lookup;
        public Task<LearningDictionaryResult> LookupAsync(string query, LearningLanguage language, bool expression, CancellationToken cancellationToken = default)
        { _calls.Enqueue(query); return Lookup?.Invoke(query, language) ?? Task.FromResult(Result(query, language)); }
    }
    private sealed class Service : ILearningExplanationGenerating
    {
        public event Action? Changed { add { } remove { } }
        public LearningLanguage Language => LearningLanguage.Chinese; public bool IsConfigured => true; public int Generations;
        public Task<LearningExplanationRecord> GenerateAsync(LearningExplanationInput input, string requestId, bool refresh = false, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Generations); throw new InvalidOperationException("Dictionary must not invoke explanation"); }
        public LearningExplanationRecord? SavedRecord(LearningExplanationInput input) => null;
        public Task<LearningExplanationRecord?> LoadSavedAsync(LearningExplanationInput input) => Task.FromResult<LearningExplanationRecord?>(null);
        public void RecordDisplay(string requestId) { }
    }
}
