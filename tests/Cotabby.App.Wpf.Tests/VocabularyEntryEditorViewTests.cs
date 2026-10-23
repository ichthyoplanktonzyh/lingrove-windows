using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Production draft controls and durable acknowledgements around actual memory SQLite.</summary>
[Collection("WPF views")]
public sealed class VocabularyEntryEditorViewTests(WpfDispatcher dispatcher)
{
    private static VocabularyCapture Capture(string text = "The bank.") => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
        Example: new(Guid.NewGuid(), text, "原句", [new(text.IndexOf("bank", StringComparison.Ordinal), 4)], VocabularyExampleCompleteness.Sentence, "context", DateTimeOffset.UnixEpoch,
            LearningLanguage.Chinese, [new("variant", LearningLanguage.French)], LearningLanguage.English, VocabularyMeaningOrigin.Ai, "source.application"));
    private static VocabularyStore Store(VocabularyArchive archive) => new(archive, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static TextBox Field(Window window, string label) => Assert.Single(Descendants<TextBox>(window), value => AutomationProperties.GetName(value) == label);
    private static Button Button(DependencyObject root, string label) => Assert.Single(Descendants<Button>(root), value => value.Content as string == label);
    private static async Task Refresh(VocabularyNotebookWindow window)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; window.UpdateLayout();
    }
    [Fact]
    public Task CancelRetainsTheAcknowledgedGraphAndBlankExampleBlocksSave() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture()));
        var baseline = Assert.Single(store.Entries); var snapshot = store.Snapshot; var window = new VocabularyEntryEditorWindow(store, baseline, baseline.Senses[0].Id);
        try
        {
            window.Show(); window.UpdateLayout(); Find<TextBox>(window, "ExpressionText").Text = "edited";
            Find<TextBox>(window, "NoteText").Text = "never saved"; Click(Find<Button>(window, "AddTextButton"));
            Click(Find<Button>(window, "AddExampleButton")); Assert.False(Find<Button>(window, "SaveButton").IsEnabled);
            Assert.Equal(2, window.Draft.Senses[0].Examples.Count); Assert.Same(snapshot, store.Snapshot);
            Click(Find<Button>(window, "CancelButton")); Assert.False(window.IsVisible); Assert.Same(snapshot, store.Snapshot);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task ChangedOriginalRetiresDependentAnnotationsAndSaveClosesAfterCommit() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); var capture = Capture(); Assert.True(await store.SaveAsync(capture));
        var input = LearningExplanationPolicy.Input(capture); var record = new LearningExplanationRecord(Guid.NewGuid(), input, LearningLanguage.English,
            new("meaning", "explanation"), "test-local", DateTimeOffset.UnixEpoch); Assert.True(await store.SaveExplanationAsync(record));
        var baseline = Assert.Single(store.Entries); var before = baseline.Senses[0].Examples[0]; var window = new VocabularyEntryEditorWindow(store, baseline, baseline.Senses[0].Id);
        try
        {
            window.Show(); window.UpdateLayout(); Field(window, "Original text").Text = "Another bank.";
            var changed = Assert.Single(window.Draft.Senses[0].Examples);
            Assert.Equal(before.Id, changed.Id); Assert.Equal(before.CreatedAt, changed.CreatedAt); Assert.Equal(before.SourceApplicationId, changed.SourceApplicationId);
            Assert.Empty(changed.Ranges); Assert.Equal("", changed.Translation); Assert.Null(changed.TranslationLanguage); Assert.Empty(changed.TranslationVariants);
            Assert.Equal("", changed.Meaning); Assert.Null(changed.MeaningLanguage); Assert.Equal(VocabularyMeaningOrigin.Captured, changed.MeaningOrigin);
            Assert.Equal(VocabularyExampleCompleteness.Fragment, changed.Completeness); Assert.Equal(before.Explanations, changed.Explanations);
            Assert.Equal("", Field(window, "Translation").Text); Assert.Equal("", Field(window, "Meaning in this context").Text);
            Assert.Equal(before, store.Entries[0].Examples[0]);
            Field(window, "Translation").Text = "my translation"; Field(window, "Meaning in this context").Text = "my meaning";
            Find<TextBox>(window, "TagsText").Text = "Work，Home,Work"; Click(Find<Button>(window, "SaveButton")); await window.LastOperation;
            Assert.False(window.IsVisible); var saved = Assert.Single(Assert.Single(store.Entries).Senses);
            Assert.Equal(new[] { "Home", "Work" }, saved.Tags); Assert.Equal("Another bank.", saved.Examples[0].Text);
            Assert.Equal("my meaning", saved.Examples[0].Meaning); Assert.Equal(VocabularyMeaningOrigin.User, saved.Examples[0].MeaningOrigin);
            Assert.Empty(saved.Examples[0].Explanations); Assert.Equal(before.Id, saved.Examples[0].Id);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task ConflictingSaveKeepsTheDraftOpenAndRetainsTheLiveNote() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture())); var baseline = store.Entries[0];
        var window = new VocabularyEntryEditorWindow(store, baseline, baseline.Senses[0].Id);
        try
        {
            window.Show(); Find<TextBox>(window, "NoteText").Text = "draft note";
            Assert.True(await store.UpdateAsync(baseline with { Senses = [baseline.Senses[0] with { Note = "live note" }] }, baseline));
            Click(Find<Button>(window, "SaveButton")); await window.LastOperation;
            Assert.True(window.IsVisible); Assert.True(Find<Button>(window, "SaveButton").IsEnabled); Assert.Equal("draft note", window.Draft.Senses[0].Note);
            Assert.Equal("live note", store.Entries[0].Senses[0].Note); Assert.Equal(store.ErrorMessage, Find<TextBlock>(window, "FailureText").Text);
            Assert.Equal(Visibility.Visible, Find<TextBlock>(window, "FailureText").Visibility);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task StaleDraftKeepsNewlyCapturedSensesAndExamples() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); var capture = Capture(); Assert.True(await store.SaveAsync(capture)); var baseline = store.Entries[0];
        Assert.Null(baseline.Senses[0].Content);
        var senseId = baseline.Senses[0].Id; var window = new VocabularyEntryEditorWindow(store, baseline, senseId);
        try
        {
            window.Show(); Find<TextBox>(window, "NoteText").Text = "draft note";
            Assert.True(await store.SaveAsync(Capture("My bank.") with { SenseId = senseId })); Assert.True(await store.SaveAsync(Capture("A bank.")));
            var live = store.Entries[0]; Assert.Equal(2, live.Senses.Count); Assert.Equal(3, live.Examples.Count);
            Click(Find<Button>(window, "SaveButton")); await window.LastOperation;
            Assert.False(window.IsVisible); var saved = Assert.Single(store.Entries); Assert.Equal(2, saved.Senses.Count); Assert.Equal(3, saved.Examples.Count);
            Assert.Equal("draft note", saved.Senses.Single(value => value.Id == senseId).Note);
            Assert.Null(saved.Senses.Single(value => value.Id == senseId).Content);
            Assert.Equal(live.Examples.Select(value => value.Id), saved.Examples.Select(value => value.Id));
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task ContentEditsKeepImageReferencesAndStableBlockOrder() => dispatcher.RunAsync(async () =>
    {
        var archive = new VocabularyArchive((string?)null); await using var store = Store(archive); Assert.True(await store.SaveAsync(Capture()));
        var baseline = store.Entries[0]; var image = new VocabularyImageReference(new string('a', 64), 1, 1, 1);
        var first = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("literal")));
        var middle = VocabularyContentBlock.Create(new VocabularyBlockContent.Image(image, "caption"));
        var last = VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new("last")));
        // This internal whole-graph fixture admits metadata only. It proves preservation of
        // existing references in the text editor, never byte validation or actual image storage.
        store.Apply(await archive.CommitAsync(store.Snapshot.Document with { Entries = [baseline with { Senses = [baseline.Senses[0] with { Content = [first, middle, last] }] }] }));
        baseline = store.Entries[0]; var window = new VocabularyEntryEditorWindow(store, baseline, baseline.Senses[0].Id);
        try
        {
            window.Show(); window.UpdateLayout(); var cards = Find<StackPanel>(window, "BlocksContent");
            Click(Button(cards.Children[1], "Move up")); window.UpdateLayout();
            Field(window, "Caption").Text = "edited caption";
            var text = Assert.Single(Descendants<TextBox>(window), value => value.Text == "literal"); text.Text = "<literal>&";
            var textCard = cards.Children[1]; Assert.Single(Descendants<ComboBox>(textCard)).SelectedItem = VocabularyTextStyle.Heading;
            var bold = Assert.Single(Descendants<CheckBox>(textCard), value => value.Content as string == "Bold"); bold.IsChecked = true; bold.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Click(Find<Button>(window, "SaveButton")); await window.LastOperation; Assert.False(window.IsVisible);
            var blocks = store.Entries[0].Senses[0].Content!; Assert.Equal(new[] { middle.Id, first.Id, last.Id }, blocks.Select(value => value.Id));
            var retained = Assert.IsType<VocabularyBlockContent.Image>(blocks[0].Content); Assert.Equal(image, retained.Reference); Assert.Equal("edited caption", retained.Caption);
            var content = Assert.IsType<VocabularyBlockContent.Text>(blocks[1].Content); Assert.Equal("<literal>&", content.Value.Text); Assert.True(content.Value.Bold); Assert.Equal(VocabularyTextStyle.Heading, content.Value.Style);
        }
        finally { await window.RetireAsync(); }
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
    public Task PendingCommitPreventsDismissalAndParentRetirementDrainsIt() => dispatcher.RunAsync(async () =>
    {
        var clock = new ClockGate(); await using var store = Store(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, clock));
        Assert.True(await store.SaveAsync(Capture())); var baseline = store.Entries[0];
        var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            notebook.Reveal(new(baseline.Id, baseline.Senses[0].Id)); notebook.Show(); await Refresh(notebook);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                VocabularyEntryEditorWindow? editor = null;
                try
                {
                    editor = Assert.Single(Application.Current.Windows.OfType<VocabularyEntryEditorWindow>());
                    Find<TextBox>(editor, "NoteText").Text = "admitted note"; clock.Block = true;
                    Click(Find<Button>(editor, "SaveButton")); await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(Find<StackPanel>(editor, "FormContent").IsEnabled); Assert.False(Find<Button>(editor, "CancelButton").IsEnabled);
                    editor.Close(); Assert.True(editor.IsVisible);
                    var retirement = notebook.RetireAsync(); Assert.False(editor.IsVisible); Assert.False(notebook.IsVisible); Assert.False(retirement.IsCompleted);
                    clock.Release.SetResult(); await retirement; Assert.Equal("admitted note", store.Entries[0].Senses[0].Note); completed.TrySetResult();
                }
                catch (Exception error) { completed.TrySetException(error); }
                finally { clock.Release.TrySetResult(); if (editor is not null) await editor.RetireAsync(); }
            });
            Click(Find<Button>(notebook, "EditButton")); await completed.Task;
        }
        finally { clock.Release.TrySetResult(); await notebook.RetireAsync(); }
    });
    [Fact]
    public Task FilteredBrowserOpensAModalDraftFromTheFullEntry() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture())); Assert.True(await store.SaveAsync(Capture("My bank.")));
        var baseline = store.Entries[0]; var senses = baseline.Senses.Select((sense, index) => sense with { Tags = [index == 0 ? "Work" : "Home"] }).ToArray();
        Assert.True(await store.UpdateAsync(baseline with { Senses = senses }, baseline)); baseline = store.Entries[0];
        var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            notebook.Reveal(new(baseline.Id, senses[0].Id)); notebook.Show(); await Refresh(notebook);
            Find<ComboBox>(notebook, "TagFilter").SelectedValue = "Work"; await Refresh(notebook);
            Assert.Single(Assert.Single(Find<TreeView>(notebook, "EntriesTree").Items.Cast<VocabularyEntryRow>()).Senses);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                VocabularyEntryEditorWindow? editor = null;
                try
                {
                    editor = Assert.Single(Application.Current.Windows.OfType<VocabularyEntryEditorWindow>());
                    Assert.Same(notebook, editor.Owner); Assert.Equal(2, editor.Draft.Senses.Count);
                    Find<TextBox>(editor, "NoteText").Text = "selected edit"; Click(Find<Button>(editor, "SaveButton")); await editor.LastOperation; completed.TrySetResult();
                }
                catch (Exception error) { completed.TrySetException(error); }
                finally { if (editor is not null) await editor.RetireAsync(); }
            });
            Click(Find<Button>(notebook, "EditButton")); await completed.Task; await Refresh(notebook);
            Assert.Equal(2, store.Entries[0].Senses.Count); Assert.Equal("selected edit", store.Entries[0].Senses[0].Note);
            Assert.Equal(baseline.Senses[1], store.Entries[0].Senses[1]);
        }
        finally { await notebook.RetireAsync(); }
    });
}
