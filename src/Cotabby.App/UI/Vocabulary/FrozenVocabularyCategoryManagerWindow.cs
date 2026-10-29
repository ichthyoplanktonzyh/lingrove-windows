using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Directory manager borrows the canonical owner, coalesces revision notifications and
/// joins child dialogs and accepted writes before its subscription is retired.</summary>
internal sealed class FrozenVocabularyCategoryManagerWindow : Window
{
    private static readonly Style TitleStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Button}">
          <Setter Property="Foreground" Value="{DynamicResource TextPrimary}" /><Setter Property="Padding" Value="8,5" />
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type Button}">
            <Border x:Name="row" Background="Transparent" BorderThickness="1" BorderBrush="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}"><TextBlock Text="{TemplateBinding Content}" TextTrimming="CharacterEllipsis" /></Border>
            <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="row" Property="Background" Value="{DynamicResource HoverBg}" /></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="row" Property="BorderBrush" Value="{DynamicResource AccentBrush}" /></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    private sealed record Row(R.VocabularyCategory Category, string Title, bool Visible)
    { public Visibility CanDelete => Category.FrameworkId is null ? Visibility.Visible : Visibility.Collapsed; }
    private readonly ICanonicalVocabularySemanticStore _store;
    private readonly IReadOnlyList<LearningLanguage> _languages;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private FrozenVocabularyImportSnapshot? _pending;
    private bool _queued, _retiring, _allowClose, _busy;
    private FrozenVocabularyImportSnapshot _snapshot;
    private readonly TextBox _search = new() { Name = "categoryManagerSearch", Tag = "Search categories" };
    private readonly ListBox _list = new() { Name = "categoryManagerList" };
    private readonly TextBlock _error = VocabularyNotebookTheme.Brush(new TextBlock { Name = "categoryManagerError", TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "OrangeBrush");
    private readonly Button _done = new() { Name = "categoryManagerDone", Content = "Done", MinWidth = 90 };
    private Window? _child;
    private TaskCompletionSource? _childDone, _retirement;
    private Task? _mutation;
    internal Task? LastOperation { get; private set; }
    internal FrozenVocabularyCategoryManagerWindow(ICanonicalVocabularySemanticStore store, IVocabularyEntryImportStore reader, FrozenVocabularyImportSnapshot snapshot, IReadOnlyList<LearningLanguage> languages)
    {
        _store = store; _snapshot = snapshot; _languages = Array.AsReadOnly(languages.ToArray()); Title = "Manage categories"; Width = 620; Height = 540; MinWidth = 500; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; VocabularyNotebookTheme.Follow(this);
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root; var header = VocabularyInputControls.Heading("Manage categories"); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = VocabularyInputControls.Footer(_done); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        VocabularyNotebookTheme.Input(_search); System.Windows.Automation.AutomationProperties.SetName(_search, "Search categories"); _search.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(_search, Dock.Top); root.Children.Add(_search);
        var rowStyle = new Style(typeof(ListBoxItem)); rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); _list.ItemContainerStyle = rowStyle;
        _list.ItemTemplate = RowTemplate(); VirtualizingPanel.SetIsVirtualizing(_list, true); VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling); VocabularyNotebookTheme.Brush(_list, Control.BackgroundProperty, "CardBg"); VocabularyNotebookTheme.Brush(_list, Control.ForegroundProperty, "TextPrimary"); VocabularyNotebookTheme.Brush(_list, Control.BorderBrushProperty, "CardBorder"); System.Windows.Automation.AutomationProperties.SetName(_list, "Categories"); root.Children.Add(_list);
        _search.TextChanged += (_, _) => Render(); _done.Click += (_, _) => Close(); Closing += OnClosing; Closed += (_, _) => BeginRetire();
        _subscription = reader.Subscribe(QueueSnapshot); Render();
    }
    private DataTemplate RowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel)); panel.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 4, 5, 4));
        var delete = Button("Delete", DeleteClicked); delete.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(Row.CanDelete))); delete.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(Row.Title)) { StringFormat = "Delete {0}" }); delete.SetValue(DockPanel.DockProperty, Dock.Right); panel.AppendChild(delete);
        var edit = Button("Edit", EditClicked); edit.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(Row.Title)) { StringFormat = "Edit {0}" }); edit.SetValue(DockPanel.DockProperty, Dock.Right); panel.AppendChild(edit);
        var visible = new FrameworkElementFactory(typeof(CheckBox)); visible.SetValue(ContentControl.ContentProperty, "Visible"); visible.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(Row.Title)) { StringFormat = "Visible: {0}" }); visible.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 4, 10, 4)); visible.SetValue(DockPanel.DockProperty, Dock.Right); visible.SetValue(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")); visible.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(Row.Visible)) { Mode = BindingMode.OneWay }); visible.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(VisibilityClicked)); panel.AppendChild(visible);
        var title = Button(null, EditClicked); title.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Row.Title))); title.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(Row.Title))); title.SetValue(FrameworkElement.StyleProperty, TitleStyle); panel.AppendChild(title);
        return new DataTemplate { VisualTree = panel };
    }
    private static FrameworkElementFactory Button(string? text, RoutedEventHandler handler)
    {
        var button = new FrameworkElementFactory(typeof(Button)); if (text is not null) button.SetValue(ContentControl.ContentProperty, text); button.SetValue(FrameworkElement.MarginProperty, new Thickness(3)); button.SetValue(Control.PaddingProperty, new Thickness(8, 4, 8, 4)); button.SetValue(FrameworkElement.StyleProperty, new DynamicResourceExtension("Btn")); button.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, handler); return button;
    }
    private void Render()
    {
        if (_retiring) return; var index = _snapshot.Document.Classification;
        _list.ItemsSource = index.Categories.Where(c => !c.IsRetired).Select(c => new Row(c, R.VocabularyClassificationPolicy.Title(c, index, _languages), !index.Overrides.Any(o => o.CategoryId == c.Id && o.IsHidden)))
            .Where(row => CultureInfo.CurrentCulture.CompareInfo.IndexOf(row.Title, _search.Text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToArray();
        _list.IsEnabled = _search.IsEnabled = _done.IsEnabled = !_busy && _childDone is null;
    }
    private void QueueSnapshot(FrozenVocabularyImportSnapshot value)
    {
        lock (_gate)
        {
            if (_retiring || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            if (_pending is null || value.Revision > _pending.Revision) _pending = value;
            if (_queued) return; _queued = true;
            try { Dispatcher.BeginInvoke(DispatcherPriority.DataBind, DrainSnapshot); }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { _queued = false; _pending = null; }
        }
    }
    private void DrainSnapshot() { FrozenVocabularyImportSnapshot? value; lock (_gate) { _queued = false; value = _retiring ? null : _pending; _pending = null; } if (value is not null) Accept(value); }
    private void Accept(FrozenVocabularyImportSnapshot value) { if (_retiring || value.Revision < _snapshot.Revision) return; _snapshot = value; Render(); }
    private void EditClicked(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: Row row }) LastOperation = EditAsync(row.Category.Id); }
    private void VisibilityClicked(object sender, RoutedEventArgs e) { if (sender is CheckBox { DataContext: Row row } check) LastOperation = SetVisibleAsync(row.Category.Id, check.IsChecked == true); }
    private void DeleteClicked(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: Row row }) LastOperation = DeleteAsync(row.Category.Id); }
    private async Task EditAsync(Guid id)
    {
        if (_retiring || _busy || _childDone is not null || _snapshot.Document.Classification.Categories.FirstOrDefault(c => c.Id == id && !c.IsRetired) is not { } category) return;
        var done = _childDone = new(TaskCreationOptions.RunContinuationsAsynchronously); Render();
        try { var editor = new FrozenVocabularyCategoryEditorWindow(_store, _snapshot.Document.Classification, _languages, category) { Owner = this }; _child = editor; try { editor.ShowDialog(); } finally { await editor.RetireAsync(); _child = null; } if (!_retiring && editor.Receipt is { } receipt) Accept(receipt); }
        finally { _child = null; _childDone = null; try { Render(); } finally { done.TrySetResult(); } }
    }
    private async Task DeleteAsync(Guid id)
    {
        if (_retiring || _busy || _childDone is not null || !_snapshot.Document.Classification.Categories.Any(c => c.Id == id && c.FrameworkId is null)) return;
        var done = _childDone = new(TaskCreationOptions.RunContinuationsAsynchronously); bool confirmed = false; Render();
        try { var dialog = new FrozenVocabularyCategoryDeleteWindow { Owner = this }; _child = dialog; try { confirmed = dialog.ShowDialog() == true; } finally { _child = null; } }
        finally { _childDone = null; done.TrySetResult(); }
        if (confirmed && !_retiring && !_busy && _snapshot.Document.Classification.Categories.Any(c => c.Id == id && c.FrameworkId is null))
        { var baseline = _snapshot.Document.Classification; await MutateAsync(R.VocabularyClassificationPolicy.RemoveCategory(id, baseline), baseline); }
        if (!_retiring) Render();
    }
    private Task SetVisibleAsync(Guid id, bool visible)
    {
        if (_retiring || _busy || _childDone is not null) { Render(); return Task.CompletedTask; }
        var baseline = _snapshot.Document.Classification; if (!baseline.Categories.Any(c => c.Id == id && !c.IsRetired)) { Render(); return Task.CompletedTask; }
        var overlays = baseline.Overrides.ToList(); int index = overlays.FindIndex(o => o.CategoryId == id);
        if (index < 0) overlays.Add(new(id) { IsHidden = !visible }); else overlays[index] = overlays[index] with { IsHidden = !visible };
        return MutateAsync(baseline with { Overrides = overlays }, baseline);
    }
    private async Task MutateAsync(R.VocabularyClassificationIndex edited, R.VocabularyClassificationIndex baseline)
    {
        _busy = true; _error.Text = ""; Render(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _mutation = completion.Task;
        try { var snapshot = await _store.UpdateClassificationAsync(edited, baseline, CancellationToken.None); if (!_retiring) Accept(snapshot); }
        catch (Exception error) { if (!_retiring) _error.Text = error.Message; }
        finally { _busy = false; _mutation = null; try { Render(); } finally { completion.TrySetResult(); } }
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!_allowClose && (_busy || _childDone is not null || _retiring)) e.Cancel = true; }
    private void BeginRetire() { lock (_gate) { _retiring = true; _pending = null; } }
    internal Task RetireAsync()
    {
        Dispatcher.VerifyAccess(); if (_retirement is not null) return _retirement.Task; var done = _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously); _ = Finish(done); return done.Task;
    }
    private async Task Finish(TaskCompletionSource done)
    {
        BeginRetire();
        try { if (_child is FrozenVocabularyCategoryEditorWindow editor) await editor.RetireAsync(); else _child?.Close(); if (_childDone is { } child) await child.Task; if (_mutation is { } mutation) await mutation; _subscription.Dispose(); _allowClose = true; if (IsVisible) Close(); done.TrySetResult(); }
        catch (Exception e) { done.TrySetException(e); }
    }
}

internal sealed class FrozenVocabularyCategoryDeleteWindow : Window
{
    internal FrozenVocabularyCategoryDeleteWindow()
    {
        Title = "Vocabulary"; Width = 410; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner; VocabularyNotebookTheme.Follow(this);
        var root = new StackPanel { Margin = new Thickness(20) }; Content = root; root.Children.Add(new TextBlock { Text = "Delete this category?", FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); root.Children.Add(VocabularyInputControls.Caption("Meanings and expressions will be kept."));
        var cancel = new Button { Name = "categoryDeleteCancel", Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 90 }; var delete = new Button { Name = "categoryDeleteConfirm", Content = "Delete category", MinWidth = 90 }; cancel.Click += (_, _) => DialogResult = false; delete.Click += (_, _) => DialogResult = true; root.Children.Add(VocabularyInputControls.Footer(cancel, delete));
    }
}
