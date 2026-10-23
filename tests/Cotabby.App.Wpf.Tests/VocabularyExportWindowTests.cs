using System.IO;
using System.Windows;
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

/// <summary>Compiled forms and the real Store/archive run on WPF. Only the human picker response
/// is controlled; successful saving uses the native atomic file adapter. No native modal picker,
/// external editor, clipboard, network or model is exercised by these routed-control tests.</summary>
[Collection("WPF views")]
public sealed class VocabularyExportWindowTests(WpfDispatcher dispatcher)
{
    private static Guid Id(int n) => Guid.Parse("00000000-0000-0000-0000-" + n.ToString("x12"));
    private static VocabularyDocument Seed()
    {
        var now = DateTimeOffset.UnixEpoch;
        return new() { Entries = [new(Id(1), "<bank>&", "bank", LearningLanguage.English, VocabularyEntryKind.Word,
            [new VocabularySense(Id(2), "core", now, now) with { Tags = ["Work"] }, new VocabularySense(Id(4), "second", now, now) with { Tags = ["Home"] }], now, now),
            new(Id(10), "café", "café", LearningLanguage.French, VocabularyEntryKind.Word,
                [new VocabularySense(Id(12), "coffee", now, now) with { Tags = ["Work"] }], now, now.AddSeconds(10))] };
    }
    private static async Task<VocabularyStore> Store()
    {
        var archive = new VocabularyArchive((string?)null); await archive.CommitAsync(Seed());
        var store = new VocabularyStore(archive, SynchronizationContext.Current!); await store.LoadAsync(); return store;
    }
    private static VocabularyExportRequest Request() => new([new(Id(1), Id(2)), new(Id(1), Id(4)), new(Id(10), Id(12))],
        [new(Id(1), Id(4))], [], VocabularyExportScope.All);
    private sealed class Picker(Func<VocabularyExportPackage, CancellationToken, Task<string?>> choose) : IVocabularyExportDestinationPicker
    {
        internal int Calls;
        internal VocabularyExportPackage? Captured;
        public Task<string?> ChooseAsync(Window owner, VocabularyExportPackage package, CancellationToken cancellationToken)
        { owner.Dispatcher.VerifyAccess(); Calls++; Captured = package; return choose(package, cancellationToken); }
    }
    private static async Task Prepare(VocabularyExportWindow window)
    { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastPreparation; window.UpdateLayout(); }
    private static async Task Refresh(VocabularyNotebookWindow window)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await window.LastRefresh; window.UpdateLayout();
    }
    private static VocabularySenseRow[] Rows(VocabularyNotebookWindow window) => Find<TreeView>(window, "EntriesTree")
        .Items.Cast<VocabularyEntryRow>().SelectMany(entry => entry.Senses).ToArray();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    [Fact]
    public Task ControlsKeepUserOptionsAndRenderTheSamePreparedLiteralBytes() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var picker = new Picker((_, _) => Task.FromResult<string?>(null));
        var window = new VocabularyExportWindow(store, Request(), picker);
        try
        {
            window.Show(); await Prepare(window);
            Assert.Equal(window.Session.Package!.Preview, Find<TextBox>(window, "PreviewText").Text);
            Assert.Contains("<bank>&", Find<TextBox>(window, "PreviewText").Text); Assert.True(Find<TextBox>(window, "PreviewText").IsReadOnly);
            Find<CheckBox>(window, "MeaningsChoice").IsChecked = false; await Prepare(window);
            Assert.False(window.Session.Configuration.Meanings); Assert.False(window.Session.Package!.Configuration.Meanings);
            Find<CheckBox>(window, "ImagesChoice").IsChecked = true; await Prepare(window);
            Assert.True(window.Session.Configuration.IncludesImages);
            Find<ComboBox>(window, "TextStyleChoice").SelectedValue = VocabularyExportTextStyle.Expressions; await Prepare(window);
            Assert.True(window.Session.Configuration.IsExpressionList); Assert.False(window.Session.Configuration.IncludesImages);
            Assert.Equal(Visibility.Collapsed, Find<GroupBox>(window, "ContentOptions").Visibility);
            Find<ComboBox>(window, "FormatChoice").SelectedValue = VocabularyExportFormat.Csv; await Prepare(window);
            Assert.Equal(Visibility.Visible, Find<CheckBox>(window, "SpreadsheetChoice").Visibility);
            Find<CheckBox>(window, "SpreadsheetChoice").IsChecked = false; await Prepare(window);
            Assert.False(window.Session.Configuration.SpreadsheetCompatibility); Assert.True(window.Session.Configuration.Images);
            Find<CheckBox>(window, "ContentChoice").IsChecked = false; await Prepare(window);
            Assert.False(Find<CheckBox>(window, "ImagesChoice").IsEnabled); Assert.False(window.Session.Configuration.IncludesImages);
            Find<ComboBox>(window, "ScopeChoice").SelectedValue = VocabularyExportScope.Filtered; await Prepare(window);
            Assert.Equal(1, window.Session.Package!.SenseCount); Assert.True(Find<Button>(window, "ExportButton").IsEnabled); Assert.Equal(0, picker.Calls);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task PickerCancellationKeepsPreparedPackageAndAllowsRetry() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var picker = new Picker((_, _) => Task.FromResult<string?>(null));
        var window = new VocabularyExportWindow(store, Request(), picker);
        try
        {
            window.Show(); await Prepare(window); var package = window.Session.Package;
            Click(Find<Button>(window, "ExportButton")); await window.LastOperation;
            Assert.Same(package, picker.Captured); Assert.Same(package, window.Session.Package); Assert.True(window.IsVisible);
            Assert.True(Find<Button>(window, "ExportButton").IsEnabled); Assert.Null(window.Session.Message); Assert.Null(store.ErrorMessage);
            Click(Find<Button>(window, "ExportButton")); await window.LastOperation; Assert.Equal(2, picker.Calls);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task SaveFailureStaysLocalAndRetryWritesCapturedBytesThenCloses() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store(); int calls = 0;
        var picker = new Picker((_, _) => Task.FromResult<string?>(++calls == 1 ? Path.Combine(folder.Export, "missing", "result.txt") : folder.Export));
        var window = new VocabularyExportWindow(store, Request(), picker);
        try
        {
            window.Show(); await Prepare(window); var package = window.Session.Package!; ulong revision = store.Snapshot.Revision;
            Click(Find<Button>(window, "ExportButton")); await window.LastOperation;
            Assert.True(window.IsVisible); Assert.NotNull(window.Session.Message); Assert.Null(store.ErrorMessage); Assert.False(File.Exists(folder.Export));
            Assert.True(Find<Button>(window, "ExportButton").IsEnabled);
            Click(Find<Button>(window, "ExportButton")); await window.LastOperation;
            Assert.False(window.IsVisible); Assert.False(window.Session.IsActive); Assert.Equal(package.CopyData(), File.ReadAllBytes(folder.Export));
            Assert.Equal(revision, store.Snapshot.Revision); Assert.Same(package, picker.Captured);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task RetirementCancelsPickerAndDrainsItsLateReplyWithoutWriting() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store();
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken observed = default;
        var picker = new Picker((_, token) => { observed = token; return reply.Task; });
        var window = new VocabularyExportWindow(store, Request(), picker);
        try
        {
            window.Show(); await Prepare(window); Click(Find<Button>(window, "ExportButton"));
            Assert.False(Find<StackPanel>(window, "FormContent").IsEnabled); Assert.False(Find<Button>(window, "CancelButton").IsEnabled);
            window.Close(); Assert.True(window.IsVisible); // A normal close cannot orphan the chooser.
            var retiring = window.RetireAsync(); Assert.False(window.IsVisible); Assert.False(retiring.IsCompleted); Assert.True(observed.IsCancellationRequested);
            reply.SetResult(folder.Export); await retiring; Assert.False(File.Exists(folder.Export)); Assert.False(window.Session.IsActive);
        }
        finally { reply.TrySetResult(null); await window.RetireAsync(); }
    });
    [Fact]
    public Task StalePickerPackageCannotSaveAfterTheSessionKeyChanges() => dispatcher.RunAsync(async () =>
    {
        using var folder = new VocabularyExportFileTests.Folder(); await using var store = await Store();
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new VocabularyExportWindow(store, Request(), new Picker((_, _) => reply.Task));
        try
        {
            window.Show(); await Prepare(window); Click(Find<Button>(window, "ExportButton"));
            window.Session.Scope = VocabularyExportScope.Filtered; reply.SetResult(folder.Export); await window.LastOperation;
            Assert.False(File.Exists(folder.Export)); Assert.True(window.IsVisible); Assert.False(window.Session.CanExport);
        }
        finally { reply.TrySetResult(null); await window.RetireAsync(); }
    });
    [Fact]
    public Task EmptyScopeShowsFixedMessageAndCannotOpenPicker() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var picker = new Picker((_, _) => Task.FromResult<string?>(null));
        var request = new VocabularyExportRequest(Request().All, Request().Filtered, [], VocabularyExportScope.Selected);
        var window = new VocabularyExportWindow(store, request, picker);
        try
        {
            window.Show(); await Prepare(window); Assert.Null(window.Session.Package);
            Assert.Equal(new VocabularyExportException(VocabularyExportFailure.Empty).Message, Find<TextBox>(window, "FailureText").Text);
            Assert.False(Find<Button>(window, "ExportButton").IsEnabled); Click(Find<Button>(window, "ExportButton")); Assert.Equal(0, picker.Calls);
        }
        finally { await window.RetireAsync(); }
    });
    // Queue a bounded probe before entering ShowDialog. The real nested dispatcher loop runs the
    // child Loaded/prepare path; a finally close prevents an assertion from stranding that loop.
    private static async Task InspectOffer(VocabularyNotebookWindow notebook, Action invoke, Action<VocabularyExportRequest> inspect)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = notebook.Dispatcher.BeginInvoke(new Action(async () =>
        {
            VocabularyExportWindow? child = null;
            try
            {
                child = Assert.Single(notebook.OwnedWindows.OfType<VocabularyExportWindow>());
                await Prepare(child); inspect(child.Session.Request); completed.SetResult();
            }
            catch (Exception error) { completed.TrySetException(error); }
            finally { if (child is not null) await child.RetireAsync(); }
        }), DispatcherPriority.ApplicationIdle);
        invoke(); await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    [Fact]
    public Task NotebookBatchPersistsAcrossFiltersAndChecklistModeAndExportsInBrowseOrder() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            notebook.Show(); await Refresh(notebook); Find<ToggleButton>(notebook, "SelectSensesButton").IsChecked = true; await Refresh(notebook);
            Rows(notebook).Single(row => row.Selection.SenseId == Id(2)).IsBatchSelected = true;
            Find<ComboBox>(notebook, "TagFilter").SelectedValue = "Work"; await Refresh(notebook);
            Click(Find<Button>(notebook, "SelectVisibleButton")); Assert.All(Rows(notebook), row => Assert.True(row.IsBatchSelected));
            Find<TextBox>(notebook, "SearchText").Text = "café"; await Refresh(notebook);
            Find<ToggleButton>(notebook, "SelectSensesButton").IsChecked = false; await Refresh(notebook);
            await InspectOffer(notebook, () => Click(Find<Button>(notebook, "ExportButton")), request =>
            {
                Assert.Equal(VocabularyExportScope.Selected, request.InitialScope);
                Assert.Equal(new[] { new VocabularySelection(Id(10), Id(12)), new(Id(1), Id(2)) }, request.Selected);
                Assert.Equal(new[] { new VocabularySelection(Id(10), Id(12)) }, request.Filtered); Assert.Equal(3, request.All.Count);
            });
            Find<ToggleButton>(notebook, "SelectSensesButton").IsChecked = true; await Refresh(notebook);
            Assert.True(Assert.Single(Rows(notebook)).IsBatchSelected); Assert.True(Find<Button>(notebook, "ExportSelectedButton").IsEnabled);
        }
        finally { await notebook.RetireAsync(); }
    });
    [Fact]
    public Task DetachedRowsCannotChangeBatchAfterRefreshAndHiddenNotebookCannotExport() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            notebook.Show(); await Refresh(notebook); Find<ToggleButton>(notebook, "SelectSensesButton").IsChecked = true; await Refresh(notebook);
            var detached = Rows(notebook).Single(row => row.Selection.SenseId == Id(2));
            Find<TextBox>(notebook, "SearchText").Text = "café"; Assert.False(Find<Button>(notebook, "ExportButton").IsEnabled);
            detached.IsBatchSelected = true; await Refresh(notebook);
            await InspectOffer(notebook, () => Click(Find<Button>(notebook, "ExportButton")), request =>
            { Assert.Equal(VocabularyExportScope.Filtered, request.InitialScope); Assert.Empty(request.Selected); });
            notebook.Close(); Assert.False(Find<Button>(notebook, "ExportButton").IsEnabled);
            Click(Find<Button>(notebook, "ExportButton")); Assert.Empty(notebook.OwnedWindows.OfType<VocabularyExportWindow>());
        }
        finally { await notebook.RetireAsync(); }
    });
    [Fact]
    public Task SenseMenuFreezesExactTargetAndItsReceiptExpiresAfterRefresh() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            notebook.Reveal(new(Id(1), Id(2))); notebook.Show(); await Refresh(notebook);
            var row = Descendants<StackPanel>(notebook).Single(panel => panel.DataContext is VocabularySenseRow sense && sense.Selection.SenseId == Id(2) && panel.ContextMenu is not null);
            var menu = row.ContextMenu; var item = Assert.IsType<MenuItem>(menu.Items[0]);
            menu.PlacementTarget = row; menu.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.NotNull(item.Tag);
            await InspectOffer(notebook, () => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), request =>
            { Assert.Equal(VocabularyExportScope.Selected, request.InitialScope); Assert.Equal(new[] { new VocabularySelection(Id(1), Id(2)) }, request.Selected); });
            menu.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.NotNull(item.Tag);
            Find<TextBox>(notebook, "SearchText").Text = "café"; await Refresh(notebook);
            Assert.Null(item.Tag); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Empty(notebook.OwnedWindows.OfType<VocabularyExportWindow>());
        }
        finally { await notebook.RetireAsync(); }
    });
    [Fact]
    public Task OpenExportKeepsFrozenIdsWhenLiveNotebookDeletesASense() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var window = new VocabularyExportWindow(store, Request(), new Picker((_, _) => Task.FromResult<string?>(null)));
        try
        {
            window.Show(); await Prepare(window); var original = window.Session.Package;
            Assert.True(await store.RemoveSenseAsync(Id(1), Id(2)));
            Assert.Equal(3, window.Session.Request.All.Count); Assert.Same(original, window.Session.Package);
            Find<ComboBox>(window, "FormatChoice").SelectedValue = VocabularyExportFormat.Json; await Prepare(window);
            Assert.Null(window.Session.Package); Assert.False(Find<Button>(window, "ExportButton").IsEnabled);
            Assert.Equal(new VocabularyExportException(VocabularyExportFailure.Changed).Message, Find<TextBox>(window, "FailureText").Text);
        }
        finally { await window.RetireAsync(); }
    });
    [Fact]
    public Task NotebookRetirementClosesAndDrainsItsOwnedExportDialog() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            notebook.Show(); await Refresh(notebook);
            _ = notebook.Dispatcher.BeginInvoke(new Action(async () =>
            {
                VocabularyExportWindow? child = null;
                try
                {
                    child = Assert.Single(notebook.OwnedWindows.OfType<VocabularyExportWindow>()); await Prepare(child);
                    Assert.Same(notebook, child.Owner); await notebook.RetireAsync();
                    Assert.False(child.IsVisible); Assert.False(child.Session.IsActive); Assert.False(notebook.IsVisible); completed.TrySetResult();
                }
                catch (Exception error) { completed.TrySetException(error); }
                finally { if (child is not null) await child.RetireAsync(); }
            }), DispatcherPriority.ApplicationIdle);
            Click(Find<Button>(notebook, "ExportButton")); await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { await notebook.RetireAsync(); }
    });
    [Fact]
    public Task DeletedBatchStillOffersMacEmptySelectionFeedback() => dispatcher.RunAsync(async () =>
    {
        await using var store = await Store(); var notebook = new VocabularyNotebookWindow(store, new WindowsVocabularyTextSearch());
        try
        {
            notebook.Show(); await Refresh(notebook); Find<ToggleButton>(notebook, "SelectSensesButton").IsChecked = true; await Refresh(notebook);
            Click(Find<Button>(notebook, "SelectVisibleButton"));
            foreach (var row in Rows(notebook)) Assert.True(await store.RemoveSenseAsync(row.Selection.EntryId, row.Selection.SenseId));
            await Refresh(notebook); Assert.Empty(store.Entries); Assert.False(Find<Button>(notebook, "ExportButton").IsEnabled);
            Assert.True(Find<Button>(notebook, "ExportSelectedButton").IsEnabled);
            await InspectOffer(notebook, () => Click(Find<Button>(notebook, "ExportSelectedButton")), request =>
            { Assert.Equal(VocabularyExportScope.Selected, request.InitialScope); Assert.Empty(request.Selected); Assert.Empty(request.All); });
        }
        finally { await notebook.RetireAsync(); }
    });
}
