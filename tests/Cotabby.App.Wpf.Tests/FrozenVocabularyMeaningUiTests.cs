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
        _ = window.Dispatcher.BeginInvoke(() => { var form = Application.Current.Windows.OfType<FrozenVocabularyMappingEditorWindow>().Single(); Find<ComboBox>(form, "canonicalMappingSense").SelectedValue = second.Selection!.Value.SenseId; Find<TextBox>(form, "canonicalMappingNuance").Text = "informal"; VocabularyThemeRender.Capture(form, "meaning-mapping", 600, 520); Find<Button>(form, "semanticFormSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
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
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<T>().First(x => x.Name == name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var value in Descendants(VisualTreeHelper.GetChild(root, i))) yield return value; }
}
