using System.Windows;
using System.Windows.Threading;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>
/// AppHost owns one lazy activating library window, matching Mac VocabularyWindowController.
/// Cards stay nonactivating; only an explicit library/Show command activates this window. Ordinary
/// close hides it and preserves browsing. Shutdown retires its worker/subscriptions before Store.
/// </summary>
internal sealed class VocabularyWindowController : IAsyncDisposable
{
    private readonly VocabularyStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly Action<VocabularySelection?> _show;
    private VocabularyNotebookWindow? _window;
    private int _disposed;
    internal VocabularyWindowController(VocabularyStore store, Dispatcher dispatcher)
    { _store = store; _dispatcher = dispatcher; _show = Show; store.OpenSelection = _show; }
    internal void Show(VocabularySelection? selection = null)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => Show(selection)); return; }
        _window ??= new VocabularyNotebookWindow(_store, new WindowsVocabularyTextSearch());
        _window.Reveal(selection); _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate(); _ = LoadAsync();
    }
    private async Task LoadAsync() { try { await _store.LoadAsync(); } catch (ObjectDisposedException) { } }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (ReferenceEquals(_store.OpenSelection, _show)) _store.OpenSelection = null;
        if (_dispatcher.HasShutdownStarted) { if (_window is not null) await _window.DrainAsync().ConfigureAwait(false); }
        else if (_dispatcher.CheckAccess()) { if (_window is not null) await _window.RetireAsync(); }
        else await (await _dispatcher.InvokeAsync(() => _window?.RetireAsync() ?? Task.CompletedTask)).ConfigureAwait(false);
        _window = null;
    }
}
