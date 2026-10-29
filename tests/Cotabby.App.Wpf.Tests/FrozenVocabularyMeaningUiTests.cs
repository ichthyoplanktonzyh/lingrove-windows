using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Win32.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyMeaningUiTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task RetiringNotebookClosesPatternConfirmationWithoutDeleting() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var added = await store.AddEntriesAsync([new(Expression: "hello")]); var before = await store.ReadAsync(); var pattern = new R.VocabularyUsagePattern(added.Selection!.Value.SenseId, "hello there"); await store.UpdateSemanticsAsync(before.Document.Semantics with { Patterns = [pattern] }, before.Document.Semantics);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; window.Reveal(added.Selection); await window.LastOperation!; var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () => { try { Assert.Single(Application.Current.Windows.OfType<FrozenVocabularyPatternDeleteWindow>()); await window.RetireAsync(); completed.SetResult(); } catch (Exception error) { completed.SetException(error); } });
        Find<Button>(window, "canonicalRemovePattern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Single((await store.ReadAsync()).Document.Semantics.Patterns);
    });
    [Fact]
    public Task LatestMeaningRetirementClosesNestedDraftWithoutPublishing() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var before = await store.ReadAsync(); var form = new FrozenVocabularyMeaningEditorWindow(store, before.Document.Semantics, [], classification: before.Document.Classification, entries: []); form.Show(); Find<TextBox>(form, "canonicalMeaningName").Text = "draft";
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _ = form.Dispatcher.BeginInvoke(async () => { try { Assert.Single(Application.Current.Windows.OfType<FrozenVocabularyDraftPickerWindow>()); await form.RetireAsync(); done.SetResult(); } catch (Exception error) { done.SetException(error); } });
        Find<Button>(form, "meaningDraftCategories").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(form.IsVisible); Assert.Equal(before, await store.ReadAsync());
    });
    [Fact]
    public Task LatestMeaningDraftCreatesCategoryAndSenseInOneAcknowledgedSave() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var added = await store.AddEntriesAsync([new(Expression: "hello", Meaning: "greeting")]); var before = await store.ReadAsync();
        var form = new FrozenVocabularyMeaningEditorWindow(store, before.Document.Semantics, [LearningLanguage.English], classification: before.Document.Classification, entries: before.Document.CollectedEntries); form.Show(); Find<TextBox>(form, "canonicalMeaningName").Text = "Greeting";
        _ = form.Dispatcher.BeginInvoke(() => { var picker = Application.Current.Windows.OfType<FrozenVocabularyDraftPickerWindow>().Single(); VocabularyThemeRender.Capture(picker, "latest-category-picker", 600, 560);
            _ = picker.Dispatcher.BeginInvoke(() => { var editor = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(editor, "categoryName").Text = "Social"; Find<Button>(editor, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
            Find<Button>(picker, "meaningDraftNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            _ = picker.Dispatcher.BeginInvoke(() => Find<Button>(picker, "meaningDraftPickerSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        });
        Find<Button>(form, "meaningDraftCategories").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => form.IsEnabled && Find<Button>(form, "meaningDraftExpressions").IsEnabled);
        Assert.Empty((await store.ReadAsync()).Document.Classification.Categories);
        _ = form.Dispatcher.BeginInvoke(() => { var picker = Application.Current.Windows.OfType<FrozenVocabularyDraftPickerWindow>().Single(); var checkbox = Descendants(picker).OfType<CheckBox>().Single(); checkbox.IsChecked = true; checkbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Find<Button>(picker, "meaningDraftPickerSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Find<Button>(form, "meaningDraftExpressions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => Find<Button>(form, "meaningDraftExpressions").IsEnabled); VocabularyThemeRender.Capture(form, "latest-meaning-draft", 600, 520);
        Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync(); var result = await store.ReadAsync();
        Assert.Equal(before.Revision + 1, result.Revision); var node = Assert.Single(result.Document.Semantics.Nodes); var category = Assert.Single(result.Document.Classification.Categories); Assert.Equal(node.Id, Assert.Single(result.Document.Classification.Memberships).NodeId); Assert.Equal(category.Id, result.Document.Classification.Memberships.Single().CategoryId); Assert.Equal(added.Selection!.Value.SenseId, Assert.Single(result.Document.Semantics.Mappings).SenseId);
    });
    [Fact]
    public Task UsagePatternCreateEditConflictAndRemoveThroughNotebook() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var added = await store.AddEntriesAsync([new(Expression: "hello", Example: "hello there")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; window.Reveal(added.Selection); await window.LastOperation!;
        _ = window.Dispatcher.BeginInvoke(() => { var form = Application.Current.Windows.OfType<FrozenVocabularyUsagePatternEditorWindow>().Single(); Find<TextBox>(form, "patternTemplate").Text = "hello {person}"; Find<TextBox>(form, "patternNote").Text = "Greeting"; Find<Expander>(form, "patternDetails").IsExpanded = true; Find<TextBox>(form, "patternCollocations").Text = " friend \n\n colleague "; VocabularyThemeRender.Capture(form, "usage-pattern-create", 600, 520); Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Find<Button>(window, "canonicalAddPattern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        var baseline = (await store.ReadAsync()).Document.Semantics; var pattern = baseline.Patterns.Single(); Assert.Equal(new[] { "friend", "colleague" }, pattern.Collocations); Assert.Equal("Greeting", pattern.Note);
        var form = new FrozenVocabularyUsagePatternEditorWindow(store, baseline, pattern.SenseId, pattern); form.Show(); Find<TextBox>(form, "patternNote").Text = "Draft";
        await store.UpdateSemanticsAsync(baseline with { Patterns = [pattern with { Region = "Concurrent" }] }, baseline); Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!;
        Assert.True(form.IsVisible); Assert.Equal("Draft", Find<TextBox>(form, "patternNote").Text); Assert.NotEmpty(Find<TextBlock>(form, "semanticFormError").Text); form.Close(); await form.RetireAsync();
        await window.Session.LoadAsync(); var current = (await store.ReadAsync()).Document.Semantics; pattern = current.Patterns.Single();
        _ = window.Dispatcher.BeginInvoke(() => Find<Button>(Application.Current.Windows.OfType<FrozenVocabularyPatternDeleteWindow>().Single(), "patternDeleteCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Find<Button>(window, "canonicalRemovePattern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; Assert.Single((await store.ReadAsync()).Document.Semantics.Patterns);
        _ = window.Dispatcher.BeginInvoke(() => Find<Button>(Application.Current.Windows.OfType<FrozenVocabularyPatternDeleteWindow>().Single(), "patternDeleteConfirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Find<Button>(window, "canonicalRemovePattern").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; Assert.Empty((await store.ReadAsync()).Document.Semantics.Patterns); window.Close();
    });
    [Fact]
    public Task PatternEditPreservesSlotsProvenanceAndRestrictedMapping() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var added = await store.AddEntriesAsync([new(Expression: "hello", Example: "hello there")]); var initial = await store.ReadAsync();
        var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Greeting")]); var pattern = new R.VocabularyUsagePattern(added.Selection!.Value.SenseId, "hello {person}") { Slots = [new R.VocabularyPatternSlot("person")], Provenance = new R.VocabularySemanticProvenance { Origin = R.VocabularySemanticOrigin.Model, Status = R.VocabularySemanticStatus.Proposed } };
        var mapping = new R.VocabularyMeaningMapping(node.Id, pattern.SenseId) { PatternId = pattern.Id, EvidenceIds = [initial.Document.Entries.Single().Senses.Single().Examples.Single().Id] };
        await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [node], Patterns = [pattern], Mappings = [mapping] }, initial.Document.Semantics); var baseline = (await store.ReadAsync()).Document.Semantics;
        var form = new FrozenVocabularyUsagePatternEditorWindow(store, baseline, pattern.SenseId, pattern); form.Show(); Assert.True(Find<TextBox>(form, "patternTemplate").IsReadOnly); Find<TextBox>(form, "patternNote").Text = "revised"; Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync();
        var saved = (await store.ReadAsync()).Document.Semantics; Assert.Equal(pattern.Id, saved.Patterns.Single().Id); Assert.Equal(pattern.Slots, saved.Patterns.Single().Slots); Assert.Equal(pattern.Provenance, saved.Patterns.Single().Provenance); Assert.Equal(mapping, saved.Mappings.Single());
        var removed = VocabularySemanticFormPolicy.RemovePattern(saved, pattern.Id); Assert.Empty(removed.Patterns); Assert.Empty(removed.Mappings); Assert.Single(removed.Nodes);
        await store.UpdateSemanticsAsync(removed, saved); var after = await store.ReadAsync(); Assert.Empty(after.Document.Semantics.Mappings); Assert.Single(after.Document.Entries); Assert.Equal(mapping.EvidenceIds.Single(), after.Document.Entries.Single().Senses.Single().Examples.Single().Id);
    });
    [Fact]
    public Task PatternAcceptedSaveDrainsAndCancelRetainsBaseline() => dispatcher.RunAsync(async () =>
    {
        var held = new HeldStore(); var baseline = new VocabularySemanticIndex(); var form = new FrozenVocabularyUsagePatternEditorWindow(held, baseline, Guid.NewGuid()); form.Show();
        Find<TextBox>(form, "patternTemplate").Text = "draft"; Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); form.Close(); Assert.True(form.IsVisible);
        var retire = form.RetireAsync(); Assert.False(retire.IsCompleted); held.Gate.SetResult(new(1, new())); await retire; Assert.True(form.Saved); Assert.False(form.IsVisible);
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var before = await store.ReadAsync(); var cancelled = new FrozenVocabularyUsagePatternEditorWindow(store, before.Document.Semantics, Guid.NewGuid()); cancelled.Show(); Find<TextBox>(cancelled, "patternTemplate").Text = "discard"; cancelled.Close(); await cancelled.RetireAsync(); Assert.Equal(before.Document.Semantics, (await store.ReadAsync()).Document.Semantics);
    });
    [Theory]
    [InlineData("uncited", true)]
    [InlineData("cited", true)]
    [InlineData("restricted", false)]
    [InlineData("uncollected-evidence", false)]
    public Task MeaningCardShowsOnlySourceAllowedExample(string scenario, bool shown) => dispatcher.RunAsync(async () =>
    {
        var time = new SourceDate(100); var plan = VocabularyEntryImportPolicy.Plan([new(Expression: "bank", Meaning: "shore", Example: "river bank"), new(Expression: "bank", Meaning: "shore", Example: "bank outside")], new(), time);
        var doc = plan.Document; var entry = doc.Entries.Single(); var sense = entry.Senses.Single(); Assert.Equal(2, sense.Examples.Count); var visible = sense.Examples[0]; var hidden = sense.Examples[1];
        var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("River")]); var pattern = new R.VocabularyUsagePattern(sense.Id, "bank pattern");
        var mapping = new R.VocabularyMeaningMapping(node.Id, sense.Id) { PatternId = scenario == "restricted" ? pattern.Id : null,
            EvidenceIds = scenario == "cited" ? [visible.Id] : scenario == "uncollected-evidence" ? [hidden.Id] : [] };
        doc = doc with { Semantics = new() { Nodes = [node], Mappings = [mapping], Patterns = scenario == "restricted" ? [pattern] : [] },
            Collections = doc.Collections.Select(c => c.SenseId == sense.Id ? c with { InterpretationIds = [visible.Id] } : c).ToArray() };
        var db = FrozenVocabularySQLiteDatabase.CreateMemory(); db.Write(doc, new(), 0); await using var store = new FrozenVocabularyImportStore(db, () => time);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!;
        window.Session.MeaningMode = true; window.Session.Destination = new(VocabularyMeaningDestinationKind.Node, node.Id); window.Session.Refresh(); window.UpdateLayout();
        Assert.Equal(shown, Descendants(window).OfType<TextBox>().Any(t => t.IsReadOnly && t.Text == visible.Text)); Assert.Equal(mapping.EvidenceIds, (await store.ReadAsync()).Document.Semantics.Mappings.Single().EvidenceIds); window.Close();
    });
    private sealed class HeldStore : ICanonicalVocabularySemanticStore
    {
        internal readonly TaskCompletionSource<FrozenVocabularyImportSnapshot> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); internal bool Fail;
        public Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(VocabularySemanticIndex edited, VocabularySemanticIndex baseline, CancellationToken token = default) => Fail ? Task.FromException<FrozenVocabularyImportSnapshot>(new VocabularyException(VocabularyFailure.EditConflict)) : Gate.Task;
        public Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(R.VocabularyClassificationIndex edited, R.VocabularyClassificationIndex baseline, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class HeldOwner(IVocabularyEntryImportStore inner, ICanonicalVocabularySemanticStore semantic) : IVocabularyEntryImportStore, ICanonicalVocabularySemanticStore
    {
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously), Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); internal int Unsubscribed;
        public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) => inner.ReadAsync(token);
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => inner.PreviewWordListAsync(table, config, token);
        public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default) => inner.AddEntriesAsync(inputs, token);
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) => new Subscription(inner.Subscribe(changed), this);
        private sealed class Subscription(IDisposable inner, HeldOwner owner) : IDisposable { public void Dispose() { inner.Dispose(); owner.Unsubscribed++; } }
        public async Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(VocabularySemanticIndex edited, VocabularySemanticIndex baseline, CancellationToken token = default) { var result = await semantic.UpdateSemanticsAsync(edited, baseline, token); Written.TrySetResult(); await Gate.Task; return result; }
        public Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(R.VocabularyClassificationIndex edited, R.VocabularyClassificationIndex baseline, CancellationToken token = default) => semantic.UpdateClassificationAsync(edited, baseline, token);
    }
    [Fact]
    public Task LinkedNewMeaningAddMappingAndDeleteKeepExpressions() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory());
        var first = await store.AddEntriesAsync([new(Expression: "hello", Meaning: "greeting")]); var second = await store.AddEntriesAsync([new(Expression: "hi", Meaning: "greeting")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), [LearningLanguage.French]); window.Show(); await window.LastOperation!; window.Reveal(first.Selection); await window.LastOperation!;
        _ = window.Dispatcher.BeginInvoke(() => { var form = Application.Current.Windows.OfType<FrozenVocabularyMeaningEditorWindow>().Single(); Find<TextBox>(form, "canonicalMeaningName").Text = "Salutation"; Find<ComboBox>(form, "canonicalMeaningKind").SelectedIndex = 1; window.SetPreferredLanguages([LearningLanguage.English]); VocabularyThemeRender.Capture(form, "meaning-create", 600, 520); Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Find<Button>(window, "canonicalNewMeaning").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        var created = await store.ReadAsync(); var node = created.Document.Semantics.Nodes.Single(); Assert.Equal("Salutation", node.Labels.Single().Text); Assert.Equal(LearningLanguage.French, node.Labels.Single().Language); Assert.Equal(R.VocabularyMeaningNodeKind.Function, node.Kind); Assert.Equal(first.Selection!.Value.SenseId, created.Document.Semantics.Mappings.Single().SenseId); Assert.Equal(node.Id, window.Session.Destination!.NodeId);
        _ = window.Dispatcher.BeginInvoke(() => { var form = Application.Current.Windows.OfType<FrozenVocabularyMappingEditorWindow>().Single(); _ = form.Dispatcher.BeginInvoke(() => { var picker = Application.Current.Windows.OfType<FrozenVocabularyDraftPickerWindow>().Single(); var search = Find<TextBox>(picker, "meaningDraftPickerSearch"); search.Text = "hi"; picker.UpdateLayout(); var check = Descendants(picker).OfType<CheckBox>().Single(); check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Find<Button>(picker, "meaningDraftPickerSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }); Find<Button>(form, "mappingChooseExpression").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); _ = form.Dispatcher.BeginInvoke(() => { Find<TextBox>(form, "canonicalMappingNuance").Text = "informal"; VocabularyThemeRender.Capture(form, "meaning-mapping", 600, 520); Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }); });
        Find<Button>(window, "canonicalAddMapping").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!;
        var mapped = await store.ReadAsync(); Assert.Equal(2, mapped.Document.Semantics.Mappings.Count); Assert.Contains(mapped.Document.Semantics.Mappings, m => m.SenseId == second.Selection!.Value.SenseId && m.Nuance == "informal" && m.Relation == R.VocabularyMappingRelation.RealizesFunction);
        var category = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("People")]);
        await store.UpdateClassificationAsync(mapped.Document.Classification with { Categories = [category], Memberships = [new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, category.Id)] }, mapped.Document.Classification);
        _ = window.Dispatcher.BeginInvoke(() => Find<Button>(Application.Current.Windows.OfType<FrozenVocabularySemanticDeleteWindow>().Single(), "semanticDeleteCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Find<Button>(window, "canonicalDeleteMeaning").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; Assert.Single((await store.ReadAsync()).Document.Semantics.Nodes);
        _ = window.Dispatcher.BeginInvoke(() => { var dialog = Application.Current.Windows.OfType<FrozenVocabularySemanticDeleteWindow>().Single(); VocabularyThemeRender.Capture(dialog, "meaning-delete", 440, (int)Math.Ceiling(dialog.ActualHeight)); Find<Button>(dialog, "semanticDeleteConfirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Find<Button>(window, "canonicalDeleteMeaning").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; var deleted = await store.ReadAsync(); Assert.Empty(deleted.Document.Semantics.Nodes); Assert.Empty(deleted.Document.Semantics.Mappings); Assert.Equal(2, deleted.Document.CollectedEntries.Count); Assert.Single(deleted.Document.Classification.Categories); Assert.Empty(deleted.Document.Classification.Memberships); window.Close();
    });
    [Fact]
    public Task EditingFallbackLabelKeepsIdLanguageAndCancelIsInert() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync();
        var english = new R.VocabularyMeaningLabel("Hello") { Language = LearningLanguage.English }; var french = new R.VocabularyMeaningLabel("Bonjour") { Language = LearningLanguage.French };
        var original = new VocabularyMeaningNode(Guid.NewGuid(), [english, french]) { CreatedAt = new SourceDate(1e20), UpdatedAt = new SourceDate(-1e20), ScopeNote = "preserve" };
        await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [original] }, initial.Document.Semantics); var baseline = (await store.ReadAsync()).Document.Semantics;
        var cancel = new FrozenVocabularyMeaningEditorWindow(store, baseline, [LearningLanguage.Chinese], original); cancel.Show(); Find<TextBox>(cancel, "canonicalMeaningName").Text = "discard"; cancel.Close(); await cancel.RetireAsync(); Assert.Equal(baseline, (await store.ReadAsync()).Document.Semantics);
        var form = new FrozenVocabularyMeaningEditorWindow(store, baseline, [LearningLanguage.Chinese], original); form.Show(); Find<TextBox>(form, "canonicalMeaningName").Text = "Welcome"; Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync();
        var node = (await store.ReadAsync()).Document.Semantics.Nodes.Single(); Assert.Equal(english.Id, node.Labels[0].Id); Assert.Equal(LearningLanguage.English, node.Labels[0].Language); Assert.Equal("Welcome", node.Labels[0].Text); Assert.Equal(french, node.Labels[1]); Assert.Equal(original.CreatedAt, node.CreatedAt); Assert.Equal("preserve", node.ScopeNote);
    });
    [Fact]
    public Task FailureKeepsDraftAndAcceptedSaveDrains() => dispatcher.RunAsync(async () =>
    {
        var held = new HeldStore { Fail = true }; var baseline = new VocabularySemanticIndex(); var form = new FrozenVocabularyMeaningEditorWindow(held, baseline, []); form.Show(); Find<TextBox>(form, "canonicalMeaningName").Text = "draft";
        Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; Assert.True(form.IsVisible); Assert.Equal("draft", Find<TextBox>(form, "canonicalMeaningName").Text); Assert.Contains("changed while", Find<TextBlock>(form, "semanticFormError").Text); VocabularyThemeRender.Capture(form, "meaning-error", 600, 520);
        held.Fail = false; Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); form.Close(); Assert.True(form.IsVisible); var retire = form.RetireAsync(); Assert.False(retire.IsCompleted); held.Gate.SetResult(new(1, new())); await retire; Assert.True(form.Saved); Assert.False(form.IsVisible);
    });
    [Fact]
    public Task MappingEditRetainsEvidenceAndPatternThenExplicitlyConfirmsAndRemoves() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var receipt = await store.AddEntriesAsync([new(Expression: "hello", Example: "hello there")]);
        var initial = await store.ReadAsync(); var entry = initial.Document.CollectedEntries.Single(); var example = entry.Senses.Single().Examples.Single();
        var a = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Greeting")]); var b = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Welcome")]) { Kind = R.VocabularyMeaningNodeKind.Function };
        var pattern = new R.VocabularyUsagePattern(entry.Senses.Single().Id, "hello there"); var mapping = new R.VocabularyMeaningMapping(a.Id, entry.Senses.Single().Id) { PatternId = pattern.Id, EvidenceIds = [example.Id], Provenance = new R.VocabularySemanticProvenance { Origin = R.VocabularySemanticOrigin.Model, Status = R.VocabularySemanticStatus.Proposed } };
        await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [a, b], Patterns = [pattern], Mappings = [mapping] }, initial.Document.Semantics);
        var baseline = (await store.ReadAsync()).Document.Semantics; var form = new FrozenVocabularyMappingEditorWindow(store, baseline, [entry], [LearningLanguage.English], original: mapping); form.Show();
        Find<ComboBox>(form, "canonicalMappingNode").SelectedValue = b.Id; Find<TextBox>(form, "canonicalMappingConditions").Text = "informal"; Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync();
        var saved = (await store.ReadAsync()).Document.Semantics.Mappings.Single(); Assert.Equal(mapping.Id, saved.Id); Assert.Equal(b.Id, saved.NodeId); Assert.Equal(mapping.PatternId, saved.PatternId); Assert.Equal(mapping.EvidenceIds, saved.EvidenceIds); Assert.Equal(R.VocabularySemanticStatus.Proposed, saved.Provenance.Status); Assert.Equal(R.VocabularyMappingRelation.RealizesFunction, saved.Relation);
        var other = await store.AddEntriesAsync([new(Expression: "other")]);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; window.Reveal(receipt.Selection); await window.LastOperation!;
        var stale = Find<Button>(window, "canonicalRemoveMapping"); window.Reveal(other.Selection); await window.LastOperation!; stale.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Single((await store.ReadAsync()).Document.Semantics.Mappings); window.Reveal(receipt.Selection); await window.LastOperation!;
        Find<Button>(window, "canonicalConfirmMapping").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; Assert.Equal(R.VocabularySemanticStatus.Confirmed, (await store.ReadAsync()).Document.Semantics.Mappings.Single().Provenance.Status);
        Find<Button>(window, "canonicalRemoveMapping").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; var removed = await store.ReadAsync(); Assert.Empty(removed.Document.Semantics.Mappings); Assert.Single(removed.Document.Semantics.Patterns); Assert.Single(removed.Document.Sources); window.Close();
    });
    [Fact]
    public Task ConcurrentSameNodeConflictKeepsFormAndItsOriginalLabel() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Original")]);
        await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [node] }, initial.Document.Semantics); var baseline = (await store.ReadAsync()).Document.Semantics;
        var form = new FrozenVocabularyMeaningEditorWindow(store, baseline, [LearningLanguage.English], node); form.Show(); Find<TextBox>(form, "canonicalMeaningName").Text = "My name";
        await store.UpdateSemanticsAsync(baseline with { Nodes = [node with { Definition = "Concurrent" }] }, baseline); Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!;
        Assert.True(form.IsVisible); Assert.Equal("My name", Find<TextBox>(form, "canonicalMeaningName").Text); Assert.NotEmpty(Find<TextBlock>(form, "semanticFormError").Text); form.Close(); await form.RetireAsync(); Assert.Equal("Concurrent", (await store.ReadAsync()).Document.Semantics.Nodes.Single().Definition);
    });
    [Fact]
    public Task RetiringNotebookJoinsAcceptedSemanticMutationBeforeUnsubscribe() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var receipt = await store.AddEntriesAsync([new(Expression: "hello")]); var before = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Greeting")]);
        await store.UpdateSemanticsAsync(before.Document.Semantics with { Nodes = [node], Mappings = [new R.VocabularyMeaningMapping(node.Id, receipt.Selection!.Value.SenseId)] }, before.Document.Semantics);
        var held = new HeldOwner(store, store); var window = new FrozenVocabularyNotebookWindow(held, new WindowsVocabularyWordListReader(), hideOnClose: true); window.Show(); await window.LastOperation!; window.Reveal(receipt.Selection); await window.LastOperation!;
        Find<Button>(window, "canonicalRemoveMapping").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await held.Written.Task; var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted); Assert.Equal(0, held.Unsubscribed);
        held.Gate.SetResult(); await retirement; Assert.Equal(1, held.Unsubscribed); Assert.Empty((await store.ReadAsync()).Document.Semantics.Mappings);
    });
    [Fact]
    public Task NamedDatabaseAcknowledgesAndReopensMeaningForm() => dispatcher.RunAsync(async () =>
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canonical-meaning-ui-" + Guid.NewGuid().ToString("N")); string file = System.IO.Path.Combine(directory, FrozenVocabularyStoreFactory.CanonicalFileName); Guid id;
        try
        {
            await using (var store = FrozenVocabularyStoreFactory.Create(file, false))
            {
                var baseline = (await store.ReadAsync()).Document.Semantics; var form = new FrozenVocabularyMeaningEditorWindow(store, baseline, [LearningLanguage.French]); id = form.NodeId; form.Show();
                Find<TextBox>(form, "canonicalMeaningName").Text = "salutation"; Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync(); Assert.True(form.Saved);
            }
            await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(file, installBundledClassification: false); var node = (await reopened.ReadAsync()).Document.Semantics.Nodes.Single(); Assert.Equal(id, node.Id); Assert.Equal("salutation", node.Labels.Single().Text); Assert.Equal(LearningLanguage.French, node.Labels.Single().Language);
        }
        finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
    });
    private static async Task Until(Func<bool> condition) { for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<T>().First(x => x.Name == name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var value in Descendants(VisualTreeHelper.GetChild(root, i))) yield return value; }
}
