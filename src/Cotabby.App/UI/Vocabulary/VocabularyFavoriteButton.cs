using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// WPF adapter of Mac's favorite fish control. It borrows one shared store and a detached capture;
/// only acknowledged state paints saved. A binding lease keeps late feedback/menu clicks attached
/// to their original card. The parent retires that lease on hide/navigation. No editor/model IO.
/// </summary>
public sealed class VocabularyFavoriteButton : Button
{
    private IWritingVocabularyStore? _store;
    private VocabularyCapture? _capture;
    private Func<bool>? _canAct;
    private Func<Task<bool>>? _preflight;
    private long? _preflightBinding;
    private long _binding;
    private string? _failure;
    private readonly ResourceDictionary _icons = new() { Source = new Uri("/Cotabby;component/UI/Vocabulary/VocabularyIcons.xaml", UriKind.Relative) };
    internal event Action<string?>? FailureChanged;
    internal Task LastOperation { get; private set; } = Task.CompletedTask;
    /// <summary>Expression candidate rows use Mac's direct Save/Remove toggle; ordinary writing
    /// cards retain their saved-item navigation menu.</summary>
    internal bool ToggleSavedOnClick { get; set; }
    public VocabularyFavoriteButton()
    {
        Width = Height = 28; Padding = new Thickness(4); Focusable = false; Background = Brushes.Transparent; BorderThickness = new Thickness(0);
        Visibility = Visibility.Collapsed; Click += OnClick;
        Unloaded += (_, _) => Clear();
        AutomationProperties.SetName(this, "Save to vocabulary");
    }
    internal void Bind(IWritingVocabularyStore store, VocabularyCapture capture, Func<bool> canAct, Func<Task<bool>>? preflight = null)
    {
        Dispatcher.VerifyAccess();
        if (!ReferenceEquals(store, _store) || capture != _capture)
        {
            Clear(); _store = store; _capture = capture; _store.Changed += Refresh;
        }
        _canAct = canAct; _preflight = preflight; Visibility = Visibility.Visible; Refresh();
        LastOperation = LoadAsync(store, _binding);
    }
    internal void Clear()
    {
        Dispatcher.VerifyAccess(); ++_binding;
        if (_store is not null) _store.Changed -= Refresh;
        if (ContextMenu is { } menu) menu.IsOpen = false;
        ContextMenu = null; _store = null; _capture = null; _canAct = null; _preflight = null; _preflightBinding = null; _failure = null;
        Content = null; ToolTip = null; Visibility = Visibility.Collapsed; IsEnabled = false; FailureChanged?.Invoke(null);
        AutomationProperties.SetHelpText(this, ""); AutomationProperties.SetItemStatus(this, "");
    }
    private async Task LoadAsync(IWritingVocabularyStore store, long binding)
    {
        try { await store.LoadAsync(); }
        catch (ObjectDisposedException) { }
        if (binding == _binding) Refresh();
    }
    private bool Current(long binding) => binding == _binding && _store is not null && _capture is not null && _canAct?.Invoke() == true;
    private void Refresh()
    {
        Dispatcher.VerifyAccess();
        if (_store is null || _capture is null) return;
        bool saved = _store.Contains(_capture), pending = _store.IsPending(_capture);
        IsEnabled = _store.IsLoaded && !pending && _preflightBinding != _binding && Current(_binding);
        Content = pending ? new ProgressBar { Width = 18, Height = 3, IsIndeterminate = true } : Icon(saved);
        ToolTip = _failure ?? (!_store.IsLoaded ? _store.ErrorMessage ?? "Loading vocabulary…" : saved ? ToggleSavedOnClick ? "Remove this saved usage" : "Saved · Show actions" : "Save to vocabulary");
        AutomationProperties.SetName(this, saved && ToggleSavedOnClick ? "Remove this saved usage" : "Save to vocabulary");
        AutomationProperties.SetItemStatus(this, !_store.IsLoaded ? (_store.ErrorMessage is null ? "Loading…" : "Unavailable") : pending ? "Saving…" : saved ? "Saved" : "Not saved");
        var detail = _failure ?? (!_store.IsLoaded ? _store.ErrorMessage : null);
        AutomationProperties.SetHelpText(this, detail ?? ""); FailureChanged?.Invoke(detail);
    }
    private FrameworkElement Icon(bool saved)
    {
        var path = new Path { Data = (Geometry)_icons[saved ? "FavoriteFishFilled" : "FavoriteFishOutline"], StrokeThickness = saved ? 0 : 24,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        path.SetResourceReference(saved ? Shape.FillProperty : Shape.StrokeProperty, saved ? "AccentBrush" : "TextSecondary");
        path.RenderTransform = new TranslateTransform(20, 20);
        var canvas = new Canvas { Width = 552, Height = 552 }; canvas.Children.Add(path);
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }
    private void OnClick(object sender, RoutedEventArgs args)
    {
        long binding = _binding;
        if (!Current(binding) || _preflightBinding == binding || !_store!.IsLoaded || _store.IsPending(_capture!)) return;
        if (!_store.Contains(_capture!)) { LastOperation = ChangeAfterPreflightAsync(binding, remove: false); return; }
        if (ToggleSavedOnClick) { LastOperation = ChangeAfterPreflightAsync(binding, remove: true); return; }
        var menu = new ContextMenu { PlacementTarget = this, Focusable = false };
        // Notebook navigation will register the source-equivalent route in its own UI slice.
        if (_store.OpenSelection is not null)
        {
            var show = new MenuItem { Header = "Show in vocabulary", Focusable = false };
            show.Click += (_, _) => { if (Current(binding)) _store!.Show(_capture!); }; menu.Items.Add(show);
        }
        var remove = new MenuItem { Header = "Remove this saved usage", Focusable = false };
        remove.Click += (_, _) => { if (Current(binding)) LastOperation = ChangeAfterPreflightAsync(binding, remove: true); };
        menu.Items.Add(remove); ContextMenu = menu; menu.IsOpen = true;
    }
    private async Task ChangeAfterPreflightAsync(long binding, bool remove)
    {
        var preflight = _preflight;
        if (preflight is not null)
        {
            if (_preflightBinding == binding) return;
            _preflightBinding = binding; Refresh();
            try { if (!await preflight()) return; }
            catch (Exception) { return; } // A failed receipt never authorizes storage.
            finally { if (_preflightBinding == binding) { _preflightBinding = null; Refresh(); } }
        }
        if (Current(binding)) await ChangeAsync(binding, remove);
    }
    private async Task ChangeAsync(long binding, bool remove)
    {
        var store = _store!; var capture = _capture!;
        try
        {
            var operation = remove ? store.RemoveAsync(capture) : store.SaveAsync(capture); Refresh();
            bool success = await operation;
            if (!Current(binding)) return;
            _failure = success ? null : store.ErrorMessage; FailureChanged?.Invoke(_failure); Refresh();
        }
        catch (ObjectDisposedException) { if (Current(binding)) { IsEnabled = false; } }
    }
}
