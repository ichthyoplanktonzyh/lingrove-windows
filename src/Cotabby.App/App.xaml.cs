using System.Windows;
using System.Windows.Threading;
using Cotabby.App.Emoji;
using Cotabby.App.Hosting;
using Cotabby.App.Overlay;
using Cotabby.App.Tray;
using Cotabby.App.UI;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.Suggestions;

namespace Cotabby.App;

/// <summary>
/// Application entry point. The app is tray-only, so there is no startup window.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName =
        "Local\\Cotabby.SingleInstance.{6f3b1d44-2c1b-4d0f-8c3d-2a1f1f7b1b2a}";

    private Mutex? _singleInstance;
    private AppHost? _host;
    private TrayController? _tray;
    private Task? _shutdown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            _singleInstance = null;
            MessageBox.Show("Cotabby is already running. Check the system tray.",
                "Cotabby", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Realizing the windows now makes the first suggestion and the first popup instant.
        var overlay = Realized(new GhostOverlayWindow());
        var macroPreview = Realized(new GhostOverlayWindow());
        var emojiPopup = Realized(new EmojiPopupWindow());

        _host = new AppHost(SynchronizationContext.Current!, overlay, macroPreview, emojiPopup);
        DispatcherUnhandledException += OnUiException;

        _tray = new TrayController(_host);
        _host.Coordinator.Start();
        _ = LoadModelAsync(_host, _tray);

        if (_host.Settings.ShowFirstRunWelcome)
        {
            Dispatcher.BeginInvoke(() => WelcomeWindow.ShowFor(_host), DispatcherPriority.Background);
        }
    }

    private static T Realized<T>(T window) where T : Window
    {
        window.Show();
        window.Hide();
        return window;
    }

    private static async Task LoadModelAsync(AppHost host, TrayController tray)
    {
        if (host.CompletionStatusEpoch is null) return;
        CompletionStatusGate.Token? status = null;
        try
        {
            host.ReconcileCompletionSelection();
            status = tray.BeginCompletionStatus("Loading model…");
            if (status is not { } current) return;
            bool loaded = await host.TryLoadCachedAsync();
            tray.PublishCompletionStatus(current, loaded ? null : "No model yet. Open Settings to download one.");
        }
        catch (Exception ex)
        {
            if (status is { } current) tray.PublishCompletionStatus(current, "Model load failed: " + ModelDisplayText.Safe(ex.Message));
        }
    }

    /// <summary>Normal Quit keeps the UI dispatcher alive while admitted work drains. WPF's
    /// OnExit is synchronous notification, so awaiting cleanup there cannot delay process exit.</summary>
    internal Task RequestShutdownAsync()
    {
        if (_shutdown is not null) return _shutdown;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _shutdown = completion.Task;
        _ = DrainAndShutdown();
        return completion.Task;
        async Task DrainAndShutdown()
        {
            try
            {
                // Closing first commits the last masked-key edit and detaches view observers.
                if (_host is not null) SettingsWindow.CloseFor(_host);
                _tray?.Dispose(); _tray = null;
                if (_host is not null) await _host.DisposeAsync();
            }
            catch (Exception error) { _host?.LogError(error, "Application shutdown failed."); }
            finally { completion.TrySetResult(true); Shutdown(); }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tray?.Dispose();
            // A forced OS exit cannot be delayed here. Normal tray Quit always uses the owned,
            // awaited path above; do not block this dispatcher on asynchronous UI continuations.
            if (_shutdown is null && _host is not null) _ = _host.DisposeAsync();
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        }
        finally { base.OnExit(e); }
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A failed settings click must not take down the keyboard hook with it.
        e.Handled = true;
        _host?.LogError(e.Exception, "Unhandled UI exception.");
    }
}
