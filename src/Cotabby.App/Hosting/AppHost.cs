using System.Net.Http;
using Cotabby.App.Learning;
using Cotabby.App.Emoji;
using Cotabby.App.Macros;
using Cotabby.App.Overlay;
using Cotabby.App.Settings;
using Cotabby.App.UI;
using Cotabby.App.Vocabulary;
using Cotabby.Core.Focus;
using Cotabby.Core.Context;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Spelling;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Vocabulary;
using Cotabby.Inference;
using Cotabby.Inference.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Focus;
using Cotabby.Win32.Input;
using Cotabby.Win32.Insertion;
using Cotabby.Win32.LanguageLearning;
using Cotabby.Win32.Power;
using Cotabby.Win32.Security;
using Cotabby.Win32.Spelling;
using Cotabby.Win32.Text;
using Cotabby.Win32.Vocabulary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Cotabby.App.Hosting;

/// <summary>
/// Composition root. Builds the long-lived object graph once at startup and
/// pushes settings changes into it.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    public static string LogPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cotabby", "logs", "cotabby.log");

    /// <summary>Debug logs include typed and suggested text, so they are opt-in.</summary>
    private static readonly bool DebugLogging = Environment.GetEnvironmentVariable("COTABBY_DEBUG_LOG") == "1";

    private readonly ServiceProvider _services;
    private readonly IKeyboardHook _hook;
    private readonly SuggestionEngineRouter _router;
    private readonly SynchronizationContext _ui;
    private readonly ILogger _logger;
    private readonly GhostOverlayWindow _macroPreview;
    private readonly SymSpellCorrector _spelling;
    private readonly HttpClient _learningHttpClient;
    private int _learningCredentialRevision;

    public SuggestionCoordinator Coordinator { get; }
    public LlamaRuntimeManager Runtime { get; }
    public ModelDownloader Downloader { get; }
    public SettingsStore SettingsStore { get; }
    public AppSettings Settings { get; private set; }
    public GhostOverlayWindow Overlay { get; }
    public EmojiPickerController EmojiPicker { get; }
    public MacroController Macros { get; }
    public OpenAICompatibleSuggestionEngine Endpoint { get; }
    public OpenAICompatibleApiClient EndpointClient { get; }
    public IOpenAICompatibleCredentialStore EndpointCredentials { get; }
    public IOpenAICompatibleCredentialStore LearningEndpointCredentials { get; }
    internal LearningModelCatalogManager LearningModels { get; }
    internal LearningPreviewController LearningPreview { get; }
    internal LearningExplanationController LearningExplanation { get; }
    internal VocabularyStore Vocabulary { get; }

    /// <summary>Raised on the UI thread after settings were applied or a model was loaded, so open windows and the tray can refresh.</summary>
    public event Action? SettingsApplied;

    public AppHost(
        SynchronizationContext ui, GhostOverlayWindow overlay, GhostOverlayWindow macroPreview,
        EmojiPopupWindow emojiPopup)
    {
        _ui = ui;
        Overlay = overlay;
        _macroPreview = macroPreview;
        // Learning has its own caller-owned HTTP pool. It never borrows the autocomplete client's
        // redirects, timeout, server state, or credentials, and it is not used until a manual request.
        _learningHttpClient = OpenAICompatibleLearningTranslationEngine.CreateHttpClient();
        LearningModels = new LearningModelCatalogManager();
        LearningEndpointCredentials = new DpapiOpenAICompatibleCredentialStore(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cotabby", "learning-endpoint-credential.bin"));

        var sc = new ServiceCollection();
        sc.AddLogging(b =>
        {
            b.SetMinimumLevel(DebugLogging ? LogLevel.Debug : LogLevel.Information);
            b.AddDebug();
            b.AddProvider(new FileLoggerProvider(LogPath));
        });
        sc.AddSingleton(_ =>
        {
            // GGUF downloads are large, so the timeout is generous.
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Cotabby-Windows/0.2 (+https://github.com/Msde-7/cotabby-windows)");
            return http;
        });
        sc.AddSingleton<SettingsStore>();
        sc.AddSingleton<IFocusTracker, UiaFocusTracker>();
        sc.AddSingleton<IKeyboardHook, KeyboardHook>();
        sc.AddSingleton<ClipboardTextReader>();
        sc.AddSingleton<IClipboardTextWriter, ClipboardTextWriter>();
        sc.AddSingleton<ITextInserter, SendInputTextInserter>();
        sc.AddSingleton<LlamaRuntimeManager>();
        sc.AddSingleton<ModelDownloader>();
        sc.AddSingleton<LlamaSuggestionEngine>();
        sc.AddSingleton<OpenAICompatibleApiClient>(_ => new OpenAICompatibleApiClient());
        sc.AddSingleton<IOpenAICompatibleCredentialStore>(_ => new DpapiOpenAICompatibleCredentialStore());
        sc.AddSingleton<ILearningSentenceTokenizer, IcuLearningSentenceTokenizer>();
        sc.AddSingleton<ILearningEvidenceSentenceTokenizer, IcuLearningEvidenceSentenceTokenizer>();
        sc.AddSingleton<LearningExplanationInputFactory>();
        sc.AddSingleton<VocabularyCaptureFactory>();
        // Recognition and dictionary cards share one store. Bundle resolution checks availability;
        // resource parsing/indexing and ICU calls remain lazy and run on the service worker.
        sc.AddSingleton<ILearningLexicalText, IcuLearningLexicalText>();
        sc.AddSingleton<ILearningLexiconResourceSource>(_ =>
            BundledLearningLexiconResourceSource.FromBundleDirectory(AppContext.BaseDirectory));
        sc.AddSingleton<LearningLexiconService>();
        sc.AddSingleton<ILearningUnitRecognizing>(services => services.GetRequiredService<LearningLexiconService>());
        sc.AddSingleton<ILearningDictionaryProviding>(services => services.GetRequiredService<LearningLexiconService>());
        // Translation owns a separate lazy runtime and native context; this singleton is only
        // created at startup, and its implementation does not load weights until a manual request.
        sc.AddSingleton<ILearningLocalRuntime, LearningLocalRuntime>();
        _services = sc.BuildServiceProvider();

        SettingsStore = _services.GetRequiredService<SettingsStore>();
        Settings = SettingsStore.Load();
        Runtime = _services.GetRequiredService<LlamaRuntimeManager>();
        Downloader = _services.GetRequiredService<ModelDownloader>();
        EndpointClient = _services.GetRequiredService<OpenAICompatibleApiClient>();
        EndpointCredentials = _services.GetRequiredService<IOpenAICompatibleCredentialStore>();

        var loggers = _services.GetRequiredService<ILoggerFactory>();
        // Explanation is a third independent native owner. Construction is inert; only an
        // explicit request through its service can load weights, and shutdown drains it first.
        LearningExplanation = new LearningExplanationController(
            new LearningLocalRuntime(loggers.CreateLogger<LearningLocalRuntime>(), loggers), Settings,
            ModelCatalog.DefaultLocalDirectory(), batterySaverOn: () => PowerStatus.IsBatterySaverOn);
        // One notebook owner is shared by every card/window. Construction does no IO; saved
        // explanation lookup or a user action loads it lazily. Saving never starts generation.
        Vocabulary = new VocabularyStore(new VocabularyArchive(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cotabby", "vocabulary", "vocabulary.sqlite")), ui);
        Vocabulary.EnrichCapture = capture =>
        {
            var input = LearningExplanationPolicy.Input(capture);
            try { return capture with { Explanation = LearningExplanation.Service.Cached(input) ?? LearningExplanation.Service.SavedRecord(input) }; }
            // Annotation is optional. A shutdown race must preserve the captured favorite even
            // when the explanation service has already closed its saved-lookup admission.
            catch (ObjectDisposedException) { return capture; }
        };
        LearningExplanation.Service.LoadPersisted = async (input, language) =>
        {
            await Vocabulary.LoadAsync().ConfigureAwait(false);
            return Vocabulary.Explanation(input, language);
        };
        LearningExplanation.Service.LookupSaved = Vocabulary.Explanation;
        LearningExplanation.Service.Explained += PersistExplanation;
        _spelling = new SymSpellCorrector(logger: loggers.CreateLogger<SymSpellCorrector>());
        _logger = loggers.CreateLogger("Cotabby.App");
        _hook = _services.GetRequiredService<IKeyboardHook>();
        var focus = _services.GetRequiredService<IFocusTracker>();
        var inserter = _services.GetRequiredService<ITextInserter>();

        Endpoint = new OpenAICompatibleSuggestionEngine(
            EndpointClient, EndpointCredentials, EndpointSettings,
            loggers.CreateLogger<OpenAICompatibleSuggestionEngine>());
        _router = new SuggestionEngineRouter(_services.GetRequiredService<LlamaSuggestionEngine>(), Endpoint);

        IcuWordBreaker.Install();

        // Inline features subscribe to the hook first so they see each key before the coordinator.
        EmojiPicker = new EmojiPickerController(
            _hook, focus, inserter, emojiPopup, overlay.Dispatcher,
            loggers.CreateLogger<EmojiPickerController>());
        Macros = new MacroController(_hook, focus, inserter, macroPreview, loggers.CreateLogger<MacroController>());
        _hook.KeyEvent += OnGlobalToggleKey;

        Coordinator = new SuggestionCoordinator(
            _hook, focus, _router, overlay, inserter, new SuggestionWorkController(TimeSpan.Zero),
            loggers.CreateLogger<SuggestionCoordinator>(), ui,
            batterySaverOn: () => PowerStatus.IsBatterySaverOn,
            typos: new TypoCorrectionAdvisor(
                _spelling, new SpellingLanguageResolver(), new WindowsSpellChecker(_spelling)),
            clipboard: _services.GetRequiredService<ClipboardTextReader>());
        Coordinator.InlineCaptureActive = () => EmojiPicker.IsCapturing || Macros.IsCapturing;
        EmojiPicker.CanCapture = Macros.CanCapture = Coordinator.IsAvailableIn;
        EmojiPicker.CommitKeyFor = Macros.CommitKeyFor = Coordinator.AcceptWordKeyFor;
        Coordinator.Metrics.Restore(Settings.WordsAccepted);
        LearningPreview = new LearningPreviewController(
            _hook, focus, overlay.Dispatcher,
            _services.GetRequiredService<ILearningSentenceTokenizer>(),
            _services.GetRequiredService<ILearningLocalRuntime>(),
            _learningHttpClient, LearningEndpointCredentials,
            loggers.CreateLogger<LearningPreviewController>(),
            // Multi-source choices require a platform confidence provider. None is registered on
            // this Windows slice, so the controller truthfully fails closed until one is selected.
            _services.GetService<ILearningLanguageDetector>(),
            batterySaverOn: () => PowerStatus.IsBatterySaverOn,
            unitRecognizer: _services.GetRequiredService<ILearningUnitRecognizing>(),
            dictionaryProvider: _services.GetRequiredService<ILearningDictionaryProviding>(),
            explanationService: LearningExplanation.Service,
            explanationInputs: _services.GetRequiredService<LearningExplanationInputFactory>(),
            configureExplanation: () => SettingsWindow.ShowLearning(this),
            clipboardWriter: _services.GetRequiredService<IClipboardTextWriter>(),
            vocabulary: Vocabulary,
            vocabularyCaptures: _services.GetRequiredService<VocabularyCaptureFactory>());

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        ApplySettings();
    }

    private OpenAICompatibleEndpointSettings EndpointSettings => new(
        Settings.EndpointBaseUrl,
        Settings.EndpointModel,
        Settings.EndpointUsesChatApi ? OpenAICompatibleApiMode.ChatCompletions : OpenAICompatibleApiMode.Completions);

    private void PersistExplanation(LearningExplanationRecord record) => _ = Vocabulary.SaveExplanationAsync(record);

    /// <summary>Pushes <see cref="Settings"/> into the running app and saves them.</summary>
    public void PersistSettings()
    {
        ApplySettings();
        Settings.WordsAccepted = Coordinator.Metrics.WordsAccepted;
        SettingsStore.Save(Settings);
    }

    /// <summary>Invalidates only an active learning request after its separate DPAPI key changes.</summary>
    public void LearningCredentialsChanged()
    {
        _learningCredentialRevision++;
        LearningPreview.ApplySettings(Settings, _learningCredentialRevision);
    }

    /// <summary>Restores every preference to its default. The chosen model and onboarding state stay.</summary>
    public void ResetSettings()
    {
        Settings = new AppSettings
        {
            ActiveModelId = Settings.ActiveModelId,
            ShowFirstRunWelcome = Settings.ShowFirstRunWelcome,
            WordsAccepted = Coordinator.Metrics.WordsAccepted,
        };
        PersistSettings();
    }

    private void ApplySettings()
    {
        // An expired pause is dropped so the tray stops showing it.
        if (Settings.Pause?.IsActive(DateTimeOffset.Now) == false) Settings.Pause = null;

        Coordinator.Settings = Settings.ToSuggestionSettings();
        _router.UseEndpoint = Settings.Engine == EngineKind.Endpoint;
        Endpoint.UpdateSettings(EndpointSettings);
        // A cold local server would make the first suggestion wait for the model to load.
        if (Settings.Engine == EngineKind.Endpoint) _ = Endpoint.PrewarmAsync();
        EmojiPicker.Enabled = Settings.EmojiPickerEnabled;
        EmojiPicker.PreferredSkinTone = Settings.EmojiSkinTone;
        EmojiPicker.PreferredGender = Settings.EmojiGender;
        Macros.Enabled = Settings.MacrosEnabled;
        LearningPreview.ApplySettings(Settings, _learningCredentialRevision);
        LearningExplanation.ApplySettings(Settings);
        Overlay.ApplyAppearance(Settings);
        _macroPreview.ApplyAppearance(Settings);
        LaunchAtLogin.Set(Settings.LaunchAtLogin);
        LoadSelectedModelIfChanged();
        SettingsApplied?.Invoke();
    }

    private void OnGlobalToggleKey(object? sender, KeyEventArgs args)
    {
        if (!args.Event.IsKeyDown || args.Event.IsRepeat || !Settings.GlobalToggle.Matches(args.Event)) return;
        args.Suppress = true;
        _ui.Post(_ =>
        {
            Settings.Enabled = !Settings.Enabled;
            Settings.Pause = null;
            PersistSettings();
        }, null);
    }

    public void LogError(Exception ex, string message) => _logger.LogError(ex, "{Message}", message);

    /// <summary>Loads the selected model if it is already on disk. Never downloads.</summary>
    public async Task<bool> TryLoadCachedAsync()
    {
        var model = ResolveActiveModel();
        if (!ModelDownloader.IsCached(model)) return false;
        return await LoadAsync(model).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the selected model if needed, then loads it. Only the download can be cancelled.
    /// A cancelled load would leave no model in memory, because the old one is released first.
    /// </summary>
    public async Task<bool> EnsureModelReadyAsync(IProgress<ModelDownloader.Progress>? progress, CancellationToken ct)
    {
        var model = ResolveActiveModel();
        if (!ModelDownloader.IsCached(model))
        {
            await Downloader.DownloadAsync(model, progress, ct).ConfigureAwait(false);
            // The selection may have changed during a long download.
            model = ResolveActiveModel();
            if (!ModelDownloader.IsCached(model)) return Runtime.IsReady;
        }
        return await LoadAsync(model).ConfigureAwait(false);
    }

    private async Task<bool> LoadAsync(CotabbyModel model)
    {
        await Runtime.LoadAsync(model, ModelDownloader.LocalPath(model), CancellationToken.None).ConfigureAwait(false);
        // The tray and any open window show which model is loaded.
        _ui.Post(_ => SettingsApplied?.Invoke(), null);
        return Runtime.IsReady;
    }

    private CotabbyModel ResolveActiveModel()
    {
        var model = (Settings.ActiveModelId is { } id ? ModelCatalog.FindById(id) : null) ?? ModelCatalog.All[0];
        Settings.ActiveModelId = model.Id;
        return PowerSourceModel() ?? model;
    }

    /// <summary>The model chosen for the current power source, when that feature is on and the file is present.</summary>
    private CotabbyModel? PowerSourceModel()
    {
        if (!Settings.PowerModelSwitching) return null;
        var id = PowerStatus.IsOnBattery ? Settings.BatteryModelId : Settings.PluggedInModelId;
        return ModelCatalog.FindById(id) is { } model && ModelDownloader.IsCached(model) ? model : null;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange) return;
        // Power availability is a shared feature gate. Learning owns its own request/cache, but it
        // must retire that work when the same battery-saver pause used by autocomplete becomes active.
        LearningPreview.PowerStatusChanged();
        LearningExplanation.PowerStatusChanged();
        _ui.Post(_ => LoadSelectedModelIfChanged(), null);
    }

    /// <summary>
    /// Loads the model the settings and the power source now call for, when one is already
    /// loaded and it is a different one. A first load is left to the caller that shows progress.
    /// </summary>
    private async void LoadSelectedModelIfChanged()
    {
        try
        {
            if (Settings.Engine != EngineKind.Local || !Runtime.IsReady) return;
            var wanted = ResolveActiveModel();
            if (wanted.Id == Runtime.ActiveModel?.Id || !ModelDownloader.IsCached(wanted)) return;
            await LoadAsync(wanted);
        }
        catch (Exception ex)
        {
            LogError(ex, "Switching models failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Settings.WordsAccepted = Coordinator.Metrics.WordsAccepted;
        SettingsStore.Save(Settings);
        _hook.KeyEvent -= OnGlobalToggleKey;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        await LearningPreview.DisposeAsync().ConfigureAwait(false);
        Vocabulary.EnrichCapture = null;
        await LearningExplanation.DisposeAsync().ConfigureAwait(false);
        LearningExplanation.Service.Explained -= PersistExplanation;
        await Vocabulary.DisposeAsync().ConfigureAwait(false);
        await LearningModels.DisposeAsync().ConfigureAwait(false);
        EmojiPicker.Dispose();
        Macros.Dispose();
        _spelling.Dispose();
        await Coordinator.DisposeAsync().ConfigureAwait(false);
        await Runtime.DisposeAsync().ConfigureAwait(false);
        EndpointClient.Dispose();
        _learningHttpClient.Dispose();
        await _services.DisposeAsync().ConfigureAwait(false);
    }
}
