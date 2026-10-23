using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real notebook commands, confirmation/menu receipts and memory archive mutations on WPF.</summary>
[Collection("WPF views")]
public sealed class VocabularySenseActionViewTests(WpfDispatcher dispatcher)
{
    private static VocabularyCapture Capture(string text = "The bank.") => new("bank", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
        Example: new(Guid.NewGuid(), text, "", [new(text.IndexOf("bank", StringComparison.Ordinal), 4)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch));
    private static VocabularyStore Store(VocabularyArchive archive) => new(archive, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    private static async Task Refresh(VocabularyNotebookWindow window)
    { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; window.UpdateLayout(); }
    private static void ClickMenu(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static Button MoveButton(VocabularyNotebookWindow window, Guid usage) => Assert.Single(Descendants<Button>(window), value => value.Tag is Guid id && id == usage);
    private static VocabularySelection At(VocabularyEntry entry, int index = 0) => new(entry.Id, entry.Senses[index].Id);
    private static async Task TwoSenses(VocabularyStore store)
    { Assert.True(await store.SaveAsync(Capture())); Assert.True(await store.SaveAsync(Capture("My bank."))); }
    [Fact]
    public Task NewSenseUsesAcknowledgedIdentityAndRetainsExistingOwnership() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); Assert.True(await store.SaveAsync(Capture())); var before = store.Entries[0];
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(At(before)); window.Show(); await Refresh(window); Assert.False(Find<Button>(window, "MergeSenseButton").IsEnabled);
            Click(Find<Button>(window, "NewSenseButton")); await window.LastMutation; await Refresh(window);
            var saved = store.Entries[0]; Assert.Equal(2, saved.Senses.Count); Assert.Equal(before.Senses[0], saved.Senses[0]);
            Assert.NotEqual(before.Senses[0].Id, saved.Senses[1].Id); Assert.Equal(At(saved, 1), window.Selection);
            Assert.Empty(saved.Senses[1].Examples); Assert.Equal("Meaning pending", VocabularyNotebookPolicy.DisplayTitle(saved.Senses[1]));
            Assert.True(Find<Button>(window, "MergeSenseButton").IsEnabled);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task MergeMenuPreservesUsageIdsAndAliasesAndCannotReplay() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await TwoSenses(store); var before = store.Entries[0]; var target = At(before, 1);
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(At(before)); window.Show(); await Refresh(window); var button = Find<Button>(window, "MergeSenseButton"); Click(button);
            var menu = button.ContextMenu!; var item = Assert.Single(menu.Items.OfType<MenuItem>()); ClickMenu(item); await window.LastMutation; await Refresh(window);
            var saved = Assert.Single(store.Entries[0].Senses); Assert.Equal(target.SenseId, saved.Id); Assert.Contains(before.Senses[0].Id, saved.MergedFrom);
            Assert.Equal(before.Senses[1].Examples.Concat(before.Senses[0].Examples).Select(value => value.Id), saved.Examples.Select(value => value.Id));
            Assert.Equal(target, window.Selection); var revision = store.Snapshot.Revision; ClickMenu(item); await window.LastMutation; Assert.Equal(revision, store.Snapshot.Revision); Assert.False(menu.IsOpen);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task MoveMenusKeepOneUsageOwnerAndRetainTheEmptySourceSense() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await TwoSenses(store); var before = store.Entries[0];
        var source = At(before); var target = At(before, 1); var usage = before.Senses[0].Examples[0]; var secondUsage = before.Senses[1].Examples[0];
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(source); window.Show(); await Refresh(window); var move = MoveButton(window, usage.Id); Click(move);
            ClickMenu((MenuItem)move.ContextMenu!.Items[0]); await window.LastMutation; await Refresh(window);
            var saved = store.Entries[0]; Assert.Empty(saved.Senses.Single(value => value.Id == source.SenseId).Examples);
            Assert.Equal(usage, saved.Senses.Single(value => value.Id == target.SenseId).Examples.Single(value => value.Id == usage.Id)); Assert.Equal(source, window.Selection);
            window.Reveal(target); await Refresh(window); move = MoveButton(window, secondUsage.Id); Click(move);
            ClickMenu(Assert.Single(move.ContextMenu!.Items.OfType<MenuItem>(), value => value.Header as string == "New sense")); await window.LastMutation; await Refresh(window);
            saved = store.Entries[0]; Assert.Equal(3, saved.Senses.Count); var owner = Assert.Single(saved.Senses, value => value.Examples.Any(example => example.Id == secondUsage.Id));
            Assert.NotEqual(source.SenseId, owner.Id); Assert.NotEqual(target.SenseId, owner.Id); Assert.Equal(secondUsage, Assert.Single(owner.Examples));
            Assert.Equal(2, saved.Examples.Select(value => value.Id).Distinct().Count()); Assert.Equal(target, window.Selection);
        }
        finally { await window.RetireAsync(); }
    });
    private static async Task Delete(VocabularyNotebookWindow window, bool confirm)
    {
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
        {
            VocabularyDeleteSenseWindow? dialog = null;
            try
            {
                dialog = Assert.Single(Application.Current.Windows.OfType<VocabularyDeleteSenseWindow>()); Assert.Same(window, dialog.Owner);
                Click(Find<Button>(dialog, confirm ? "DeleteButton" : "CancelButton")); answered.TrySetResult();
            }
            catch (Exception error) { answered.TrySetException(error); }
            finally { dialog?.Close(); }
        });
        Click(Find<Button>(window, "DeleteSenseButton")); await answered.Task; await window.LastMutation; await Refresh(window);
    }
    [Fact]
    public Task DeleteNeedsExplicitConfirmationAndRemovesOnlyTheNamedSense() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await TwoSenses(store); var before = store.Entries[0];
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(At(before)); window.Show(); await Refresh(window); ulong revision = store.Snapshot.Revision;
            await Delete(window, false); Assert.Equal(revision, store.Snapshot.Revision); Assert.Equal(before, store.Entries[0]);
            await Delete(window, true); Assert.Equal(before.Senses[1], Assert.Single(store.Entries[0].Senses)); Assert.Null(window.Selection);
            Assert.False(Find<Button>(window, "DeleteSenseButton").IsEnabled);
            window.Reveal(At(before, 1)); await Refresh(window); await Delete(window, true); Assert.Empty(store.Entries); Assert.Null(window.Selection);
            Assert.Equal("No saved expressions", Find<TextBlock>(window, "EmptyText").Text);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task MenusAndDetachedUsageButtonsRetireOnSelectionAndHide() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await TwoSenses(store); var before = store.Entries[0];
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(At(before)); window.Show(); await Refresh(window); var oldButton = MoveButton(window, before.Examples[0].Id);
            Click(oldButton); Assert.NotNull(oldButton.ContextMenu);
            Click(Find<Button>(window, "MergeSenseButton")); var oldMenu = Find<Button>(window, "MergeSenseButton").ContextMenu!; var oldItem = (MenuItem)oldMenu.Items[0];
            Assert.Null(oldButton.ContextMenu);
            window.Reveal(At(before, 1)); await Refresh(window); ulong revision = store.Snapshot.Revision;
            ClickMenu(oldItem); Click(oldButton); Assert.Null(oldButton.ContextMenu); Assert.Equal(revision, store.Snapshot.Revision);
            var currentMove = MoveButton(window, before.Senses[1].Examples[0].Id); Click(currentMove); oldMenu = currentMove.ContextMenu!; oldItem = (MenuItem)oldMenu.Items[0];
            window.Close(); Assert.False(oldMenu.IsOpen); Assert.Null(currentMove.ContextMenu); ClickMenu(oldItem); Assert.Equal(revision, store.Snapshot.Revision);
            window.Reveal(null); window.Show(); await Refresh(window); Click(Find<Button>(window, "MergeSenseButton"));
            ClickMenu((MenuItem)Find<Button>(window, "MergeSenseButton").ContextMenu!.Items[0]); await window.LastMutation; await Refresh(window);
            Assert.Single(store.Entries[0].Senses);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task MissingMoveTargetShowsFixedFailureAndSuccessfulCommandRecovers() => dispatcher.RunAsync(async () =>
    {
        await using var store = Store(new VocabularyArchive((string?)null)); await TwoSenses(store); var before = store.Entries[0]; var usage = before.Examples[0];
        var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(At(before)); window.Show(); await Refresh(window); var button = MoveButton(window, usage.Id); Click(button); var item = (MenuItem)button.ContextMenu!.Items[0];
            Assert.True(await store.RemoveSenseAsync(before.Id, before.Senses[1].Id)); await Refresh(window);
            ClickMenu(item); await window.LastMutation; await Refresh(window);
            Assert.Equal(usage, Assert.Single(store.Entries[0].Examples)); Assert.Equal(store.ErrorMessage, Find<TextBlock>(window, "StorageError").Text);
            Assert.Equal(Visibility.Visible, Find<TextBlock>(window, "StorageError").Visibility);
            Click(Find<Button>(window, "NewSenseButton")); await window.LastMutation; await Refresh(window);
            Assert.Equal(2, store.Entries[0].Senses.Count); Assert.Null(store.ErrorMessage); Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "StorageError").Visibility);
        }
        finally { await window.RetireAsync(); }
    });
    private sealed class ClockGate : TimeProvider
    {
        public bool Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() { if (Block) { Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); } return DateTimeOffset.UnixEpoch; }
    }
    [Fact]
    public Task CompletedMutationCannotOverrideALaterVisitBackToTheOriginalSense() => dispatcher.RunAsync(async () =>
    {
        var clock = new ClockGate(); await using var store = Store(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, clock)); await TwoSenses(store);
        var before = store.Entries[0]; var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(At(before)); window.Show(); await Refresh(window); clock.Block = true; Click(Find<Button>(window, "NewSenseButton"));
            await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.Reveal(At(before, 1)); await Refresh(window); window.Reveal(At(before)); await Refresh(window);
            clock.Release.SetResult(); await window.LastMutation; await Refresh(window);
            Assert.Equal(3, store.Entries[0].Senses.Count); Assert.Equal(At(before), window.Selection);
        }
        finally { clock.Release.TrySetResult(); await window.RetireAsync(); }
    });
    [Fact]
    public Task RetirementDrainsAnAdmittedSenseMutationWithoutAnotherUiCallback() => dispatcher.RunAsync(async () =>
    {
        var clock = new ClockGate(); await using var store = Store(new VocabularyArchive(VocabularySQLiteDatabase.CreateMemory, clock)); Assert.True(await store.SaveAsync(Capture()));
        var before = store.Entries[0]; var window = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            window.Reveal(At(before)); window.Show(); await Refresh(window); clock.Block = true; Click(Find<Button>(window, "NewSenseButton"));
            await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(Find<Button>(window, "NewSenseButton").IsEnabled);
            var retirement = window.RetireAsync(); Assert.False(window.IsVisible); Assert.False(retirement.IsCompleted);
            clock.Release.SetResult(); await retirement; Assert.Equal(2, store.Entries[0].Senses.Count); Assert.Equal(before.Senses[0], store.Entries[0].Senses[0]);
        }
        finally { clock.Release.TrySetResult(); await window.RetireAsync(); }
    });
}
