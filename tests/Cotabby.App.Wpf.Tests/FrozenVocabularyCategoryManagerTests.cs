using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.EntryInput;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Win32.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class FrozenVocabularyCategoryManagerTests(WpfDispatcher dispatcher)
{
    private sealed class HeldStore(IVocabularyEntryImportStore reader, ICanonicalVocabularySemanticStore store) : IVocabularyEntryImportStore, ICanonicalVocabularySemanticStore
    {
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously), Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); internal int Unsubscribed; internal bool Fail;
        public Task<FrozenVocabularyImportSnapshot> ReadAsync(CancellationToken token = default) => reader.ReadAsync(token);
        public Task<FrozenVocabularyImportPreview> PreviewWordListAsync(VocabularyWordList table, VocabularyWordListConfiguration config, CancellationToken token = default) => reader.PreviewWordListAsync(table, config, token);
        public Task<VocabularyEntryImportResult> AddEntriesAsync(IReadOnlyList<VocabularyEntryInput> inputs, CancellationToken token = default) => reader.AddEntriesAsync(inputs, token);
        public IDisposable Subscribe(Action<FrozenVocabularyImportSnapshot> changed) => new Sub(reader.Subscribe(changed), this);
        private sealed class Sub(IDisposable value, HeldStore owner) : IDisposable { public void Dispose() { value.Dispose(); owner.Unsubscribed++; } }
        public Task<FrozenVocabularyImportSnapshot> UpdateSemanticsAsync(VocabularySemanticIndex edited, VocabularySemanticIndex baseline, CancellationToken token = default) => store.UpdateSemanticsAsync(edited, baseline, token);
        public async Task<FrozenVocabularyImportSnapshot> UpdateClassificationAsync(R.VocabularyClassificationIndex edited, R.VocabularyClassificationIndex baseline, CancellationToken token = default) { if (Fail) throw new VocabularyException(VocabularyFailure.EditConflict); var receipt = await store.UpdateClassificationAsync(edited, baseline, token); Written.TrySetResult(); await Gate.Task; return receipt; }
    }
    [Fact]
    public Task SidebarCreatesChildAndSelectsAcknowledgedId() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var parent = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Function, [new R.VocabularyMeaningLabel("Parent")]);
        await store.UpdateClassificationAsync(initial.Document.Classification with { Categories = [parent] }, initial.Document.Classification);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), [LearningLanguage.French]); window.Show(); await window.LastOperation!; Find<ComboBox>(window, "frozenNotebookOrganization").SelectedIndex = 1; window.UpdateLayout();
        var list = FindAutomation<ListBox>(window, "vocabularyCategoryList"); list.SelectedItem = list.Items.Cast<ListBoxItem>().Single(i => i.Tag is string s && s.Equals(parent.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        _ = window.Dispatcher.BeginInvoke(() => { var form = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(form, "categoryName").Text = "Enfant"; Find<Button>(form, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        FindAutomation<Button>(window, "vocabularyNewCategory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await window.LastOperation!; var result = (await store.ReadAsync()).Document.Classification; var child = result.Categories.Single(c => c.Id != parent.Id);
        Assert.Equal(R.VocabularyCategoryAxis.Function, child.Axis); Assert.Equal(LearningLanguage.French, child.Labels.Single().Language); Assert.Contains(result.Parents, edge => edge.CategoryId == child.Id && edge.ParentId == parent.Id); Assert.Equal(child.Id, Guid.Parse(window.Session.Category));
        list = FindAutomation<ListBox>(window, "vocabularyCategoryList"); var childRow = list.Items.Cast<ListBoxItem>().Single(i => i.Tag is string s && s.Equals(child.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        _ = window.Dispatcher.BeginInvoke(() => { var form = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(form, "categoryName").Text = "Modifié"; Find<Button>(form, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        ((MenuItem)childRow.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await window.LastOperation!; Assert.Equal("Modifié", (await store.ReadAsync()).Document.Classification.Categories.Single(c => c.Id == child.Id).Labels.Single().Text); window.Close();
    });
    [Fact]
    public Task ManagerHideRestoreEditAndConfirmedDeleteRetainBuiltinsAndMeanings() => dispatcher.RunAsync(async () =>
    {
        var framework = new R.VocabularyClassificationFramework(Guid.NewGuid(), "framework", 1); var built = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Built in")]) { FrameworkId = framework.Id, Key = "built" };
        var custom = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Custom")]); var node = new VocabularyMeaningNode(Guid.NewGuid(), [new R.VocabularyMeaningLabel("Meaning")]); var membership = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, custom.Id);
        var doc = new Cotabby.Core.Vocabulary.Frozen.VocabularyDocument { Semantics = new() { Nodes = [node] }, Classification = new() { Frameworks = [framework], Categories = [built, custom], Memberships = [membership] } }; var db = FrozenVocabularySQLiteDatabase.CreateMemory(); db.Write(doc, new(), 0); await using var store = new FrozenVocabularyImportStore(db);
        var manager = new FrozenVocabularyCategoryManagerWindow(store, store, await store.ReadAsync(), [LearningLanguage.English]); manager.Show(); manager.UpdateLayout(); VocabularyThemeRender.Capture(manager, "category-manager", 620, 540);
        manager.Width = 500; manager.Height = 400; manager.UpdateLayout(); VocabularyThemeRender.Capture(manager, "category-manager-narrow", 500, 400); manager.Width = 620; manager.Height = 540; manager.UpdateLayout();
        var builtRow = Row(manager, "Built in"); Assert.DoesNotContain(Descendants(builtRow).OfType<Button>(), b => Equals(b.Content, "Delete") && b.Visibility == Visibility.Visible);
        var visible = Descendants(builtRow).OfType<CheckBox>().Single(); visible.IsChecked = false; visible.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await manager.LastOperation!;
        var hidden = (await store.ReadAsync()).Document.Classification; Assert.Equal(built, hidden.Categories.Single(c => c.Id == built.Id)); Assert.True(hidden.Overrides.Single(o => o.CategoryId == built.Id).IsHidden); VocabularyThemeRender.Capture(manager, "category-manager-hidden", 620, 540);
        builtRow = Row(manager, "Built in"); visible = Descendants(builtRow).OfType<CheckBox>().Single(); visible.IsChecked = true; visible.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await manager.LastOperation!; Assert.False((await store.ReadAsync()).Document.Classification.Overrides.Single(o => o.CategoryId == built.Id).IsHidden);
        _ = manager.Dispatcher.BeginInvoke(() => { var editor = Application.Current.Windows.OfType<FrozenVocabularyCategoryEditorWindow>().Single(); Find<TextBox>(editor, "categoryName").Text = "Renamed"; Find<Button>(editor, "classificationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Descendants(Row(manager, "Custom")).OfType<Button>().Single(b => Equals(b.Content, "Edit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await manager.LastOperation!;
        Assert.Equal("Renamed", (await store.ReadAsync()).Document.Classification.Categories.Single(c => c.Id == custom.Id).Labels.Single().Text);
        _ = manager.Dispatcher.BeginInvoke(() => Find<Button>(Application.Current.Windows.OfType<FrozenVocabularyCategoryDeleteWindow>().Single(), "categoryDeleteCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Descendants(Row(manager, "Renamed")).OfType<Button>().Single(b => Equals(b.Content, "Delete")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await manager.LastOperation!; Assert.Equal(2, (await store.ReadAsync()).Document.Classification.Categories.Count);
        _ = manager.Dispatcher.BeginInvoke(() => { var confirm = Application.Current.Windows.OfType<FrozenVocabularyCategoryDeleteWindow>().Single(); VocabularyThemeRender.Capture(confirm, "category-manager-delete", 410, (int)Math.Ceiling(confirm.ActualHeight)); Find<Button>(confirm, "categoryDeleteConfirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); });
        Descendants(Row(manager, "Renamed")).OfType<Button>().Single(b => Equals(b.Content, "Delete")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await manager.LastOperation!;
        var deleted = await store.ReadAsync(); Assert.Equal(built, deleted.Document.Classification.Categories.Single()); Assert.Empty(deleted.Document.Classification.Memberships); Assert.Single(deleted.Document.Semantics.Nodes); manager.Close(); await manager.RetireAsync();
    });
    [Fact]
    public Task ManagerRetirementDrainsAcceptedWriteBeforeUnsubscribe() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var category = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Category")]);
        await store.UpdateClassificationAsync(initial.Document.Classification with { Categories = [category] }, initial.Document.Classification); var held = new HeldStore(store, store); var manager = new FrozenVocabularyCategoryManagerWindow(held, held, await held.ReadAsync(), []); manager.Show(); manager.UpdateLayout();
        var check = Descendants(Row(manager, "Category")).OfType<CheckBox>().Single(); check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await held.Written.Task; var retirement = manager.RetireAsync(); Assert.False(retirement.IsCompleted); Assert.Equal(0, held.Unsubscribed); held.Gate.SetResult(); await retirement;
        Assert.Equal(1, held.Unsubscribed); Assert.True((await store.ReadAsync()).Document.Classification.Overrides.Single().IsHidden);
    });
    [Fact]
    public Task ManagerFailureShowsErrorAndRestoresVisibleCheckbox() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var category = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Category")]);
        await store.UpdateClassificationAsync(initial.Document.Classification with { Categories = [category] }, initial.Document.Classification); var held = new HeldStore(store, store) { Fail = true }; var manager = new FrozenVocabularyCategoryManagerWindow(held, held, await held.ReadAsync(), []); manager.Show();
        var check = Descendants(Row(manager, "Category")).OfType<CheckBox>().Single(); check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await manager.LastOperation!;
        Assert.Contains("changed while", Find<TextBlock>(manager, "categoryManagerError").Text); Assert.True(Descendants(Row(manager, "Category")).OfType<CheckBox>().Single().IsChecked); Assert.Empty((await store.ReadAsync()).Document.Classification.Overrides);
        VocabularyThemeRender.Capture(manager, "category-manager-error", 620, 540); manager.Close(); await manager.RetireAsync(); Assert.Equal(1, held.Unsubscribed);
    });
    [Fact]
    public Task ManagerObservesConcurrentDirectoryChangesWithoutResettingSearch() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync();
        var first = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("First")]); await store.UpdateClassificationAsync(initial.Document.Classification with { Categories = [first] }, initial.Document.Classification);
        var manager = new FrozenVocabularyCategoryManagerWindow(store, store, await store.ReadAsync(), []); manager.Show(); var search = Find<TextBox>(manager, "categoryManagerSearch"); search.Text = "second";
        var current = (await store.ReadAsync()).Document.Classification; var second = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Second")]); await store.UpdateClassificationAsync(current with { Categories = current.Categories.Append(second).ToArray() }, current);
        await Until(() => { manager.UpdateLayout(); return Descendants(manager).OfType<Button>().Any(b => Equals(b.Content, "Second")); }); Assert.Equal("second", search.Text); Assert.DoesNotContain(Descendants(manager).OfType<Button>(), b => Equals(b.Content, "First")); manager.Close(); await manager.RetireAsync();
    });
    [Fact]
    public Task SidebarManagerHidingSelectedCategoryReconcilesAndReenablesOnReopen() => dispatcher.RunAsync(async () =>
    {
        await using var store = new FrozenVocabularyImportStore(FrozenVocabularySQLiteDatabase.CreateMemory()); var initial = await store.ReadAsync(); var category = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new R.VocabularyMeaningLabel("Visible category")]);
        await store.UpdateClassificationAsync(initial.Document.Classification with { Categories = [category] }, initial.Document.Classification);
        var window = new FrozenVocabularyNotebookWindow(store, new WindowsVocabularyWordListReader(), hideOnClose: true); window.Show(); await window.LastOperation!; Find<ComboBox>(window, "frozenNotebookOrganization").SelectedIndex = 1; window.UpdateLayout();
        var list = FindAutomation<ListBox>(window, "vocabularyCategoryList"); list.SelectedItem = list.Items.Cast<ListBoxItem>().Single(i => i.Tag is string s && s.Equals(category.Id.ToString(), StringComparison.OrdinalIgnoreCase)); Assert.Equal(category.Id, Guid.Parse(window.Session.Category));
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () => { try { var manager = Application.Current.Windows.OfType<FrozenVocabularyCategoryManagerWindow>().Single(); var check = Descendants(Row(manager, "Visible category")).OfType<CheckBox>().Single(); check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await manager.LastOperation!; await Until(() => window.Session.Category == "all"); Find<Button>(manager, "categoryManagerDone").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); complete.SetResult(); } catch (Exception e) { complete.SetException(e); } });
        FindAutomation<Button>(window, "vocabularyManageCategories").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await complete.Task; await window.LastOperation!;
        Assert.True(FindAutomation<Button>(window, "vocabularyManageCategories").IsEnabled); window.Close(); Assert.False(window.IsVisible); window.Show(); Assert.True(FindAutomation<Button>(window, "vocabularyManageCategories").IsEnabled); await window.RetireAsync();
    });
    private static async Task Until(Func<bool> condition) { for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }
    private static ListBoxItem Row(Window root, string title) { root.UpdateLayout(); return Descendants(root).OfType<ListBoxItem>().First(row => Descendants(row).OfType<Button>().Any(button => Equals(button.Content, title))); }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<T>().First(x => x.Name == name); }
    private static T FindAutomation<T>(DependencyObject root, string id) where T : FrameworkElement { if (root is Window window) window.UpdateLayout(); return Descendants(root).OfType<T>().First(x => AutomationProperties.GetAutomationId(x) == id); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
