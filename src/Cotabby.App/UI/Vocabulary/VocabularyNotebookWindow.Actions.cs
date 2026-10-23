using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Notebook-owned command presentation from Mac's sense and usage actions. Stable receipts name
/// exact owners; Store/Archive remain the sole mutation authority. Detached menus retire on
/// target change/hide. Cancellation stops presentation only, while admitted transactions drain.
/// This partial boundary keeps durable command sequencing separate from filtering and rendering.
/// </summary>
public partial class VocabularyNotebookWindow
{
    private readonly CancellationTokenSource _actionsLifetime = new();
    private readonly List<ContextMenu> _actionMenus = [];
    private VocabularyDeleteSenseWindow? _confirmation;
    private bool _mutating;
    private int _actionsRetired;
    private long _detailVersion, _menuEpoch;
    private sealed record MutationResult(bool Succeeded, Guid? Selection);
    internal Task LastMutation { get; private set; } = Task.CompletedTask;

    private (VocabularyEntry Entry, VocabularySense Sense)? CurrentTarget()
    {
        if (Selection is not { } selection) return null;
        var entry = _store.Snapshot.Document.Entries.FirstOrDefault(value => value.Id == selection.EntryId);
        var sense = entry?.Senses.FirstOrDefault(value => value.Id == selection.SenseId);
        return entry is null || sense is null ? null : (entry, sense);
    }
    private bool CanAct(VocabularySelection receipt, long version) => !_retired && !_mutating && IsVisible
        && version == _detailVersion && Selection == receipt;
    private void UpdateActions()
    {
        var target = CurrentTarget(); bool enabled = target is not null && !_mutating && !_retired;
        EditButton.IsEnabled = enabled; NewSenseButton.IsEnabled = enabled; DeleteSenseButton.IsEnabled = enabled;
        MergeSenseButton.IsEnabled = enabled && target!.Value.Entry.Senses.Count > 1;
        DetailContent.IsEnabled = !_mutating;
    }
    private void OnNewSense(object sender, RoutedEventArgs args)
    {
        if (CurrentTarget() is not { } target) return;
        var receipt = new VocabularySelection(target.Entry.Id, target.Sense.Id);
        BeginMutation(receipt, _detailVersion, async () =>
        {
            var id = await _store.CreateSenseAsync(receipt.EntryId).ConfigureAwait(false);
            return new(id is not null, id);
        });
    }
    private void OnMergeSense(object sender, RoutedEventArgs args)
    {
        if (CurrentTarget() is not { } target) return;
        var receipt = new VocabularySelection(target.Entry.Id, target.Sense.Id); long version = _detailVersion;
        if (!CanAct(receipt, version)) return;
        var menu = NewMenu(); long epoch = _menuEpoch;
        foreach (var sense in target.Entry.Senses.Where(sense => sense.Id != receipt.SenseId))
        {
            var item = new MenuItem { Header = VocabularyNotebookPolicy.DisplayTitle(sense) };
            item.Click += (_, _) =>
            {
                if (epoch != _menuEpoch) return;
                menu.IsOpen = false; BeginMutation(receipt, version, async () => new(
                    await _store.MergeSensesAsync(receipt.EntryId, receipt.SenseId, sense.Id).ConfigureAwait(false), sense.Id));
            };
            menu.Items.Add(item);
        }
        MergeSenseButton.ContextMenu = menu; menu.PlacementTarget = MergeSenseButton; menu.IsOpen = true;
    }
    private void OnDeleteSense(object sender, RoutedEventArgs args)
    {
        if (_confirmation is not null || CurrentTarget() is not { } target) return;
        var receipt = new VocabularySelection(target.Entry.Id, target.Sense.Id); long version = _detailVersion;
        if (!CanAct(receipt, version)) return;
        var confirmation = new VocabularyDeleteSenseWindow { Owner = this }; _confirmation = confirmation;
        bool confirmed;
        try { confirmed = confirmation.ShowDialog() == true; }
        finally { _confirmation = null; }
        if (confirmed) BeginMutation(receipt, version, async () => new(
            await _store.RemoveSenseAsync(receipt.EntryId, receipt.SenseId).ConfigureAwait(false), null));
    }
    private Button MoveUsageButton(VocabularySelection receipt, Guid usageId)
    {
        long version = _detailVersion; var button = new Button { Content = "Move usage", HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 8, 0, 0), Tag = usageId };
        button.Click += (_, _) =>
        {
            if (!CanAct(receipt, version) || CurrentTarget() is not { } target || !target.Sense.Examples.Any(value => value.Id == usageId)) return;
            var menu = NewMenu(); long epoch = _menuEpoch;
            void Add(string title, Guid? destination)
            {
                var item = new MenuItem { Header = title };
                item.Click += (_, _) =>
                {
                    if (epoch != _menuEpoch) return;
                    menu.IsOpen = false; BeginMutation(receipt, version, async () => new(
                        await _store.MoveExampleAsync(receipt.EntryId, usageId, receipt.SenseId, destination).ConfigureAwait(false), receipt.SenseId));
                };
                menu.Items.Add(item);
            }
            foreach (var sense in target.Entry.Senses.Where(value => value.Id != receipt.SenseId)) Add(VocabularyNotebookPolicy.DisplayTitle(sense), sense.Id);
            Add("New sense", null); button.ContextMenu = menu; menu.PlacementTarget = button; menu.IsOpen = true;
        };
        return button;
    }
    private ContextMenu NewMenu() { CloseActionMenus(); var menu = new ContextMenu(); _actionMenus.Add(menu); return menu; }
    private void CloseActionMenus()
    {
        ++_menuEpoch;
        foreach (var menu in _actionMenus)
        {
            menu.IsOpen = false;
            if (menu.PlacementTarget is FrameworkElement owner && ReferenceEquals(owner.ContextMenu, menu)) owner.ContextMenu = null;
            menu.PlacementTarget = null;
        }
        _actionMenus.Clear(); MergeSenseButton.ContextMenu = null;
    }
    private void BeginMutation(VocabularySelection receipt, long version, Func<Task<MutationResult>> operation)
    {
        if (!CanAct(receipt, version)) return;
        CloseActionMenus(); _mutating = true; UpdateActions();
        LastMutation = RunMutation(receipt, version, operation, _actionsLifetime.Token); Track(LastMutation);
    }
    private async Task RunMutation(VocabularySelection receipt, long version, Func<Task<MutationResult>> operation, CancellationToken token)
    {
        MutationResult result; string? failure;
        try { result = await operation().ConfigureAwait(false); failure = _store.ErrorMessage; }
        catch (Exception) { result = new(false, null); failure = new VocabularyException(VocabularyFailure.InvalidContent).Message; }
        if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_retired) return;
                _mutating = false;
                // A later user selection owns browsing. A completed transaction may update its
                // graph, but cannot pull the user back from a different sense they selected.
                if (result.Succeeded && _detailVersion == version && _selection == receipt.SenseId) _selection = result.Selection;
                if (result.Succeeded) _expanded.Add(receipt.EntryId);
                UpdateActions(); RequestRefresh();
                if (!result.Succeeded && failure is { } message) { StorageError.Text = message; StorageError.Visibility = Visibility.Visible; }
            }, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
    private void RetireActions()
    {
        CloseActionMenus(); _confirmation?.Close();
        if (Interlocked.Exchange(ref _actionsRetired, 1) != 0) return;
        _actionsLifetime.Cancel();
        _ = DrainAsync().ContinueWith(_ => _actionsLifetime.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
