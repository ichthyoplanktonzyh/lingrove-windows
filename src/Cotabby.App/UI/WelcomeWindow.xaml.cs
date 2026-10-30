using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cotabby.App.Hosting;
using Cotabby.App.Localization;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.Models;
using Cotabby.Core.Suggestions;
using Cotabby.Inference;

namespace Cotabby.App.UI;

/// <summary>
/// First-run wizard with the steps welcome, starting point, personalize, keybinds and done.
/// The model download starts in the background once a starting point is confirmed, so the
/// later steps never wait on the network. The done step shows its progress.
/// </summary>
public partial class WelcomeWindow : Window
{
    private static readonly HashSet<WelcomeWindow> OpenWindows = [];
    private readonly AppHost _host;
    private readonly FrameworkElement[] _steps;
    private readonly (Border Card, Border Badge)[] _templates;
    private readonly List<ShortcutEditor> _shortcuts = [];
    private readonly List<Border> _capsules = [];
    private int _step;
    private string _templateModelId;
    private bool _templateChosen, _recommendedRoute = true;
    private readonly bool _resumeFirstRun;
    private readonly WelcomeReadinessView _readiness;
    private readonly WelcomeDonePage StepDone;
    private bool _retired, _retiring;

    // Background model preparation. The sequence number lets a run that was superseded,
    // because the user went back and picked another starting point, drop its late results.
    private CancellationTokenSource? _modelCts;
    private int _ensureSeq;
    private bool _ensureBusy;
    private string? _lastEnsureFor;
    private string? _modelReadyFor;
    private string? _modelError;
    private int? _modelPercent;
    private readonly CompletionStatusGate _statusGate = new();
    private CompletionStatusGate.Token? _modelOperation;

