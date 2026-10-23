using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.App.UI.Learning;
using Cotabby.App.UI.Vocabulary;
using Cotabby.App.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;
using static Cotabby.App.Wpf.Tests.LearningViewProbe;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Compiled favorite controls around the real in-memory archive, without model/editor IO.</summary>
[Collection("WPF views")]
public sealed class VocabularyFavoriteViewTests(WpfDispatcher dispatcher)
{
    private static VocabularyCapture Capture(string text = "The bank.", int offset = 4) => new("bank", "bank", LearningLanguage.English,
        VocabularyEntryKind.Word, Example: new(Guid.NewGuid(), text, "原句", [new(offset, 4)], VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch));
    private static VocabularyStore Store(VocabularyArchive archive) => new(archive, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
    private static void ClickMenu(MenuItem menu) => menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static async Task Settle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    [Fact]
    public Task ComponentSaveAndRemovePreserveTheParentPatternEncounter() => dispatcher.RunAsync(async () =>
    {
        var window = new LearningDictionaryWindow(); await using var store = Store(new VocabularyArchive((string?)null));
        var unit = Unit; var word = unit.Words[^1];
        var usage = new VocabularyExample(Guid.NewGuid(), Preview.Result!, Preview.Source,
            unit.Ranges.Select(range => new VocabularyTextRange(range.Location, range.Length)).ToArray(),
            VocabularyExampleCompleteness.Sentence, "", DateTimeOffset.UnixEpoch) { TranslationLanguage = LearningLanguage.Chinese };
        var parent = new VocabularyCapture(unit.Query, unit.Query, LearningLanguage.English, VocabularyEntryKind.Pattern, Example: usage);
        var component = VocabularyCaptureFactory.FromComponent(word, unit, parent)!;
        try
        {
            Assert.True(await store.SaveAsync(parent));
            var state = Dictionary with { SelectedWord = word, Explanation = new() { Input = LearningExplanationPolicy.Input(component) } };
            window.ShowState(state, Anchor); window.BindVocabulary(store, component, state.TargetIdentity, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            Click(button); await button.LastOperation; await Settle();
            Assert.True(store.Contains(parent)); Assert.True(store.Contains(component)); Assert.Equal(2, store.Entries.Count);
            var saved = store.Entries.Single(entry => entry.Kind == VocabularyEntryKind.Word).Examples[0];
            Assert.NotEqual(usage.Id, saved.Id); Assert.Equal(word.Range.Location, Assert.Single(saved.Ranges).Location);
            Assert.Equal(usage.Translation, saved.Translation);
            Click(button); ClickMenu(Assert.Single(button.ContextMenu!.Items.OfType<MenuItem>())); await button.LastOperation; await Settle();
            Assert.True(store.Contains(parent)); Assert.False(store.Contains(component)); Assert.Equal(usage.Id, Assert.Single(Assert.Single(store.Entries).Examples).Id);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task AcknowledgedSavePaintsSavedAndMenuRemovesOnlyThatEncounter() => dispatcher.RunAsync(async () =>
    {
        var window = new LearningPreviewWindow(); await using var store = Store(new VocabularyArchive((string?)null));
        var capture = Capture(); var other = Capture("My bank.", 3);
        try
        {
            Assert.True(await store.SaveAsync(other)); window.ShowState(Preview, Anchor, false);
            window.BindVocabulary(store, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            Assert.Equal("Not saved", AutomationProperties.GetItemStatus(button)); Click(button); await button.LastOperation; await Settle();
            Assert.True(store.Contains(capture)); Assert.True(store.Contains(other)); Assert.Equal("Saved", AutomationProperties.GetItemStatus(button));
            AssertNonactivating(window); AssertPaints(window);
            Click(button); var remove = Assert.Single(button.ContextMenu!.Items.OfType<MenuItem>());
            Assert.Equal("Remove this saved usage", remove.Header); ClickMenu(remove); await button.LastOperation; await Settle();
            Assert.False(store.Contains(capture)); Assert.True(store.Contains(other));
            Assert.Equal(other.Example!.Id, Assert.Single(Assert.Single(store.Entries).Examples).Id);
            Assert.Equal("Not saved", AutomationProperties.GetItemStatus(button));
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task PendingWriteNeverPaintsSavedAndRetiredBindingCannotIssueAnotherSave() => dispatcher.RunAsync(async () =>
    {
        var archive = new VocabularyArchive((string?)null); await using var store = Store(archive); var capture = Capture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var window = new LearningPreviewWindow();
        Task<bool>? writing = null;
        try
        {
            window.ShowState(Preview, Anchor, false); window.BindVocabulary(store, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            writing = store.Change(capture, async () => { await gate.Task; return await archive.SaveAsync(capture); }); await Settle();
            Assert.False(store.Contains(capture)); Assert.False(button.IsEnabled); Assert.IsType<ProgressBar>(button.Content);
            Assert.Equal("Saving…", AutomationProperties.GetItemStatus(button));
            gate.SetResult(); Assert.True(await writing); await Settle(); Assert.Equal("Saved", AutomationProperties.GetItemStatus(button));
            var before = store.Snapshot.Revision; window.HidePreview(); Click(button); await button.LastOperation;
            Assert.Equal(Visibility.Collapsed, button.Visibility); Assert.Equal(before, store.Snapshot.Revision); Assert.Null(button.ContextMenu);
        }
        finally { gate.TrySetResult(); if (writing is not null) await writing; window.Close(); }
    });
    [Fact]
    public Task FailedArchiveSaveShowsFixedErrorAndDoesNotClaimSaved() => dispatcher.RunAsync(async () =>
    {
        var window = new LearningPreviewWindow(); await using var store = Store(new VocabularyArchive((string?)null));
        var capture = Capture() with { Expression = "", DictionaryForm = "" };
        try
        {
            window.ShowState(Preview, Anchor, false); window.BindVocabulary(store, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            Click(button); await button.LastOperation; await Settle();
            Assert.False(store.Contains(capture)); Assert.Empty(store.Entries); Assert.Equal("Not saved", AutomationProperties.GetItemStatus(button));
            Assert.Equal(store.ErrorMessage, Find<TextBlock>(window, "VocabularyStatus").Text);
            Assert.Equal(Visibility.Visible, Find<TextBlock>(window, "VocabularyStatus").Visibility);
            Assert.Equal(store.ErrorMessage, AutomationProperties.GetHelpText(button));
            window.ShowState(Preview with { RequestId = "new" }, Anchor, false);
            Assert.Equal(Visibility.Collapsed, button.Visibility); Assert.Equal(Visibility.Collapsed, Find<TextBlock>(window, "VocabularyStatus").Visibility);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task SavedActionsNavigateStableSenseAndOldMenuCannotDeleteANewCard() => dispatcher.RunAsync(async () =>
    {
        var window = new LearningPreviewWindow(); await using var store = Store(new VocabularyArchive((string?)null)); var capture = Capture();
        try
        {
            Assert.True(await store.SaveAsync(capture)); VocabularySelection? opened = null; store.OpenSelection = value => opened = value;
            window.ShowState(Preview, Anchor, false); window.BindVocabulary(store, capture, Preview.RequestId, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            Click(button); var menu = button.ContextMenu!; var show = (MenuItem)menu.Items[0]; var remove = (MenuItem)menu.Items[1];
            Assert.Equal("Show in vocabulary", show.Header); ClickMenu(show);
            var destination = store.ExplanationDestination(capture)!.Value; Assert.Equal(new VocabularySelection(destination.EntryId, destination.SenseId), opened);
            window.ShowState(Preview with { RequestId = "next" }, Anchor, false); ClickMenu(remove); await button.LastOperation;
            Assert.True(store.Contains(capture)); Assert.False(menu.IsOpen);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task DictionaryNavigationAndCloseRetireTheFavoriteCapture() => dispatcher.RunAsync(async () =>
    {
        var window = new LearningDictionaryWindow(); await using var store = Store(new VocabularyArchive((string?)null)); var capture = Capture();
        try
        {
            var state = Dictionary; window.ShowState(state, Anchor); window.BindVocabulary(store, capture, state.TargetIdentity, () => true);
            var button = Find<VocabularyFavoriteButton>(window, "FavoriteButton"); await button.LastOperation;
            Assert.Equal(Visibility.Visible, button.Visibility); window.ShowState(state with { TargetIdentity = Guid.NewGuid() }, Anchor);
            Assert.Equal(Visibility.Collapsed, button.Visibility); Click(button); Assert.False(store.Contains(capture));
            window.BindVocabulary(store, capture, state.TargetIdentity, () => true); Assert.Equal(Visibility.Collapsed, button.Visibility);
            AssertNonactivating(window); window.HideCard(); Assert.Null(button.Content); Assert.Equal("", AutomationProperties.GetItemStatus(button)); Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });
}
