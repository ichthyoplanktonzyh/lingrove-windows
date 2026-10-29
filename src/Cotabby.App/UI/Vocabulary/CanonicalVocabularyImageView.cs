using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cotabby.Core.Vocabulary.Frozen;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// A visible editor/reading row owns one decoded bitmap and its optional enlargement window.
/// Draft bytes are borrowed from the editor; persisted bytes come through the shared Store. Each
/// visibility/target lifetime has its own cancellation and identity guard. Cancelling the wait
/// retires presentation promptly; Store still owns/drains any already admitted bounded file IO.
/// </summary>
internal sealed class CanonicalVocabularyImageView : Border, IDisposable
{
    private readonly ICanonicalVocabularyMediaStore _store;
    private readonly Button _open;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, MaxHeight = 280 };
    private readonly TextBlock _status = new() { Text = "Loading image…", Margin = new Thickness(0, 8, 0, 8) };
    private readonly VocabularyImageReference _reference;
    private VocabularyPreparedImage? _draft;
    private CancellationTokenSource? _work;
    private long _version;

    private bool _loaded, _disposed;
    private Window? _preview;
    private readonly List<Task> _loads = [];
    internal Task LastLoad { get; private set; } = Task.CompletedTask;
    internal BitmapSource? Bitmap => _image.Source as BitmapSource;

    internal CanonicalVocabularyImageView(ICanonicalVocabularyMediaStore store, VocabularyImageReference reference, VocabularyPreparedImage? draft = null, string caption = "")
    {
        _store = store; _reference = reference; _draft = draft;
        _open = new Button { Content = _image, ToolTip = "Enlarge image", Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(_open, "Enlarge image"); _open.Click += (_, _) => Enlarge();
        var content = new StackPanel(); content.Children.Add(_status); content.Children.Add(_open); Child = content; SetCaption(caption);
        Loaded += (_, _) => Resume(); Unloaded += (_, _) => Suspend();
        IsVisibleChanged += (_, _) => { if (IsVisible) Resume(); else Suspend(); };
    }
    internal void SetCaption(string caption)
    { Dispatcher.VerifyAccess(); AutomationProperties.SetName(this, caption.Length == 0 ? "Image" : caption); }
    private void Resume()
    {
        Dispatcher.VerifyAccess();
        if (_disposed || !IsVisible || _work is not null || _loaded) return;
        var work = new CancellationTokenSource(); _work = work; long version = ++_version;
        _loaded = false; _image.Source = null;
        _status.Text = "Loading image…"; _status.Visibility = Visibility.Visible; _open.Visibility = Visibility.Collapsed;
        _loads.RemoveAll(task => task.IsCompleted); LastLoad = LoadAsync(_reference, _draft, version, work); _loads.Add(LastLoad);
    }
    private async Task LoadAsync(VocabularyImageReference reference, VocabularyPreparedImage? draft, long version, CancellationTokenSource work)
    {
        var token = work.Token;
        try
        {
            BitmapSource? bitmap = null;
            try
            {
                if (draft is not null)
                    bitmap = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(draft.CopyBytes(), reference, token), token).ConfigureAwait(false);
                else
                {
                    byte[] bytes = await _store.ReadImageAsync(reference, token).ConfigureAwait(false);
                    bitmap = await Task.Run(() => WindowsVocabularyImageDecoder.Decode(bytes, reference, token), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* Mac shows unavailable for a missing/corrupt/undecodable image. */ }
            token.ThrowIfCancellationRequested(); if (Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (_disposed || version != _version || !IsVisible) return;
                _loaded = true; _image.Source = bitmap;
                _open.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
                _status.Text = "Image unavailable"; _status.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
                _work = null;
            }, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
        finally { work.Dispose(); }
    }
    private void Suspend()
    {
        Dispatcher.VerifyAccess(); ++_version;
        var work = _work; _work = null;
        try { work?.Cancel(); } catch (ObjectDisposedException) { }
        _preview?.Close(); _preview = null; _image.Source = null; _loaded = false;
    }
    private void Enlarge()
    {
        if (_disposed || !IsVisible || Bitmap is not { } bitmap || _preview is not null) return;
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(14, 6, 14, 6) };
        var panel = new DockPanel { Margin = new Thickness(16) }; DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 12) });
        var preview = new Window { Title = "Image", Content = panel, Width = 760, Height = 580, MinWidth = 300, MinHeight = 240,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        preview.SetResourceReference(Window.BackgroundProperty, "WindowBg"); preview.SetResourceReference(Window.ForegroundProperty, "TextPrimary"); WindowTheme.Follow(preview);
        close.Click += (_, _) => preview.Close(); _preview = preview;
        try { preview.ShowDialog(); } finally { _preview = null; }
    }
    internal async Task RetireAsync() { Dispose(); await Task.WhenAll(_loads); }
    public void Dispose()
    {
        Dispatcher.VerifyAccess(); if (_disposed) return; _disposed = true; Suspend(); _draft = null;
    }
}
