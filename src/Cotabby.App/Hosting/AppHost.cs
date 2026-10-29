using System.Net.Http;
using Cotabby.App.Learning;
using Cotabby.App.Reading;
using Cotabby.App.Review;
using Cotabby.App.Emoji;
using Cotabby.App.Macros;
using Cotabby.App.Overlay;
using Cotabby.App.Settings;
using Cotabby.App.UI;
using Cotabby.App.Vocabulary;
using Cotabby.App.UI.ExpressionAssist;
using Cotabby.Win32.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Context;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.Models;
using Cotabby.Core.Spelling;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Notebook;
using Cotabby.Inference;
using Cotabby.Inference.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;
using Cotabby.Inference.OpenAICompatible;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Focus;
using Cotabby.Win32.Input;
using Cotabby.Win32.Insertion;
using Cotabby.Win32.LanguageLearning;
using Cotabby.Win32.Reading;
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
    private readonly CompletionModelAdmission _completion;
    private readonly SuggestionSettingsApplication _suggestionApplication;
    private readonly SynchronizationContext _ui;
    private readonly ILogger _logger;
    private readonly GhostOverlayWindow _macroPreview;
    private readonly SymSpellCorrector _spelling;
    private readonly HttpClient _learningHttpClient;
    private int _learningCredentialRevision;
    private int _completionCredentialRevision;
    private bool _completionStarted;
    private volatile bool _shuttingDown;
    private CompletionModelSelection.PowerProfileState? _lastPowerSelection;
    // UI-owned sharing of one cold preparation per selection epoch. Individual keystrokes
    // may stop waiting without cancelling the selection-owned native load.
    private readonly CompletionPreparationSharing _lazyPreparation = new();

    public SuggestionCoordinator Coordinator { get; }
    public LlamaRuntimeManager Runtime { get; }
    /// <summary>Only an admitted current model is usable or presented as ready.</summary>
    public bool LocalModelReady => _completion.IsReady;
    public CotabbyModel? AdmittedLocalModel => _completion.ReadAdmitted(() => Runtime.ActiveModel);
    public long? CompletionStatusEpoch => _shuttingDown ? null : _completion.LocalSelectionEpoch;
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
    internal RecommendedSetupController RecommendedSetup { get; }
    internal LearningPreviewController LearningPreview { get; }
    internal LearningExplanationController LearningExplanation { get; }
    private readonly IAsyncDisposable? _expressionEditor;
    private readonly ExpressionAssistApplication _expressions;
    private readonly Win32ReadingSelectionReader? _nativeReading;
    private readonly ReadingApplication? _reading;
    private readonly CanonicalVocabularyFeature _notebook;
    internal CanonicalWritingViewStore Vocabulary => _notebook.Writing;
    internal AnkiReviewFeature Review { get; }
    internal FrozenVocabularyWindowController VocabularyWindow => _notebook.Window;

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
        sc.AddSingleton<InsertionTransactionGate>();
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
        sc.AddSingleton<IcuLearningLexicalText>();
        sc.AddSingleton<ILearningLexicalText>(services => services.GetRequiredService<IcuLearningLexicalText>());
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
        _notebook = new CanonicalVocabularyFeature(CanonicalVocabularyFeature.DefaultPath, ui, overlay.Dispatcher,
            new VocabularyNotebookListPreferences(() => Settings, () => SettingsStore.Save(Settings)),
            NotebookLabelLanguages());
        // Review has its own inert HTTP/key owners. Its settings save only their durable values;
        // editing a deck or connection must not wake/reconfigure the autocomplete runtime.
        Review = AnkiReviewFeature.CreateConnectionSettings(() => Settings, () => SettingsStore.Save(Settings),
            new Cotabby.Win32.Review.AnkiConnectClient(), new DpapiAnkiCredentialStore());
        Review.Controller.Configure = () => SettingsWindow.ShowReview(this);
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
        _completion = new CompletionModelAdmission(_services.GetRequiredService<LlamaSuggestionEngine>());
        _router = new SuggestionEngineRouter(_completion, Endpoint);

        IcuWordBreaker.Install();

        // One insertion gate covers completion/emoji/macros and the explicit expression edit.
        // The panel borrows this exact focus/input owner and the existing explanation runtime.
        var nativeFocus = (UiaFocusTracker)focus;
        IExpressionAssistEditor expressionAdapter = new UnavailableExpressionAssistEditor();
        if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            var nativeEditor = new Win32ExpressionEditor(nativeFocus, _hook,
                _services.GetRequiredService<InsertionTransactionGate>(), _services.GetRequiredService<ClipboardTextReader>());
            _expressionEditor = nativeEditor;
            expressionAdapter = new NativeExpressionAssistEditor(nativeEditor);
        }
        _expressions = new(overlay.Dispatcher, focus, _hook, nativeFocus.RefreshGuarded,
            expressionAdapter,
            (request, token) => LearningExplanation.GenerateExpressionAsync(request,
                _services.GetRequiredService<IcuLearningLexicalText>().Lowercase, token),
            () => { Coordinator?.InvalidateSession(); LearningPreview?.SuspendForInlineFeature(); });

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
        _suggestionApplication = new SuggestionSettingsApplication(Coordinator, _completion);
        Coordinator.EnsureLocalReady = EnsureSelectedCompletionReadyAsync;
        Coordinator.InlineCaptureActive = () => _expressions.IsActive || _reading?.IsActive == true || EmojiPicker.IsCapturing || Macros.IsCapturing;
        EmojiPicker.CanCapture = Macros.CanCapture = field => !_expressions.IsActive && _reading?.IsActive != true && Coordinator.IsAvailableIn(field);
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
        LearningPreview.InlineCaptureActive = () => _expressions.IsActive || _reading?.IsActive == true;
        _expressions.OtherInlineActive = () => _reading?.IsActive == true;
        if (OperatingSystem.IsWindowsVersionAtLeast(8, 1))
        {
            _nativeReading = new Win32ReadingSelectionReader(nativeFocus, _hook);
            var lexical = _services.GetRequiredService<IcuLearningLexicalText>();
            _reading = new ReadingApplication(overlay.Dispatcher, _hook, focus,
                new NativeReadingSelectionReader(_nativeReading), (text, language) => language is { } known
                    ? lexical.Words(text, known) : lexical.WordsUnknown(text),
                _services.GetRequiredService<ILearningDictionaryProviding>(),
                (input, token) => LearningExplanation.GenerateReadingLookupAsync(input, null,
                    raw => ReadingLookupPolicy.Parse(raw, input, lexical.Lowercase), token),
                (input, basic, token) => LearningExplanation.GenerateReadingLookupAsync(input, basic,
                    ReadingLookupPolicy.ParseDetails, token),
                () => SettingsWindow.ShowLearning(this), LearningPreview.ReadingTranslator,
                () => { Coordinator.InvalidateSession(); LearningPreview.SuspendForInlineFeature(); },
                () => _expressions.IsActive || EmojiPicker.IsCapturing || Macros.IsCapturing);
        }

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        ApplySettings();
        RecommendedSetup = new RecommendedSetupController(new RecommendedSetupHost(this, ui), Cotabby.Win32.Models.WindowsPhysicalMemory.GetBytes());
    }

    private OpenAICompatibleEndpointSettings EndpointSettings => new(
        Settings.EndpointBaseUrl,
        Settings.EndpointModel,
        Settings.EndpointUsesChatApi ? OpenAICompatibleApiMode.ChatCompletions : OpenAICompatibleApiMode.Completions);

    private void PersistExplanation(LearningExplanationRecord record) => _ = Vocabulary.SaveExplanationAsync(record);

    /// <summary>Pushes <see cref="Settings"/> into the running app and saves them.</summary>
    public void PersistSettings()
    {
        if (_shuttingDown) return;
        ApplySettings();
        Settings.WordsAccepted = Coordinator.Metrics.WordsAccepted;
        SettingsStore.Save(Settings);
    }

    internal void PersistRecommendedSelection()
    {
        if (_shuttingDown) return;
        ApplySettings(allowCompletionLoad: false);
        Settings.WordsAccepted = Coordinator.Metrics.WordsAccepted;
        SettingsStore.Save(Settings);
    }

    /// <summary>Completes an already published model-file operation on the UI owner, including
    /// during shutdown while the model manager drains accepted work.</summary>
    internal void AcknowledgeModelMetadata(Action<AppSettings> mutate)
    {
        mutate(Settings);
        if (_shuttingDown) SettingsStore.Save(Settings);
        else PersistSettings();
    }

    /// <summary>Invalidates only an active learning request after its separate DPAPI key changes.</summary>
    public void LearningCredentialsChanged()
    {
        _learningCredentialRevision++;
        LearningPreview.ApplySettings(Settings, _learningCredentialRevision);
        _reading?.ApplySettings(Settings, _learningCredentialRevision, PowerStatus.IsBatterySaverOn);
    }

    public void CompletionCredentialsChanged()
    {
        if (_shuttingDown) return;
        _completionCredentialRevision++;
        ApplySettings();
    }

    /// <summary>Restores every preference to its default. The chosen model and onboarding state stay.</summary>
    public void ResetSettings()
    {
        Settings = Settings.ResetGeneralPreferences(Coordinator.Metrics.WordsAccepted);
        PersistSettings();
    }

    private void ApplySettings(bool discoveryChanged = false, bool allowCompletionLoad = true)
    {
        if (_shuttingDown) return;
        // An expired pause is dropped so the tray stops showing it.
        if (Settings.Pause?.IsActive(DateTimeOffset.Now) == false) Settings.Pause = null;

        var suggestionSettings = Settings.ToSuggestionSettings();
        var endpointSettings = EndpointSettings;
        var epoch = _completion.Epoch;

        var assignments = Settings.ModelAssignmentSnapshot();
        bool powerActive = Settings.Engine == EngineKind.Local && Settings.PowerModelSwitching;
        bool onBattery = powerActive && PowerStatus.IsOnBattery;
        var powerState = new CompletionModelSelection.PowerProfileState(powerActive, onBattery,
            powerActive ? Settings.BatteryModelId ?? "" : "", powerActive ? Settings.PluggedInModelId ?? "" : "");
        bool powerChanged = discoveryChanged || CompletionModelSelection.PowerChanged(_lastPowerSelection, powerState);
        var power = powerChanged && powerActive ? PowerSourceModel(assignments, onBattery) : null;
        var nextId = CompletionModelSelection.ApplyPower(_lastPowerSelection, powerState, Settings.ActiveModelId, power?.Id, discoveryChanged);
        var desired = power is not null && nextId == power.Id ? power : ResolveMainModel(assignments);
        // Resolve the current profile on either profile edit. Only an effective selection or
        // eligibility change should invalidate in-flight local completion work.
        var transition = _suggestionApplication.Apply(suggestionSettings,
            (endpointSettings, _completionCredentialRevision), new CompletionModelSelection.SelectionIdentity(nextId, desired?.Id));
        bool localEnabled = Settings.Engine == EngineKind.Local && Settings.Enabled;
        // Apply withdraws once for an effective local transition. A concurrent power withdrawal
        // advances the epoch again and must prevent this resolved snapshot from being admitted.
        long selectionEpoch = transition.SelectionEpoch(epoch);
        var accepted = _completion.SelectIfEpoch(selectionEpoch, localEnabled ? desired?.Id : null, localEnabled);
        bool powerSelectionChanged = false;
        if (accepted is not null && _completion.CommitIfEpoch(accepted.Value.Epoch, () =>
        {
            // Mac's power profile selects and persists the installed model; an unavailable profile
            // leaves the last selected model alone rather than restoring a different fallback.
            _lastPowerSelection = powerState;
            if (Settings.ActiveModelId != nextId)
            { Settings.ActiveModelId = nextId; powerSelectionChanged = true; }
        }) && powerSelectionChanged) SaveAutomaticModelSelection();
        _router.UseEndpoint = Settings.Engine == EngineKind.Endpoint;
        if (transition.EndpointChanged) Endpoint.UpdateSettings(endpointSettings);
        // A cold local server would make the first suggestion wait for the model to load.
        if (Settings.Engine == EngineKind.Endpoint && transition.EndpointChanged) _ = Endpoint.PrewarmAsync();
        EmojiPicker.Enabled = Settings.EmojiPickerEnabled;
        EmojiPicker.PreferredSkinTone = Settings.EmojiSkinTone;
        EmojiPicker.PreferredGender = Settings.EmojiGender;
        Macros.Enabled = Settings.MacrosEnabled;
        LearningPreview.ApplySettings(Settings, _learningCredentialRevision);
        LearningExplanation.ApplySettings(Settings);
        _reading?.ApplySettings(Settings, _learningCredentialRevision, PowerStatus.IsBatterySaverOn);
        _expressions.ApplySettings(Settings, PowerStatus.IsBatterySaverOn);
        VocabularyWindow.SetPreferredLanguages(NotebookLabelLanguages());
        Overlay.ApplyAppearance(Settings);
        _macroPreview.ApplyAppearance(Settings);
        LaunchAtLogin.Set(Settings.LaunchAtLogin);
        if (allowCompletionLoad && RecommendedSetup?.Busy != true && !_completion.IsReady) LoadSelectedModelIfChanged();
        SettingsApplied?.Invoke();
    }

    // The current Windows interface is English. Explicit explanation language has priority;
    // changing it updates labels only, preserving the notebook's authored languages and selection.
    private IReadOnlyList<LearningLanguage> NotebookLabelLanguages() =>
        VocabularyMeaningLabelPolicy.PreferredLanguages(Settings.LearningExplanationLanguage, ["en"]);

    private void OnGlobalToggleKey(object? sender, KeyEventArgs args)
    {
        if (args.Suppress || !args.Event.IsKeyDown || args.Event.IsRepeat || !Settings.GlobalToggle.Matches(args.Event)) return;
        args.Suppress = true;
        _ui.Post(_ =>
        {
            Settings.Enabled = !Settings.Enabled;
            Settings.Pause = null;
            PersistSettings();
        }, null);
    }

    public void LogError(Exception ex, string message) => _logger.LogError(ex, "{Message}", message);

    // Retire publication first, then invalidate captured edits and any visible suggestion. The
    // coordinator retains the user's enabled state, so unrelated inline features stay available.
    private void WithdrawCompletion()
    {
        _completion.Withdraw();
        Coordinator.InvalidateSession();
    }

    /// <summary>Loads the selected model if it is already on disk. Never downloads.</summary>
    public async Task<bool> TryLoadCachedAsync()
    {
        if (Settings.Engine == EngineKind.Local && Settings.Enabled) _completionStarted = true;
        ReconcileCompletionSelection();
        var model = ResolveActiveModel();
        if (model is null || !ModelDownloader.IsCached(model) || Settings.Engine != EngineKind.Local || !Settings.Enabled) return false;
        return await PrepareModelAsync(model, allowDownload: false, null, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the selected model if needed, then loads it. Only the download can be cancelled.
    /// A cancelled load would leave no model in memory, because the old one is released first.
    /// </summary>
    public async Task<bool> EnsureModelReadyAsync(IProgress<ModelDownloader.Progress>? progress, CancellationToken ct)
    {
        var model = ResolveActiveModel();
        if (model is null || Settings.Engine != EngineKind.Local || !Settings.Enabled) return false;
        return await PrepareModelAsync(model, allowDownload: true, progress, ct).ConfigureAwait(false);
    }

    private async Task<bool> PrepareModelAsync(CotabbyModel model, bool allowDownload, IProgress<ModelDownloader.Progress>? progress, CancellationToken ct)
    {
        if (_shuttingDown || _completion.Capture(model.Id) is not { } ticket) return false;
        _completionStarted = true;
        bool ready = await _completion.PrepareAsync(ticket, async token =>
        {
            if (ModelDownloader.IsCached(model)) return;
            if (!allowDownload) throw new System.IO.FileNotFoundException("The selected model is unavailable.");
            await Downloader.DownloadAsync(model, progress, token, () => AcknowledgeCompletionDownloadAsync(model)).ConfigureAwait(false);
        }, () => Runtime.LoadAsync(model, ModelDownloader.LocalPath(model), CancellationToken.None),
            () => Runtime.ActiveModel is { } active && ModelCatalog.MatchesId(model.Id, active.Id), ct).ConfigureAwait(false);
        _ui.Post(_ =>
        {
            // A queued UI callback must not restore old authorization after a newer selection.
            if (!_shuttingDown && _completion.IsCurrent(ticket) && _completion.IsReady) SettingsApplied?.Invoke();
        }, null);
        return ready;
    }

    private async Task<bool> EnsureSelectedCompletionReadyAsync(CancellationToken requestCancellation)
    {
        var dispatched = new TaskCompletionSource<Task<bool>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            try
            {
                if (requestCancellation.IsCancellationRequested || _shuttingDown || Settings.Engine != EngineKind.Local
                    || !Settings.Enabled || string.IsNullOrEmpty(Settings.ActiveModelId) || ResolveActiveModel() is not { } model
                    || _completion.Capture(model.Id) is not { } ticket)
                { dispatched.TrySetResult(null); return; }
                if (_completion.IsReady) { dispatched.TrySetResult(Task.FromResult(true)); return; }
                dispatched.TrySetResult(_lazyPreparation.GetOrStart(ticket.Epoch,
                    () => PrepareModelAsync(model, allowDownload: false, null, CancellationToken.None)));
            }
            catch (Exception error) { dispatched.TrySetException(error); }
        }, null);
        var preparation = await dispatched.Task.WaitAsync(requestCancellation).ConfigureAwait(false);
        return preparation is not null && await preparation.WaitAsync(requestCancellation).ConfigureAwait(false);
    }

    internal Task AcknowledgeCompletionDownloadAsync(CotabbyModel model)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            try
            {
                AcknowledgeModelMetadata(settings =>
                {
                    settings.RecordLocalModelTask(model.FileName, LocalModelTask.Completion);
                    settings.RecordLocalModelSource(model.FileName, new Uri(model.DownloadUrl));
                });
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        }, null);
        return completion.Task;
    }

    private CotabbyModel? ResolveActiveModel()
    {
        var assignments = Settings.ModelAssignmentSnapshot();
        return ResolveMainModel(assignments);
    }

    private CotabbyModel? ResolveMainModel(IReadOnlyDictionary<string, string[]> assignments) =>
        string.IsNullOrEmpty(Settings.ActiveModelId) ? ModelCatalog.All[0] : ModelCatalog.FindById(Settings.ActiveModelId, assignments);

    /// <summary>Reconcile discovery like the frozen bootstrap: keep incompatible choices, but
    /// replace a compatible vanished choice with the first installed candidate. An explicit
    /// transfer in progress owns its selection until it finishes.</summary>
    internal void ReconcileCompletionSelection(IReadOnlyList<LearningInstalledModel>? discovered = null)
    {
        if (_shuttingDown) return;
        var installed = discovered is null
            ? ModelCatalog.Available(Settings.ModelAssignmentSnapshot()).Where(ModelDownloader.IsCached).ToArray()
            : discovered.Where(value => Settings.IsLocalModelCandidate(value.Filename, LocalModelTask.Completion)).Select(value =>
                ModelCatalog.All.FirstOrDefault(model => string.Equals(model.FileName, value.Filename, StringComparison.OrdinalIgnoreCase))
                ?? new CotabbyModel { Id = "custom:" + value.Filename, FileName = value.Filename, DisplayName = value.DisplayName,
                    DownloadUrl = "", ApproxSizeBytes = value.SizeBytes, ContextLength = 4096 }).ToArray();
        string profileId = Settings.PowerModelSwitching && Settings.Engine == EngineKind.Local
            ? (PowerStatus.IsOnBattery ? Settings.BatteryModelId : Settings.PluggedInModelId) ?? "" : "";
        var profile = profileId.Length == 0 ? null : installed.FirstOrDefault(model => ModelCatalog.MatchesId(profileId, model.Id));
        bool profileNeedsSelection = profile is not null && !ModelCatalog.MatchesId(Settings.ActiveModelId ?? "", profile.Id);
        if (_completion.IsPreparing && !profileNeedsSelection) return;
        var next = _completion.IsPreparing ? Settings.ActiveModelId : CompletionModelSelection.Reconcile(Settings.ActiveModelId, installed);
        if (string.Equals(next, Settings.ActiveModelId, StringComparison.Ordinal) && !profileNeedsSelection) return;
        if (Settings.Engine == EngineKind.Local && Settings.Enabled) WithdrawCompletion();
        Settings.ActiveModelId = next;
        ApplySettings(discoveryChanged: true);
        SaveAutomaticModelSelection();
    }

    private void SaveAutomaticModelSelection()
    {
        try { SettingsStore.Save(Settings); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { LogError(error, "Saving automatic model selection failed."); }
    }

    /// <summary>The model chosen for the current power source, when that feature is on and the file is present.</summary>
    private CotabbyModel? PowerSourceModel(IReadOnlyDictionary<string, string[]> assignments, bool onBattery)
    {
        if (!Settings.PowerModelSwitching) return null;
        var id = onBattery ? Settings.BatteryModelId : Settings.PluggedInModelId;
        return ModelCatalog.FindById(id, assignments) is { } model && ModelDownloader.IsCached(model) ? model : null;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_shuttingDown || e.Mode != PowerModes.StatusChange) return;
        _completion.Withdraw();
        _reading?.Withdraw();
        if (!_router.UseEndpoint) Coordinator.InvalidateSession();
        // Learning owns separate requests and retires them under its own power eligibility gate.
        LearningPreview.PowerStatusChanged();
        LearningExplanation.PowerStatusChanged();
        _ui.Post(_ =>
        {
            if (Settings.Engine == EngineKind.Local) ApplySettings();
            else if (!_shuttingDown)
            {
                _expressions.ApplySettings(Settings, PowerStatus.IsBatterySaverOn);
                _reading?.ApplySettings(Settings, _learningCredentialRevision, PowerStatus.IsBatterySaverOn);
            }
        }, null);
    }

    /// <summary>
    /// Loads the model the settings and the power source now call for, when one is already
    /// loaded and it is a different one. A first load is left to the caller that shows progress.
    /// </summary>
    private async void LoadSelectedModelIfChanged()
    {
        try
        {
            // A superseded native load can still be finishing after cancellation. Queue the new
            // installed choice behind it even while the physical runtime is temporarily empty.
            if (Settings.Engine != EngineKind.Local || !Settings.Enabled || (!Runtime.IsReady && !_completionStarted)) return;
            var wanted = ResolveActiveModel();
            if (wanted is null || !ModelDownloader.IsCached(wanted)) return;
            await PrepareModelAsync(wanted, allowDownload: false, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogError(ex, "Switching models failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shuttingDown = true;
        Settings.WordsAccepted = Coordinator.Metrics.WordsAccepted;
        try { SettingsStore.Save(Settings); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { LogError(error, "Saving settings during shutdown failed."); }
        _hook.KeyEvent -= OnGlobalToggleKey;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        WithdrawCompletion();
        try
        {
            try { await RetireWritingFeaturesAsync().ConfigureAwait(false); }
            finally
            {
                // Producer or presentation failures must not strand the sole database owner.
                try { await _notebook.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    await LearningModels.DisposeAsync().ConfigureAwait(false);
                    EmojiPicker.Dispose();
                    Macros.Dispose();
                    _spelling.Dispose();
                    await Coordinator.DisposeAsync().ConfigureAwait(false);
                    EndpointClient.Dispose();
                    _learningHttpClient.Dispose();
                    await _services.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await Runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RetireWritingFeaturesAsync()
    {
        try { await RecommendedSetup.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { await _completion.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                try { await RetireReadingAsync().ConfigureAwait(false); }
                finally
                {
                    try { await RetireExpressionAsync().ConfigureAwait(false); }
                    finally { await RetireNotebookProducersAsync().ConfigureAwait(false); }
                }
            }
        }
    }

    private async Task RetireReadingAsync()
    {
        try { if (_reading is not null) await _reading.DisposeAsync().ConfigureAwait(false); }
        finally { if (OperatingSystem.IsWindowsVersionAtLeast(8, 1) && _nativeReading is not null) await _nativeReading.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task RetireExpressionAsync()
    {
        try { await _expressions.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { if (_expressionEditor is not null) await _expressionEditor.DisposeAsync().ConfigureAwait(false); }
            finally { await LearningPreview.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private async Task RetireNotebookProducersAsync()
    {
        try { await VocabularyWindow.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            Vocabulary.OpenSelection = null;
            Vocabulary.EnrichCapture = null;
            try { await LearningExplanation.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                LearningExplanation.Service.Explained -= PersistExplanation;
                await Review.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
