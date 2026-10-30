using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Cotabby.App.Hosting;
using Cotabby.App.Settings;
using Cotabby.App.UI;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.Models;
using Cotabby.Core.Suggestions;
using Cotabby.Inference;
using H.NotifyIcon;
using Cotabby.App.Branding;
using Cotabby.App.Localization;

namespace Cotabby.App.Tray;

/// <summary>Owns the system tray icon and its menu.</summary>
public sealed class TrayController : IDisposable
{
    private readonly AppHost _host;
    private readonly TaskbarIcon _tray;
    private readonly MenuItem _statusItem = new() { IsEnabled = false };
    private readonly MenuItem _wordCountItem = new() { IsEnabled = false };
    private readonly MenuItem _enabledItem = new() { Header = InterfaceText.Get("Enabled"), IsCheckable = true };
    private readonly MenuItem _pauseMenu = new() { Header = InterfaceText.Get("Pause") };
    private readonly MenuItem _resumeItem = new() { Header = InterfaceText.Get("Resume") };
    private readonly MenuItem _modelMenu = new() { Header = InterfaceText.Get("Model") };
    private readonly MenuItem _lengthMenu = new() { Header = InterfaceText.Get("Completion length") };
    private string? _statusOverride;
    private readonly CompletionStatusGate _statusGate = new();
    private CompletionStatusGate.Token? _statusToken;
    private bool _disposed;

