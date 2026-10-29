using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary.Notebook;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Detail commands borrow the canonical owner. UI receipts prevent a stale menu or an
/// acknowledged operation from redirecting a later user selection. Accepted writes are tracked
/// until completion, including while the notebook is being retired.</summary>
internal sealed partial class FrozenVocabularyNotebookWindow
{
    private bool _senseMutating;
    private TaskCompletionSource? _senseCompletion;
    private long _actionSelectionEpoch, _actionMenuEpoch;
    private string? _actionError;
    private readonly List<ContextMenu> _senseMenus = [];
    internal static readonly Style SenseMenuStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ContextMenu}">
          <Setter Property="Background" Value="{DynamicResource CardBg}" /><Setter Property="BorderBrush" Value="{DynamicResource CardBorder}" /><Setter Property="Foreground" Value="{DynamicResource TextPrimary}" />
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ContextMenu}">
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="3"><ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" CanContentScroll="True"><ItemsPresenter /></ScrollViewer></Border>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    internal static readonly Style SenseMenuItemStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type MenuItem}">
          <Setter Property="Foreground" Value="{DynamicResource TextPrimary}" /><Setter Property="Padding" Value="12,7" />
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type MenuItem}">
            <Border x:Name="row" Background="Transparent" BorderBrush="Transparent" BorderThickness="2,0,0,0" CornerRadius="4" Padding="{TemplateBinding Padding}"><ContentPresenter ContentSource="Header" /></Border>
            <ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="row" Property="Background" Value="{DynamicResource HoverBg}" /><Setter TargetName="row" Property="BorderBrush" Value="{DynamicResource AccentBrush}" /></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    private bool CanSenseAct(VocabularySelection source, long epoch) => !_retiring && !_senseMutating && !_semanticBusy && _modalDone is null && IsVisible
        && _store is ICanonicalVocabularySenseStore && _session.Selection == source && epoch == _actionSelectionEpoch;
    private void AddSenseActions(Panel panel, F.VocabularyEntry entry, F.VocabularySense sense)
    {
        var source = new VocabularySelection(entry.Id, sense.Id); long epoch = _actionSelectionEpoch; bool enabled = CanSenseAct(source, epoch);
        var create = SenseButton("New sense", "frozenNewSense", enabled); create.Click += (_, _) => LastOperation = Track(SenseActionAsync(source, epoch, async store => (await store.CreateSenseAsync(entry.Id, CancellationToken.None)).SenseId, true)); panel.Children.Add(create);
        var merge = SenseButton("Merge sense", "frozenMergeSense", enabled && entry.Senses.Count > 1); merge.Click += (_, _) =>
        {
            if (!CanSenseAct(source, epoch)) return; var menu = NewSenseMenu(merge); long menuEpoch = _actionMenuEpoch;
            foreach (var target in entry.Senses.Where(s => s.Id != sense.Id))
            {
                var item = SenseMenuItem(SenseTitle(target), target.Id); item.Click += (_, _) =>
                {
                    if (menuEpoch != _actionMenuEpoch || !CanSenseAct(source, epoch)) return; menu.IsOpen = false;
                    LastOperation = Track(SenseActionAsync(source, epoch, async store => { await store.MergeSensesAsync(entry.Id, sense.Id, target.Id, CancellationToken.None); return target.Id; }, true));
                }; menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }; panel.Children.Add(merge);
        var delete = SenseButton("Delete", "frozenDeleteSense", enabled); delete.Click += (_, _) => LastOperation = Track(DeleteSenseAsync(source, epoch)); panel.Children.Add(delete);
    }
    private Button MoveUsageButton(F.VocabularyEntry entry, F.VocabularySense sense, F.VocabularyExample example)
    {
        var source = new VocabularySelection(entry.Id, sense.Id); long epoch = _actionSelectionEpoch;
        var button = SenseButton("Move usage", "frozenMoveUsage", CanSenseAct(source, epoch)); button.Tag = example.Id; button.HorizontalAlignment = HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetName(button, "Move usage: " + example.Text);
        button.Click += (_, _) =>
        {
            if (!CanSenseAct(source, epoch) || !_session.Entries.Any(e => e.Id == entry.Id && e.Senses.Any(s => s.Id == sense.Id && s.Examples.Any(x => x.Id == example.Id)))) return;
            var menu = NewSenseMenu(button); long menuEpoch = _actionMenuEpoch;
            void Add(string title, Guid? target)
            {
                var item = SenseMenuItem(title, target); item.Click += (_, _) =>
                {
                    if (menuEpoch != _actionMenuEpoch || !CanSenseAct(source, epoch)) return; menu.IsOpen = false;
                    LastOperation = Track(SenseActionAsync(source, epoch, async store => (await store.MoveExampleAsync(entry.Id, example.Id, sense.Id, target, CancellationToken.None)).SenseId, false));
                }; menu.Items.Add(item);
            }
            foreach (var target in entry.Senses.Where(s => s.Id != sense.Id)) Add(SenseTitle(target), target.Id);
            Add("New sense", null); menu.IsOpen = true;
        };
        return button;
    }
    private static Button SenseButton(string text, string name, bool enabled)
    {
        var button = new Button { Name = name, Content = text, IsEnabled = enabled, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8) };
        if (!enabled) button.ToolTip = "This action is unavailable while the notebook is busy or the store does not support it.";
        VocabularyNotebookTheme.Button(button); return button;
    }
    private static MenuItem SenseMenuItem(string title, Guid? target)
    {
        var item = new MenuItem { Header = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 }, Tag = target, Style = SenseMenuItemStyle };
        System.Windows.Automation.AutomationProperties.SetName(item, title);
        item.PreviewKeyDown += (_, args) => { if (args.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { args.Handled = true; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); } };
        return item;
    }
    private ContextMenu NewSenseMenu(Button owner)
    {
        CloseActionMenus(); var menu = new ContextMenu { PlacementTarget = owner, MinWidth = 160, MaxWidth = 340, MaxHeight = 360, Style = SenseMenuStyle }; owner.ContextMenu = menu; _senseMenus.Add(menu); return menu;
    }
    private void CloseActionMenus()
    {
        ++_actionMenuEpoch; foreach (var menu in _senseMenus) { menu.IsOpen = false; if (menu.PlacementTarget is FrameworkElement element && ReferenceEquals(element.ContextMenu, menu)) element.ContextMenu = null; menu.PlacementTarget = null; } _senseMenus.Clear();
    }
    private async Task SenseActionAsync(VocabularySelection source, long epoch, Func<ICanonicalVocabularySenseStore, Task<Guid?>> operation, bool selectDestination)
    {
        if (!CanSenseAct(source, epoch) || _store is not ICanonicalVocabularySenseStore store) return;
        Guid? node = _session.Destination?.NodeId; bool meaning = _session.MeaningMode;
        CloseActionMenus(); _senseMutating = true; var completion = _senseCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously); _actionError = null;
        try
        {
            _session.Refresh();
            Guid? result = await operation(store);
            if (_retiring) return;
            await _session.LoadAsync(); if (_retiring || epoch != _actionSelectionEpoch) return;
            if (selectDestination && result is { } id && _session.Entries.Any(e => e.Id == source.EntryId && e.Senses.Any(s => s.Id == id)))
            {
                _expanded.Add(source.EntryId); _session.Selection = new(source.EntryId, id);
                if (meaning && _session.MeaningMode) _session.Destination = new(VocabularyMeaningDestinationKind.Sense, node, id);
            }
        }
        catch (Exception error) { if (!_retiring && epoch == _actionSelectionEpoch) _actionError = error.Message; }
        finally { _senseMutating = false; _senseCompletion = null; try { if (!_retiring) _session.Refresh(); } finally { completion.TrySetResult(); } }
    }
    private async Task DeleteSenseAsync(VocabularySelection source, long epoch)
    {
        if (!CanSenseAct(source, epoch)) return; CloseActionMenus(); var done = _modalDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool confirmed = false;
        try { var window = new FrozenVocabularyDeleteSenseWindow { Owner = this }; _inputWindow = window; try { confirmed = window.ShowDialog() == true; } finally { _inputWindow = null; } }
        finally { _modalDone = null; done.TrySetResult(); }
        if (confirmed && CanSenseAct(source, epoch)) await SenseActionAsync(source, epoch, async store => { await store.RemoveSenseAsync(source.EntryId, source.SenseId, CancellationToken.None); return null; }, false);
        if (!_retiring && _pendingShow is { } pending) { _pendingShow = null; _expanded.Add(pending.EntryId); await _session.RevealSelectionAsync(pending); }
        if (_hideRequested && !_retiring) { _hideRequested = false; Hide(); }
        if (!_retiring) _session.Refresh();
    }
}

/// <summary>Explicit removal confirmation; the canonical Store owns the actual uncollection.</summary>
internal sealed class FrozenVocabularyDeleteSenseWindow : Window
{
    internal FrozenVocabularyDeleteSenseWindow()
    {
        Title = "Vocabulary"; Width = 410; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; VocabularyNotebookTheme.Follow(this);
        var root = new StackPanel { Margin = new Thickness(20) }; Content = root; root.Children.Add(new TextBlock { Text = "Remove from vocabulary?", FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(VocabularyInputControls.Caption("The saved sense will be removed from your notebook."));
        var cancel = new Button { Name = "frozenDeleteCancel", Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 85 }; var delete = new Button { Name = "frozenDeleteConfirm", Content = "Delete", MinWidth = 85 }; cancel.Click += (_, _) => DialogResult = false; delete.Click += (_, _) => DialogResult = true;
        root.Children.Add(VocabularyInputControls.Footer(cancel, delete));
    }
}