    public WelcomeWindow(AppHost host, bool resumeFirstRun = false)
    {
        _host = host; _resumeFirstRun = resumeFirstRun;
        InitializeComponent();
        OpenWindows.Add(this);
        StepDone = new WelcomeDonePage { Visibility = Visibility.Collapsed };
        Grid.SetRowSpan(StepDone, 3); ((Panel)NormalFooter.Parent).Children.Add(StepDone);
        StepDone.FinishRequested += Finish;
        StepDone.BackRequested += () => ShowStep(_step - 1);
        _readiness = new WelcomeReadinessView(host);
        _readiness.CompletionFailed = id => _modelError is not null && _lastEnsureFor == id;
        StepDone.ReadinessHost.Content = _readiness;
        _readiness.IsVisibleChanged += (_, _) => _readiness.VisibilityChanged();
        Activated += (_, _) => _readiness.Refresh();
        Closing += OnRetiring;
        InterfaceText.ApplyDirection(this);
        Title = InterfaceText.Get("Welcome to Lingrove", "Learning");
        InterfaceText.TranslateInitialTree(this);
        StepWelcome.Children.Add(ProductFeatureExamples.WelcomeChoices(() => _host.Settings.ExpressionAssistTrigger, current: () => _host.Settings));
        _host.SettingsApplied += HostSettingsApplied;
        Closed += (_, _) => { OpenWindows.Remove(this); _host.SettingsApplied -= HostSettingsApplied; _statusGate.Invalidate(); _modelCts?.Cancel(); };
        WindowTheme.Follow(this);

        var permissions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        permissions.Children.Add(new TextBlock { Text = InterfaceText.Get("Permissions"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        permissions.Children.Add(new TextBlock { Text = InterfaceText.Get("Use Lingrove in a compatible editor. If that editor runs as an administrator, Lingrove may need the same privilege level.", "Localizable"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        permissions.Children.Add(new TextBlock { Text = InterfaceText.Get("Text stays local unless you explicitly configure and use an endpoint.", "Localizable"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        ((Panel)StepWelcome.Parent).Children.Add(permissions);
        _steps = [StepWelcome, permissions, StepRecommended, StepTemplates, StepProfile, StepKeys, StepDone];
        var recommendation = new RecommendedSetupView(host);
        recommendation.ManualRequested += () => { _recommendedRoute = false; ShowStep(3); };
        recommendation.Applied += () => _recommendedRoute = true;
        StepRecommended.Content = recommendation;
        _templates = [(QuickCard, QuickBadge), (EverydayCard, EverydayBadge), (PowerCard, PowerBadge)];
        BuildCapsules();
        BuildShortcutRows();

        // Seeding from the current settings makes a second run from the tray non-destructive.
        var settings = _host.Settings;
        NameBox.Text = settings.UserName;
        NameBox.LostKeyboardFocus += (_, _) =>
        {
            if (!_resumeFirstRun || _host.Settings.UserName == NameBox.Text.Trim()) return;
            _host.Settings.UserName = NameBox.Text.Trim();
            _host.SettingsStore.Save(_host.Settings);
        };
        LanguagesHost.Content = new LanguageEditor(host);
        ModeWord.IsChecked = settings.AcceptanceMode == AcceptanceGranularity.Word;
        ModePhrase.IsChecked = settings.AcceptanceMode == AcceptanceGranularity.Phrase;
        _templateModelId = _templates.Any(template => TemplateId(template.Card) == settings.ActiveModelId)
            ? settings.ActiveModelId!
            : TemplateId(EverydayCard);
        UpdateTemplateVisuals();

        var progress = settings.OnboardingProgress;
        if (_resumeFirstRun) _recommendedRoute = !progress.Manual;
        ShowStep(_resumeFirstRun ? progress.Step : 0);
    }

    public static void ShowFor(AppHost host, bool resumeFirstRun = false) => new WelcomeWindow(host, resumeFirstRun).Show();

    internal static async Task RetireForAsync(AppHost host)
    {
        var windows = OpenWindows.Where(window => ReferenceEquals(window._host, host)).ToArray();
        foreach (var window in windows) { window.IsEnabled = false; window._retiring = true; }
        await Task.WhenAll(windows.Select(window => window._readiness.RetireAsync()));
        foreach (var window in windows) if (!window._retired) { window._retired = true; window.Close(); }
    }

    private async void OnRetiring(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_retired) return;
        e.Cancel = true; IsEnabled = false;
        if (_retiring) return;
        _retiring = true;
        await _readiness.RetireAsync();
        if (_retired) return;
        _retired = true; Close();
    }

    // ---------- Step navigation ----------

    private void BuildCapsules()
    {
        for (int i = 0; i < 4; i++)
        {
            var capsule = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0) };
            _capsules.Add(capsule);
            CapsulePanel.Children.Add(capsule);
        }
    }

    private void ShowStep(int index)
    {
        if (_resumeFirstRun && _steps[_step] == StepProfile && index != _step)
        {
            _host.Settings.UserName = NameBox.Text.Trim();
            _host.SettingsStore.Save(_host.Settings);
        }
        _step = Math.Clamp(index, 0, _steps.Length - 1);
        if (_resumeFirstRun)
        {
            if (_host.Settings.RecordOnboardingStep(_step, !_recommendedRoute))
            {
                _host.SettingsStore.Save(_host.Settings);
            }
        }
        for (int i = 0; i < _steps.Length; i++) _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
        int progressIndex = _step switch { 1 => 1, 2 or 3 => 2, 4 => 3, 5 => 4, _ => 0 };
        CapsulePanel.Visibility = StepCaption.Visibility = progressIndex == 0 ? Visibility.Hidden : Visibility.Visible;
        for (int i = 0; i < _capsules.Count; i++)
        {
            _capsules[i].Width = i + 1 == progressIndex ? 28 : 18;
            _capsules[i].SetResourceReference(Border.BackgroundProperty, i + 1 <= progressIndex ? "AccentBrush" : "TrackOff");
        }

        var step = _steps[_step];
        NormalFooter.Visibility = step == StepDone ? Visibility.Collapsed : Visibility.Visible;
        StepCaption.Text = InterfaceText.Format("Step %lld of %lld", progressIndex, 4);
        ContinueButton.Content =
            step == StepWelcome ? InterfaceText.Get("Get Started")
            : step == StepDone ? InterfaceText.Get("Start Using Lingrove")
            : InterfaceText.Get("Continue");
        // Hidden rather than collapsed so the footer height never jumps between steps.
        BackButton.Visibility = step == StepWelcome ? Visibility.Hidden : Visibility.Visible;
        SkipButton.Visibility = step == StepDone ? Visibility.Hidden : Visibility.Visible;

        if (step == StepTemplates) UpdateTemplateStatus();
        if (step == StepDone)
        {
            StepDone.Keys.Text = DoneKeysHint();
            UpdateModelStatusUi();
        }
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowStep(_steps[_step] == StepProfile && _recommendedRoute ? 2 : _step - 1);

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (_steps[_step] == StepTemplates && _templateChosen) CommitTemplate();
        if (_steps[_step] == StepDone) Finish();
        else if (_steps[_step] == StepRecommended) { _recommendedRoute = true; ShowStep(4); }
        else ShowStep(_step + 1);
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Finish();

    /// <summary>Saves what was entered so far and stops the wizard from showing on the next launch.</summary>
    private void Finish()
    {
        var settings = _host.Settings;
        settings.UserName = NameBox.Text.Trim();
        if (settings.Languages.Count == 0) settings.Languages = [.. LanguageCatalog.DefaultLanguages];
        settings.ShowFirstRunWelcome = false;
        settings.OnboardingProgressValue = null;
        _host.PersistSettings();
        // A model download that is still running continues on the host after this window closes.
        Close();
    }

    // ---------- Starting point ----------

    private static string TemplateId(Border card) => (string)card.Tag;

    private void OnTemplateClicked(object sender, MouseButtonEventArgs e)
    {
        _templateModelId = TemplateId((Border)sender);
        _templateChosen = true;
        UpdateTemplateVisuals();
        UpdateTemplateStatus();
    }

    private void UpdateTemplateVisuals()
    {
        foreach (var (card, badge) in _templates)
        {
            bool selected = _templateChosen && TemplateId(card) == _templateModelId;
            card.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "CardBorder");
            badge.SetResourceReference(Border.BackgroundProperty, selected ? "AccentTint" : "KeycapBg");
        }
    }

    private void UpdateTemplateStatus()
    {
        if (ModelCatalog.FindById(_templateModelId) is not { } model)
        {
            TemplateStatusText.Text = "";
            return;
        }
        bool ready = ModelDownloader.IsCached(model);
        TemplateStatusText.Text = !_templateChosen ? InterfaceText.Get("Choose a model to configure it, or continue without changes.") : ready
            ? InterfaceText.Get("Model ready")
            : InterfaceText.Format("Will download {0:F1} GB on Continue", model.ApproxSizeBytes / 1_000_000_000.0);
        TemplateStatusText.SetResourceReference(TextBlock.ForegroundProperty, ready ? "GreenBrush" : "TextSecondary");
    }

    private void CommitTemplate()
    {
        _host.Settings.ActiveModelId = _templateModelId;
        _host.PersistSettings();
        StartModelEnsure(_templateModelId);
    }

    // ---------- Background model preparation ----------

    private async void StartModelEnsure(string modelId)
    {
        if (_modelReadyFor == modelId && _modelOperation is { } readyToken
            && _statusGate.IsCurrent(readyToken, _host.CompletionStatusEpoch))
        {
            UpdateModelStatusUi();
            return;
        }
        // Confirming the same starting point again must not restart a running download.
        if (_ensureBusy && _lastEnsureFor == modelId) return;

        if (_host.CompletionStatusEpoch is not { } epoch)
        {
            _modelReadyFor = null; _modelError = null; _ensureBusy = false; UpdateModelStatusUi(); return;
        }
        var status = _statusGate.Begin(epoch); _modelOperation = status;
        if (_host.AdmittedLocalModel?.Id == modelId)
        {
            _modelReadyFor = modelId;
            _modelError = null;
            _ensureBusy = false;
            UpdateModelStatusUi();
            return;
        }

        _modelCts?.Cancel();
        using var transfer = new CancellationTokenSource();
        _modelCts = transfer;
        var seq = ++_ensureSeq;
        _lastEnsureFor = modelId;
        _ensureBusy = true;
        _modelReadyFor = null;
        _modelError = null;
        _modelPercent = null;
        UpdateModelStatusUi();

        var progress = new Progress<ModelDownloader.Progress>(p =>
        {
            if (seq != _ensureSeq || !_statusGate.IsCurrent(status, _host.CompletionStatusEpoch)) return;
            if (p.TotalBytes is long total && total > 0)
            {
                _modelPercent = (int)(p.BytesDownloaded * 100 / total);
            }
            UpdateModelStatusUi();
        });

        try
        {
            bool ready = await _host.EnsureModelReadyAsync(progress, transfer.Token)
                && _host.AdmittedLocalModel?.Id == modelId;
            if (seq == _ensureSeq && _statusGate.IsCurrent(status, _host.CompletionStatusEpoch))
            {
                _modelReadyFor = ready ? modelId : null;
                _modelError = ready ? null : "The selected model is unavailable.";
                _ensureBusy = false;
            }
        }
        catch (OperationCanceledException)
        {
            if (seq == _ensureSeq && _statusGate.IsCurrent(status, _host.CompletionStatusEpoch)) _ensureBusy = false;
        }
        catch (Exception ex)
        {
            if (seq == _ensureSeq && _statusGate.IsCurrent(status, _host.CompletionStatusEpoch))
            {
                _modelError = ModelDisplayText.Safe(ex.Message);
                _ensureBusy = false;
            }
        }
        finally { if (ReferenceEquals(_modelCts, transfer)) _modelCts = null; }
        if (seq == _ensureSeq && _statusGate.IsCurrent(status, _host.CompletionStatusEpoch)) UpdateModelStatusUi();
    }

    private void HostSettingsApplied()
    {
        if (_modelOperation is not { } operation || _statusGate.IsCurrent(operation, _host.CompletionStatusEpoch)) return;
        _statusGate.Invalidate(); _modelOperation = null; _ensureSeq++;
        _modelCts?.Cancel(); _ensureBusy = false; _modelReadyFor = null; _modelError = null; _modelPercent = null;
        UpdateModelStatusUi();
    }

    private void UpdateModelStatusUi()
    {
        _readiness.Refresh();
    }

    // ---------- Keybinds ----------

    private void BuildShortcutRows()
    {
        foreach (var shortcut in new[] { GlobalShortcut.AcceptWord, GlobalShortcut.AcceptAll, GlobalShortcut.Toggle })
        {
            var editor = new ShortcutEditor(shortcut, () => _host.Settings, ChangeShortcut)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _shortcuts.Add(editor);
            ShortcutRows.Children.Add(new Grid { Children = { Ui.Text(InterfaceText.Get(shortcut.Name)), editor } }.Styled("SettingRow"));
            ShortcutRows.Children.Add(Ui.Separator());
        }
        RefreshShortcuts();
    }

    private void ChangeShortcut(Action<AppSettings> change)
    {
        change(_host.Settings);
        _host.PersistSettings();
        RefreshShortcuts();
    }

    /// <summary>Refreshes every editor, because one binding can change how another one reads.</summary>
    private void RefreshShortcuts()
    {
        foreach (var editor in _shortcuts) editor.Refresh();
    }

    private void OnModeClicked(object sender, RoutedEventArgs e)
    {
        _host.Settings.AcceptanceMode = ModePhrase.IsChecked == true
            ? AcceptanceGranularity.Phrase
            : AcceptanceGranularity.Word;
        _host.PersistSettings();
    }

    private string DoneKeysHint()
    {
        var settings = _host.Settings;
        bool word = settings.AcceptWord.IsBound;
        bool full = GlobalShortcut.AcceptAll.Binding(settings).IsBound || (settings.DoubleTapAcceptsAll && word);
        string wordKey = GlobalShortcut.AcceptWord.Label(settings);
        string fullKey = GlobalShortcut.AcceptAll.Label(settings);
        return (word, full) switch
        {
            (true, true) => InterfaceText.Format("Start typing anywhere.\nPress %@ to accept a word, %@ for the full suggestion.", wordKey, fullKey),
            (true, false) => InterfaceText.Format("Start typing anywhere.\nPress %@ to accept.", wordKey),
            (false, true) => InterfaceText.Format("Start typing anywhere.\nPress {0} to accept the full suggestion.", fullKey),
            _ => InterfaceText.Get("Start typing anywhere."),
        };
    }
}