    public TrayController(AppHost host)
    {
        _host = host;
        _tray = new TaskbarIcon { ToolTipText = AppBrand.Name };
        using (var icon = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Lingrove.ico"))!.Stream)
        {
            _tray.Icon = new Icon(icon);
        }
        // A tray-only app never joins a visual tree, so the icon has to be created explicitly.
        _tray.ForceCreate();

        _enabledItem.Click += (_, _) => Update(s => s.Enabled = _enabledItem.IsChecked);
        _resumeItem.Click += (_, _) => Update(s => s.Pause = null);
        foreach (var duration in Enum.GetValues<PauseDuration>())
        {
            var item = new MenuItem { Header = InterfaceText.Get(PauseState.MenuLabel(duration)) };
            item.Click += (_, _) => Update(s => s.Pause = PauseState.For(duration, DateTimeOffset.Now));
            _pauseMenu.Items.Add(item);
        }
        foreach (var preset in WordCountPreset.All)
        {
            var item = new MenuItem { Header = InterfaceText.Get(preset.Label), Tag = preset.Id };
            item.Click += (_, _) => Update(s =>
            {
                s.CompletionLengthPreset = preset.Id;
                s.UseCustomWordRange = false;
            });
            _lengthMenu.Items.Add(item);
        }

        var settingsItem = new MenuItem { Header = InterfaceText.Get("Settings…") };
        settingsItem.Click += (_, _) => SettingsWindow.ShowOrFocus(host);
        var translateItem = new MenuItem { Header = InterfaceText.Get("Translate Current Sentence…") };
        translateItem.Click += (_, _) => host.LearningPreview.RequestFromCurrentField();
        var vocabularyItem = new MenuItem { Header = InterfaceText.Get("Vocabulary…") };
        vocabularyItem.Click += (_, _) => host.VocabularyWindow.Show();
        var welcomeItem = new MenuItem { Header = InterfaceText.Get("Run welcome again…") };
        welcomeItem.Click += (_, _) => WelcomeWindow.ShowFor(host);
        var quitItem = new MenuItem { Header = InterfaceText.Get("Quit Lingrove", "Learning") };
        quitItem.Click += async (_, _) => await ((App)Application.Current).RequestShutdownAsync();

        var menu = new ContextMenu();
        foreach (var item in new Control[]
                 {
                     _statusItem, _wordCountItem, new Separator(),
                     _enabledItem, _pauseMenu, _resumeItem, _modelMenu, _lengthMenu, new Separator(),
                     translateItem, vocabularyItem, settingsItem, welcomeItem, new Separator(), quitItem,
                 })
        {
            menu.Items.Add(item);
        }
        // Refreshing on open keeps a pause that expired in the background from showing as active.
        menu.Opened += (_, _) => Refresh();
        _tray.ContextMenu = menu;
        _tray.TrayLeftMouseUp += (_, _) => SettingsWindow.ShowOrFocus(host);

        host.SettingsApplied += Refresh;
        Refresh();
    }

    /// <summary>Shows a transient status such as a load failure. Null returns to the normal status.</summary>
    public CompletionStatusGate.Token? BeginCompletionStatus(string text)
    {
        if (_disposed || _host.CompletionStatusEpoch is not { } epoch) return null;
        var token = _statusGate.Begin(epoch); _statusToken = token; _statusOverride = text; Refresh(); return token;
    }

    public void PublishCompletionStatus(CompletionStatusGate.Token token, string? text) => Application.Current.Dispatcher.Invoke(() =>
    {
        if (_disposed || !_statusGate.IsCurrent(token, _host.CompletionStatusEpoch)) return;
        _statusToken = text is null ? null : token; _statusOverride = text; Refresh();
    });

    private void Update(Action<AppSettings> change)
    {
        change(_host.Settings);
        _host.PersistSettings();
    }

    private void Refresh()
    {
        var settings = _host.Settings;
        if (_statusToken is { } token && !_statusGate.IsCurrent(token, _host.CompletionStatusEpoch))
        { _statusToken = null; _statusOverride = null; }
        var now = DateTimeOffset.Now;
        bool paused = settings.Pause?.IsActive(now) == true;

        _statusItem.Header = _statusOverride
            ?? (!settings.Enabled ? InterfaceText.Get("Lingrove is off")
                : paused ? PausePresentation.Status(settings.Pause, now)
                : settings.Engine == EngineKind.Endpoint ? InterfaceText.Format("Endpoint · {0}", settings.EndpointModel)
                : _host.AdmittedLocalModel is { } active ? InterfaceText.Format("Ready · {0}", ModelDisplayText.Name(active.FileName))
                : InterfaceText.Get("No model loaded"));
        _tray.ToolTipText = ModelDisplayText.Safe($"{AppBrand.Name} · {_statusItem.Header}", maximumUtf16: 120);

        long words = _host.Coordinator.Metrics.WordsAccepted;
        _wordCountItem.Header = InterfaceText.Format("{0:N0} words completed", words);
        _wordCountItem.Visibility = settings.ShowTrayWordCount && words > 0 ? Visibility.Visible : Visibility.Collapsed;

        _enabledItem.IsChecked = settings.Enabled;
        _pauseMenu.Visibility = settings.Enabled && !paused ? Visibility.Visible : Visibility.Collapsed;
        _resumeItem.Visibility = settings.Enabled && paused ? Visibility.Visible : Visibility.Collapsed;
        // Rebuilt each time so a model dropped into the folder shows up without a restart.
        _modelMenu.Items.Clear();
        foreach (var model in ModelCatalog.Available(settings.ModelAssignmentSnapshot()))
        {
            var item = new MenuItem { Header = ModelDisplayText.Name(model.FileName), ToolTip = model.Subtitle, Tag = model.Id };
            item.Click += (_, _) => _ = SwitchModelAsync(model.Id);
            _modelMenu.Items.Add(item);
        }
        Check(_modelMenu, settings.ActiveModelId);
        Check(_lengthMenu, settings.UseCustomWordRange ? null : settings.CompletionLengthPreset);
    }

    private static void Check(MenuItem menu, string? selectedTag)
    {
        foreach (MenuItem item in menu.Items) item.IsChecked = (string)item.Tag == selectedTag;
    }

    private async Task SwitchModelAsync(string id)
    {
        if (ModelCatalog.FindById(id, _host.Settings.ModelAssignmentSnapshot()) is not { } selected) return;
        Update(s => s.ActiveModelId = id);
        if (!ModelDownloader.IsCached(selected))
        {
            // Downloads are several GB, so they start from Settings where progress is visible.
            SettingsWindow.ShowOrFocus(_host);
            return;
        }
        if (BeginCompletionStatus(InterfaceText.Get("Loading model…")) is not { } status) return;
        try
        {
            bool ready = await _host.EnsureModelReadyAsync(null, CancellationToken.None);
            PublishCompletionStatus(status, ready ? null : InterfaceText.Get("The selected model is unavailable."));
        }
        catch (Exception ex)
        {
            PublishCompletionStatus(status, InterfaceText.Format("Model load failed: {0}", ModelDisplayText.Safe(ex.Message)));
        }
    }

    public void Dispose() { _disposed = true; _statusGate.Invalidate(); _host.SettingsApplied -= Refresh; _tray.Dispose(); }
}
