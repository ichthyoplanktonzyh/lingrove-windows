using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using Cotabby.App.Localization;
using Cotabby.App.Settings;
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

        // The local CLI launches a tray-only background instance. It must not raise a modal
        // duplicate-instance or welcome window while an automation request is pending.
        bool cliStartup = e.Args.Length == 1 && string.Equals(e.Args[0], "-lingrove-cli", StringComparison.Ordinal);

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            if (!cliStartup)
            {
                InterfaceText.Initialize(null);
                MessageBox.Show(InterfaceText.Get("Lingrove is already running. Check the system tray."),
                    Branding.AppBrand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        // Freeze the app-only interface language before constructing any presentation. AppHost
        // borrows this exact store/snapshot; it must not load a second settings owner.
        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        InterfaceText.Initialize(settings.InterfaceLanguage);
        settings.InterfaceLocaleAtLaunch = InterfaceText.Locale;
        InterfaceText.RequestRestart = () => RequestShutdownAsync(restart: true);

        // Realizing the windows now makes the first suggestion and the first popup instant.
        var overlay = Realized(new GhostOverlayWindow());
        var macroPreview = Realized(new GhostOverlayWindow());
        var emojiPopup = Realized(new EmojiPopupWindow());

        _host = new AppHost(SynchronizationContext.Current!, overlay, macroPreview, emojiPopup, settingsStore, settings);
        DispatcherUnhandledException += OnUiException;

        _tray = new TrayController(_host);
        _host.Coordinator.Start();
        _ = LoadModelAsync(_host, _tray);

        if (!cliStartup && _host.Settings.ShowFirstRunWelcome)
        {
            Dispatcher.BeginInvoke(() => WelcomeWindow.ShowFor(_host, resumeFirstRun: true), DispatcherPriority.Background);
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
            status = tray.BeginCompletionStatus(InterfaceText.Get("Loading model…"));
            if (status is not { } current) return;
            bool loaded = await host.TryLoadCachedAsync();
            tray.PublishCompletionStatus(current, loaded ? null : InterfaceText.Get("No model yet. Open Settings to download one."));
        }
        catch (Exception ex)
        {
            if (status is { } current) tray.PublishCompletionStatus(current, InterfaceText.Format("Model load failed: {0}", ModelDisplayText.Safe(ex.Message)));
        }
    }

    /// <summary>Normal Quit keeps the UI dispatcher alive while admitted work drains. WPF's
    /// OnExit is synchronous notification, so awaiting cleanup there cannot delay process exit.</summary>
    internal Task RequestShutdownAsync() => RequestShutdownAsync(restart: false);

    private Task RequestShutdownAsync(bool restart)
    {
        Dispatcher.VerifyAccess();
        if (_shutdown is not null) return _shutdown;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _shutdown = completion.Task;
        _ = DrainAndShutdown();
        return completion.Task;
        async Task DrainAndShutdown()
        {
            bool drained = false;
            try
            {
                // Closing first commits the last masked-key edit and detaches view observers.
                try { if (_host is not null) SettingsWindow.CloseFor(_host); }
                finally
                {
                    try { _tray?.Dispose(); _tray = null; }
                    finally
                    {
                        if (_host is not null)
                        {
                            try { await WelcomeWindow.RetireForAsync(_host); }
                            finally { await _host.DisposeAsync(); }
                        }
                    }
                }
                drained = true;
            }
            catch (Exception error) { _host?.LogError(error, "Application shutdown failed."); }
            finally
            {
                InterfaceText.RequestRestart = null;
                // The mutex is owned by this UI thread. Release it only after admitted work
                // drains, and before launching the same executable with a fresh interface locale.
                ReleaseSingleInstance();
                if (restart && drained)
                {
                    try
                    {
                        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
                        using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
                        if (process is null) throw new InvalidOperationException("Application restart did not start.");
                    }
                    catch (Exception error)
                    {
                        _host?.LogError(error, "Application restart failed.");
                        MessageBox.Show(InterfaceText.Get("Could not restart Lingrove. Please open the application again.", "Localizable"),
                            InterfaceText.Get("Lingrove", "Localizable"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (restart)
                    MessageBox.Show(InterfaceText.Get("Lingrove could not finish restarting. Please open the application again.", "Localizable"),
                        InterfaceText.Get("Lingrove", "Localizable"), MessageBoxButton.OK, MessageBoxImage.Error);
                completion.TrySetResult(true);
                Shutdown();
            }
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
            InterfaceText.RequestRestart = null;
            ReleaseSingleInstance();
        }
        finally { base.OnExit(e); }
    }

    private void ReleaseSingleInstance()
    {
        if (_singleInstance is not { } mutex) return;
        _singleInstance = null;
        try { mutex.ReleaseMutex(); }
        finally { mutex.Dispose(); }
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A failed settings click must not take down the keyboard hook with it.
        e.Handled = true;
        _host?.LogError(e.Exception, "Unhandled UI exception.");
    }
}
