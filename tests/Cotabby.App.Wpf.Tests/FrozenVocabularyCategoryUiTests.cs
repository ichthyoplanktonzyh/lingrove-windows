using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyCategoryUiTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task LatestNestedCategoryIsDetachedAndConcurrentMembershipSurvivesSave() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Meaning")]); await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [node] }, initial.Document.Semantics);
        var snapshot = await store.ReadAsync(); var a = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("A")]); var b = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Function, [new R.VocabularyMeaningLabel("B")]); var existing = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, a.Id);
        var saved = await store.UpdateClassificationAsync(snapshot.Document.Classification with { Categories = [a, b], Memberships = [existing] }, snapshot.Document.Classification); var baseline = saved.Document.Classification;
        var picker = new FrozenVocabularyDraftPickerWindow(store, baseline, [], new HashSet<Guid> { a.Id }, R.VocabularyCategoryAxis.Content, commit: async (directory, ids) => { await store.UpdateClassificationAsync(VocabularyClassificationFormPolicy.SelectMemberships(directory, node.Id, ids), baseline); }); picker.Show();
        _ = picker.Dispatcher.BeginInvoke(() => { var editor = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(editor, "categoryName").Text = "New"; Find<Button>(editor, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Find<Button>(picker, "meaningDraftNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Yield(); var live = (await store.ReadAsync()).Document.Classification; Assert.Equal(2, live.Categories.Count);
        var concurrent = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, b.Id); await store.UpdateClassificationAsync(live with { Memberships = live.Memberships.Append(concurrent).ToArray() }, live);
        Find<Button>(picker, "meaningDraftPickerSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); for (int i = 0; i < 200 && picker.IsVisible; i++) await Task.Delay(10); Assert.False(picker.IsVisible); await picker.RetireAsync(); var result = (await store.ReadAsync()).Document.Classification;
        Assert.Equal(3, result.Categories.Count); Assert.Contains(result.Memberships, x => x.Id == concurrent.Id); Assert.Contains(result.Memberships, x => x.CategoryId == a.Id); Assert.Equal(3, result.Memberships.Count);
    });
    [Fact]
    public Task LatestBuiltInRestoreRemovesLocalOverlayWithoutChangingSourceGuidance() => dispatcher.RunAsync(async () =>
    {
        var framework = new R.VocabularyClassificationFramework(Guid.NewGuid(), "catalog", 4); var category = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Original")]) { FrameworkId = framework.Id, Key = "original", Examples = [new("example", "en")], Localizations = [new("en", "Original", "Definition")] };
        var db = FrozenVocabularySQLiteDatabase.CreateMemory(); db.Write(new() { Classification = new() { Frameworks = [framework], Categories = [category], Overrides = [new(category.Id) { Localizations = [new("en", Name: "Local")], IsHidden = true }] } }, new(), 0); await using var store = new FrozenVocabularyImportStore(db); var baseline = (await store.ReadAsync()).Document.Classification;
        var form = new FrozenVocabularyCategoryEditorWindow(store, baseline, [], category, currentRules: true); form.Show(); Assert.Equal("Local", Find<TextBox>(form, "categoryName").Text); Find<Button>(form, "categoryRestoreDefaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("Original", Find<TextBox>(form, "categoryName").Text); Find<Button>(form, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync();
        var saved = (await store.ReadAsync()).Document.Classification; Assert.Empty(saved.Overrides); Assert.Equal(category, Assert.Single(saved.Categories));
    });
    private sealed class HeldStore(ICanonicalVocabularySemanticStore inner) : ICanonicalVocabularySemanticStore
    {
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously), Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(VocabularySemanticIndex edited, VocabularySemanticIndex baseline, CancellationToken token = default) => inner.UpdateSemanticsAsync(edited, baseline, token);
        public async Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(R.VocabularyClassificationIndex edited, R.VocabularyClassificationIndex baseline, CancellationToken token = default) { var result = await inner.UpdateClassificationAsync(edited, baseline, token); Written.TrySetResult(); await Gate.Task; return result; }
    }
    private sealed class HeldReader(IVocabularyEntryImportStore inner) : IVocabularyEntryImportStore
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); internal CancellationToken Token;
        public async Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) { Token = token; Started.TrySetResult(); await Gate.Task; return await inner.ReadAsync(); }
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => inner.PreviewWordListAsync(table, config, token);
        public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default) => inner.AddEntriesAsync(inputs, token);
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) => inner.Subscribe(changed);
    }
    private sealed class HeldOwner(IVocabularyEntryImportStore inner, ICanonicalVocabularySemanticStore semantic) : IVocabularyEntryImportStore, ICanonicalVocabularySemanticStore
    {
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously), Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); internal int Unsubscribed;
        public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) => inner.ReadAsync(token);
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => inner.PreviewWordListAsync(table, config, token);
        public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default) => inner.AddEntriesAsync(inputs, token);
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) => new Subscription(inner.Subscribe(changed), this);
        private sealed class Subscription(IDisposable value, HeldOwner owner) : IDisposable { public void Dispose() { value.Dispose(); owner.Unsubscribed++; } }
        public Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(VocabularySemanticIndex edited, VocabularySemanticIndex baseline, CancellationToken token = default) => semantic.UpdateSemanticsAsync(edited, baseline, token);
        public async Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(R.VocabularyClassificationIndex edited, R.VocabularyClassificationIndex baseline, CancellationToken token = default) { var result = await semantic.UpdateClassificationAsync(edited, baseline, token); Written.TrySetResult(); await Gate.Task; return result; }
    }
    [Fact]
    public Task NotebookCategorizeSearchToggleAndConfirmProposal() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Greeting")]);
        await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [node] }, initial.Document.Semantics); var before = await store.ReadAsync();
        var a = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Existing")]); var b = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Newly selected")]);
        var proposal = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, a.Id) { Provenance = new() { Origin = R.VocabularySemanticOrigin.Model, Status = R.VocabularySemanticStatus.Proposed } };
        await store.UpdateClassificationAsync(before.Document.Classification with { Categories = [a, b], Memberships = [proposal] }, before.Document.Classification);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader()); window.Show(); await window.LastOperation!; window.Session.MeaningMode = true; window.Session.Destination = new(VocabularyMeaningDestinationKind.Node, node.Id); window.Session.Refresh();
        _ = window.Dispatcher.BeginInvoke(() =>
        {
            var sheet = Application.Current.Windows.OfType<FrozenVocabularyDraftPickerWindow>().Single(); var search = Find<TextBox>(sheet, "meaningDraftPickerSearch"); search.Text = "newly"; sheet.UpdateLayout();
            var check = Descendants(sheet).OfType<CheckBox>().Single(c => (string?)c.Content == "Newly selected"); check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); search.Text = ""; sheet.UpdateLayout();
            Assert.True(Descendants(sheet).OfType<CheckBox>().Single(c => (string?)c.Content == "Newly selected").IsChecked); Find<Button>(sheet, "meaningDraftPickerSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Find<Button>(window, "canonicalCategorizeMeaning").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; var result = (await store.ReadAsync()).Document.Classification;
        Assert.Equal(2, result.Memberships.Count); Assert.Equal(R.VocabularySemanticStatus.Confirmed, result.Memberships.Single(m => m.Id == proposal.Id).Provenance.Status); Assert.Contains(result.Memberships, m => m.CategoryId == b.Id && m.NodeId == node.Id); window.Close();
    });
    [Fact]
    public Task NestedCreationPreservesConcurrentMembershipAndSelectsNewCategory() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Greeting")]);
        await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [node] }, initial.Document.Semantics); var before = await store.ReadAsync();
        var a = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Existing")]); var b = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Function, [new R.VocabularyMeaningLabel("Concurrent")]);
        var membership = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, a.Id);
        await store.UpdateClassificationAsync(before.Document.Classification with { Categories = [a, b], Memberships = [membership] }, before.Document.Classification); var baseline = (await store.ReadAsync()).Document.Classification;
        var window = new FrozenVocabularyMeaningCategoryWindow(store, store, baseline, node.Id, [LearningLanguage.French]); window.Show();
        var callback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () => { try { var child = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(child, "categoryName").Text = "Nouveau"; VocabularyThemeRender.Capture(child, "category-new", 600, 560);
            var live = (await store.ReadAsync()).Document.Classification; await store.UpdateClassificationAsync(live with { Memberships = live.Memberships.Append(new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, b.Id)).ToArray() }, live);
            Find<Button>(child, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await child.LastOperation!; callback.SetResult(); } catch (Exception e) { callback.SetException(e); } });
        Find<Button>(window, "membershipNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await callback.Task; await Until(() => Find<Button>(window, "classificationSave").IsEnabled);
        VocabularyThemeRender.Capture(window, "category-membership", 600, 560); window.Width = 500; window.Height = 440; window.UpdateLayout(); VocabularyThemeRender.Capture(window, "category-membership-narrow", 500, 440); window.Width = 600; window.Height = 560; window.UpdateLayout();
        Find<Button>(window, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; await window.RetireAsync();
        var result = (await store.ReadAsync()).Document.Classification; var created = result.Categories.Single(c => c.Labels.Single().Text == "Nouveau"); Assert.Equal(LearningLanguage.French, created.Labels.Single().Language); Assert.Equal(new[] { a.Id, b.Id, created.Id }.Order(), result.Memberships.Where(m => m.NodeId == node.Id).Select(m => m.CategoryId).Order());
    });
    [Fact]
    public Task BuiltInEditWritesOverrideAndConcurrentConflictKeepsDraft() => dispatcher.RunAsync(async () =>
    {
        var framework = new R.VocabularyClassificationFramework(Guid.NewGuid(), "framework", 1); var label = new R.VocabularyMeaningLabel("Built in") { Language = LearningLanguage.English }; var category = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [label]) { FrameworkId = framework.Id, Key = "category", Definition = "base" };
        var doc = new VocabularyDocument { Classification = new() { Frameworks = [framework], Categories = [category] } }; var db = FrozenVocabularySQLiteDatabase.CreateMemory(); db.Write(doc, new(), 0); await using var store = new FrozenVocabularyImportStore(db);
        var baseline = (await store.ReadAsync()).Document.Classification; var form = new FrozenVocabularyCategoryEditorWindow(store, baseline, [LearningLanguage.French], category); form.Show();
        Assert.Equal("Built in", Find<TextBox>(form, "categoryName").Text); Find<TextBox>(form, "categoryName").Text = "Personal"; Find<TextBox>(form, "categoryDescription").Text = "My definition";
        Find<Button>(form, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync(); var updated = (await store.ReadAsync()).Document.Classification;
        Assert.Equal(category, updated.Categories.Single()); var overlay = updated.Overrides.Single(); Assert.Equal(label.Id, overlay.Labels!.Single().Id); Assert.Equal(label.Language, overlay.Labels!.Single().Language); Assert.Equal("Personal", overlay.Labels!.Single().Text); Assert.Equal("My definition", overlay.Definition);
        var conflict = new FrozenVocabularyCategoryEditorWindow(store, updated, [LearningLanguage.French], category); conflict.Show(); Find<TextBox>(conflict, "categoryName").Text = "Mine";
        await store.UpdateClassificationAsync(updated with { Overrides = [overlay with { Definition = "Theirs" }] }, updated); Find<Button>(conflict, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await conflict.LastOperation!;
        Assert.True(conflict.IsVisible); Assert.Equal("Mine", Find<TextBox>(conflict, "categoryName").Text); Assert.NotEmpty(Find<TextBlock>(conflict, "classificationError").Text); VocabularyThemeRender.Capture(conflict, "category-conflict", 600, 560); conflict.Close(); await conflict.RetireAsync();
    });
    [Fact]
    public Task CustomParentChoicesExcludeDescendantsAndKeepSelectedIdentity() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var before = await store.ReadAsync();
        var root = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Root")]); var child = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Child")]); var alternative = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Alternative")]);
        await store.UpdateClassificationAsync(before.Document.Classification with { Categories = [root, child, alternative], Parents = [new(Guid.NewGuid(), child.Id, root.Id)] }, before.Document.Classification);
        var baseline = (await store.ReadAsync()).Document.Classification; var form = new FrozenVocabularyCategoryEditorWindow(store, baseline, [], root); form.Show(); form.UpdateLayout();
        var list = Find<ListBox>(form, "categoryParents"); var choices = list.Items.Cast<FrozenVocabularyCategoryChoice>().ToArray(); Assert.Single(choices); Assert.Equal(alternative.Id, choices[0].Id);
        var check = Descendants(list).OfType<CheckBox>().Single(c => (string?)c.Content == "Alternative"); check.IsChecked = true; Find<Button>(form, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await form.LastOperation!; await form.RetireAsync();
        var result = (await store.ReadAsync()).Document.Classification; Assert.Contains(result.Parents, e => e.CategoryId == root.Id && e.ParentId == alternative.Id); Assert.Contains(result.Parents, e => e.CategoryId == child.Id && e.ParentId == root.Id);
    });
    [Fact]
    public Task RetirementDrainsNestedAcceptedCategorySave() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var before = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Meaning")]); await store.UpdateSemanticsAsync(before.Document.Semantics with { Nodes = [node] }, before.Document.Semantics);
        var held = new HeldStore(store); var form = new FrozenVocabularyMeaningCategoryWindow(held, store, (await store.ReadAsync()).Document.Classification, node.Id, []); form.Show(); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = form.Dispatcher.BeginInvoke(async () => { try { var child = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(child, "categoryName").Text = "New"; Find<Button>(child, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await held.Written.Task; var retirement = form.RetireAsync(); Assert.False(retirement.IsCompleted); held.Gate.SetResult(); await retirement; done.SetResult(); } catch (Exception e) { done.SetException(e); } });
        Find<Button>(form, "membershipNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await done.Task; Assert.False(form.IsVisible); Assert.Single((await store.ReadAsync()).Document.Classification.Categories);
    });
    [Fact]
    public Task RetirementCancelsAndJoinsLateDirectoryRead() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var before = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Meaning")]); await store.UpdateSemanticsAsync(before.Document.Semantics with { Nodes = [node] }, before.Document.Semantics);
        var reader = new HeldReader(store); var form = new FrozenVocabularyMeaningCategoryWindow(store, reader, (await store.ReadAsync()).Document.Classification, node.Id, []); form.Show(); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = form.Dispatcher.BeginInvoke(async () => { try { var child = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(child, "categoryName").Text = "New"; Find<Button>(child, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await reader.Started.Task; var retirement = form.RetireAsync(); Assert.True(reader.Token.IsCancellationRequested); Assert.False(retirement.IsCompleted); reader.Gate.SetResult(); await retirement; done.SetResult(); } catch (Exception e) { done.SetException(e); } });
        Find<Button>(form, "membershipNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await done.Task; Assert.False(form.IsVisible); Assert.Single((await store.ReadAsync()).Document.Classification.Categories);
    });
    [Fact]
    public Task NotebookRetirementWaitsForNestedAcceptedWriteBeforeUnsubscribe() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var before = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Meaning")]); await store.UpdateSemanticsAsync(before.Document.Semantics with { Nodes = [node] }, before.Document.Semantics);
        var held = new HeldOwner(store, store); var window = new FrozenVocabularyNotebookWindow(held, new WindowsVocabularyWordListReader(), hideOnClose: true); window.Show(); await window.LastOperation!; window.Session.MeaningMode = true; window.Session.Destination = new(VocabularyMeaningDestinationKind.Node, node.Id); window.Session.Refresh();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(() => { var sheet = Application.Current.Windows.OfType<FrozenVocabularyDraftPickerWindow>().Single(); _ = sheet.Dispatcher.BeginInvoke(() => { var child = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(child, "categoryName").Text = "New"; Find<Button>(child, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }); Find<Button>(sheet, "meaningDraftNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); _ = sheet.Dispatcher.BeginInvoke(async () => { try { Find<Button>(sheet, "meaningDraftPickerSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await held.Written.Task; var retirement = window.RetireAsync(); Assert.False(retirement.IsCompleted); Assert.Equal(0, held.Unsubscribed); held.Gate.SetResult(); await retirement; done.SetResult(); } catch (Exception e) { done.SetException(e); } }); });
        Find<Button>(window, "canonicalCategorizeMeaning").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await done.Task; Assert.Equal(1, held.Unsubscribed); Assert.Single((await store.ReadAsync()).Document.Classification.Categories);
    });
    [Fact]
    public Task NamedStoreReopensAcknowledgedCategoryAndMembership() => dispatcher.RunAsync(async () =>
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "canonical-category-ui-" + Guid.NewGuid().ToString("N")); string file = System.IO.Path.Combine(directory, FrozenVocabularyStoreFactory.CanonicalFileName); Guid nodeId, categoryId;
        try
        {
            await using (var store = FrozenVocabularyStoreFactory.Create(file, false))
            {
                var initial = await store.ReadAsync(); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Greeting")]); nodeId = node.Id; await store.UpdateSemanticsAsync(initial.Document.Semantics with { Nodes = [node] }, initial.Document.Semantics);
                var editor = new FrozenVocabularyCategoryEditorWindow(store, (await store.ReadAsync()).Document.Classification, [LearningLanguage.French]); categoryId = editor.CategoryId; editor.Show(); Find<TextBox>(editor, "categoryName").Text = "Personnes"; Find<Button>(editor, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await editor.LastOperation!; await editor.RetireAsync();
                var sheet = new FrozenVocabularyMeaningCategoryWindow(store, store, (await store.ReadAsync()).Document.Classification, nodeId, [LearningLanguage.French]); sheet.Show(); sheet.UpdateLayout(); Descendants(sheet).OfType<CheckBox>().Single(c => (string?)c.Content == "Personnes").IsChecked = true; Find<Button>(sheet, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await sheet.LastOperation!; await sheet.RetireAsync();
            }
            await using var reopened = await FrozenVocabularyStoreFactory.OpenAsync(file, installBundledClassification: false); var result = (await reopened.ReadAsync()).Document.Classification; Assert.Equal(categoryId, result.Categories.Single().Id); Assert.Equal(nodeId, result.Memberships.Single().NodeId); Assert.Equal(categoryId, result.Memberships.Single().CategoryId);
        }
        finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
    });
    private static async Task Until(Func<bool> predicate) { for (int i = 0; i < 200 && !predicate(); i++) await Task.Delay(10); Assert.True(predicate()); }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<T>().First(x => x.Name == name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var value in Descendants(VisualTreeHelper.GetChild(root, i))) yield return value; }
}
