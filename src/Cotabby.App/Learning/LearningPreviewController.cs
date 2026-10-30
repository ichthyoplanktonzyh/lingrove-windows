using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.Context;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Support.Runtime;
using Cotabby.Inference.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;
using Cotabby.Inference.OpenAICompatible;
using Microsoft.Extensions.Logging;

namespace Cotabby.App.Learning;

/// <summary>
/// Adapts the Mac manual current-sentence flow to Windows: the hook only claims the configured
/// chord, UI Automation captures off-thread, Core owns translation request identity, and this
/// controller revalidates the exact field snapshot before rendering a readonly WPF card.
/// </summary>
/// <remarks>
/// The controller owns one transient capture and preview window for the app lifetime. It never
/// edits text or asks the inference engine to rediscover editor content. A complete frozen learning
/// settings projection also means unrelated autocomplete edits do not cancel a sentence request.
/// </remarks>
internal sealed partial class LearningPreviewController : IAsyncDisposable
{
    internal Func<bool>? InlineCaptureActive { get; set; }
    internal void SuspendForInlineFeature() => CancelAndHide();
    private static readonly TimeSpan CapturePollInterval = TimeSpan.FromMilliseconds(400);
    private static readonly ILearningTranslationEngine UnavailableLocalEngine = new UnavailableLocalLearningTranslationEngine();
    private const string SecureFieldMessage = "Sentence translation is unavailable in secure fields.";
    private const string OwnFieldMessage = "Cotabby does not translate text from its own windows.";
    private const string BlockedAppMessage = "Sentence translation is disabled in this app.";
    private const string TerminalFieldMessage = "Sentence translation is unavailable in terminal apps.";
    private const string SelectedTextMessage = "Collapse the text selection and leave the caret in the sentence you want to translate.";
    private const string IncompleteTextMessage = "The editor did not expose the complete field text, so the current sentence cannot be verified.";
    private const string UnreliableSelectionMessage = "The editor did not expose a reliable caret position for this sentence.";
    private const string CompositionMessage = "Finish composing text in a supported editor before translating this sentence.";
    private const string UnsupportedFieldMessage = "Focus a supported text field and try the sentence shortcut again.";
    private const string ChangedFieldMessage = "The sentence changed before its preview could open. Try again.";
    private const string SameLanguageMessage = "Choose different source and target languages in Learning settings.";
    private const string NoSourceLanguageMessage = "Select at least one source language in Learning settings.";
    private const string MultiSourceDetectorUnavailableMessage = "Choose one source language to translate. Multiple-language detection is not available on this Windows build.";
    private const string UncertainSourceLanguageMessage = "The source language is uncertain. Choose one source language or select a clearer sentence.";

    private readonly IKeyboardHook _hook;
    private readonly IFocusTracker _focus;
    private readonly Dispatcher _dispatcher;
    private readonly LearningPreviewCoordinator _coordinator;
    private readonly ILearningSentenceTokenizer _sentenceTokenizer;
    private readonly ILearningLanguageDetector? _languageDetector;
    private readonly ILearningLocalRuntime _localRuntime;
    private readonly Func<bool> _batterySaverOn;
    private readonly HttpClient _learningHttpClient;
    private readonly IOpenAICompatibleCredentialStore _credentials;
    private readonly ILogger<LearningPreviewController> _logger;
    private readonly object _gate = new();

    private LearningPreviewWindow? _window;
    private LearningRuntimeSettings? _settings;
    // Resource intent is independent of card/request state. Style changes retire a request while
    // keeping its allocation; pause/provider/model changes also retire native ownership.
    private LocalRuntimeResidency _desiredLocalResidency = LocalRuntimeResidency.Released;
    private LearningLocalModelConfiguration? _desiredLocalConfiguration;
    private CapturedSentence? _activeCapture;
    private CaptureOrigin? _pendingOrigin;
    private string? _activeRequestId;
    private LearningPreviewState? _pendingLoadingState;
    private bool _loadingStateDispatchQueued;
    private bool _intentActive;
    private bool _disposed;
    private long _intentGeneration;
    private long _stateValidationGeneration;

    public LearningPreviewController(
        IKeyboardHook hook,
        IFocusTracker focus,
        Dispatcher dispatcher,
        ILearningSentenceTokenizer sentenceTokenizer,
        ILearningLocalRuntime localRuntime,
        HttpClient learningHttpClient,
        IOpenAICompatibleCredentialStore credentials,
        ILogger<LearningPreviewController> logger,
        ILearningLanguageDetector? languageDetector = null,
        Func<bool>? batterySaverOn = null,
        ILearningUnitRecognizing? unitRecognizer = null,
        ILearningDictionaryProviding? dictionaryProvider = null,
        ILearningExplanationGenerating? explanationService = null,
        LearningExplanationInputFactory? explanationInputs = null,
        Action? configureExplanation = null,
        IClipboardTextWriter? clipboardWriter = null,
        Vocabulary.IWritingVocabularyStore? vocabulary = null,
        Cotabby.Core.Vocabulary.VocabularyCaptureFactory? vocabularyCaptures = null)
    {
        _hook = hook;
        _vocabulary = vocabulary; _vocabularyCaptures = vocabularyCaptures;
        _textCopy = clipboardWriter is null ? null : new LearningTextCopyController(clipboardWriter);
        _coordinator = new LearningPreviewCoordinator(unitRecognizer: unitRecognizer);
        _dictionarySession = dictionaryProvider is null ? null : new LearningDictionarySession(dictionaryProvider,
            explanationInputs is null ? null : explanationService);
        if (_dictionarySession is not null) _dictionarySession.StateChanged += OnDictionaryStateChanged;
        _explanationInputs = explanationInputs;
        _configureExplanation = configureExplanation;
        _explanationSession = explanationService is null || explanationInputs is null ? null : new LearningExplanationSession(explanationService);
        if (_explanationSession is not null) _explanationSession.StateChanged += OnExplanationStateChanged;
        _focus = focus;
        _dispatcher = dispatcher;
        _sentenceTokenizer = sentenceTokenizer;
        _localRuntime = localRuntime;
        _languageDetector = languageDetector;
        _batterySaverOn = batterySaverOn ?? (() => false);
        _learningHttpClient = learningHttpClient;
        _credentials = credentials;
        _logger = logger;
        _hook.KeyEvent += OnKeyEvent;
        _focus.FocusChanged += OnFocusChanged;
        _coordinator.StateChanged += OnCoordinatorStateChanged;
    }

    /// <summary>
    /// Receives the live app settings on the dispatcher and publishes an immutable snapshot for
    /// the hook thread. Only translation-affecting fields invalidate an active request.
    /// </summary>
    public void ApplySettings(AppSettings settings, int credentialRevision)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => ApplySettings(settings, credentialRevision));
            return;
        }

        var next = LearningRuntimeSettings.From(settings, credentialRevision);
        RetiredIntent? retired = null;
        lock (_gate)
        {
            if (_disposed) return;
            var previous = _settings;
            if (previous is not null && !previous.HasSameTranslationInputs(next))
                retired = RetireIntentLocked();
            Volatile.Write(ref _settings, next);
            // Retirement, provider publication and native resource intent share capture admission.
            // A new hotkey sees all three together; old planner tokens are already cancelled.
            ReconcileLocalResources();
        }
        if (retired is not null)
        {
            _coordinator.ClearCache();
            FinishRetirement(retired);
        }
    }

    /// <summary>
    /// Applies the shared battery-saver pause when Windows reports a power-status change. The
    /// active capture monitor checks the same condition every 400 ms if no status event is raised.
    /// </summary>
    public void PowerStatusChanged()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(PowerStatusChanged);
            return;
        }

        var settings = Volatile.Read(ref _settings);
        if (settings is null) return;
        if (IsBatterySaverPauseActive(settings))
        {
            _coordinator.ClearCache();
            CancelAndHide();
        }
        ReconcileLocalResources();
    }

    /// <summary>Starts the same manual current-sentence command from a tray menu item.</summary>
    public void RequestFromCurrentField()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(RequestFromCurrentField);
            return;
        }

        if (InlineCaptureActive?.Invoke() == true) { CancelAndHide(); return; }

        var settings = Volatile.Read(ref _settings);
        if (settings is null || !settings.TranslationEnabled)
        {
            BeginIntent();
            var anchor = _focus.Current?.CaretRect ?? ScreenRect.Empty;
            EnsureWindow().ShowCaptureFailure(
                "Sentence translation is off. Enable it in Learning settings before requesting a preview.", anchor);
            return;
        }

        var availabilityFailure = SharedAvailabilityFailure(settings);
        if (availabilityFailure is not null)
        {
            ClearCacheIfBatterySaverPaused(settings);
            BeginIntent();
            var anchor = _focus.Current?.CaretRect ?? ScreenRect.Empty;
            EnsureWindow().ShowCaptureFailure(availabilityFailure, anchor);
            return;
        }

        var origin = _focus.Current;
        BeginCaptureIntent(settings, CaptureOrigin.From(origin), origin?.CaretRect ?? ScreenRect.Empty);
    }

    /// <summary>Removes the hook before app shutdown and drains the coordinator's bounded work set.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _stateValidationGeneration);
            _pendingLoadingState = null;
            _loadingStateDispatchQueued = false;
        }

        _hook.KeyEvent -= OnKeyEvent;
        _focus.FocusChanged -= OnFocusChanged;
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
        _textCopy?.Dispose();
        if (_dictionarySession is not null) _dictionarySession.StateChanged -= OnDictionaryStateChanged;
        if (_explanationSession is not null) _explanationSession.StateChanged -= OnExplanationStateChanged;
        CancelCurrentState();

        Task articleShutdown = Task.CompletedTask;
        if (_dispatcher.CheckAccess())
        {
            _window?.Close();
            _window = null;
            var dictionaryWindow = _dictionaryWindow; dictionaryWindow?.Close(); articleShutdown = dictionaryWindow?.ArticleShutdown ?? Task.CompletedTask;
            _dictionaryWindow = null;
        }
        else
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _window?.Close();
                _window = null;
                var dictionaryWindow = _dictionaryWindow; dictionaryWindow?.Close(); articleShutdown = dictionaryWindow?.ArticleShutdown ?? Task.CompletedTask;
                _dictionaryWindow = null;
            });
        }

        // Core waits only for its documented bounded interval. The dedicated endpoint client is
        // owned by AppHost and stays alive until this drain has completed.
        await articleShutdown.ConfigureAwait(false);
        _coordinator.ClearCache();
        await _coordinator.DisposeAsync().ConfigureAwait(false);
        DetachPerformanceHistory();
        if (_dictionarySession is not null) await _dictionarySession.DisposeAsync().ConfigureAwait(false);
        if (_explanationSession is not null) await _explanationSession.DisposeAsync().ConfigureAwait(false);
    }

    private void OnKeyEvent(object? sender, KeyEventArgs args)
    {
        if (InlineCaptureActive?.Invoke() == true) { CancelAndHide(); return; }
        var input = args.Event;
        var settings = Volatile.Read(ref _settings);
        var focused = _focus.Current;
        bool matchesLearningChord = input.IsKeyDown && !input.IsRepeat && !args.Suppress
            && settings is { TranslationEnabled: true, ShortcutConflicts: false }
            && settings.Shortcut.Matches(input);
        var isLearningAvailable = matchesLearningChord && IsLearningAvailable(settings!);
        if (matchesLearningChord && !isLearningAvailable)
            ClearCacheIfBatterySaverPaused(settings!);
        bool isLearningChord = isLearningAvailable
            && focused is not null && FieldEnvironmentFailure(focused, settings!) is null;

        if (isLearningChord)
        {
            args.Suppress = true;
            var origin = focused;
            var anchor = focused!.CaretRect;
            BeginCaptureIntentFromHook(settings!, CaptureOrigin.From(origin), anchor);
            return;
        }

        if (!input.IsKeyDown || !IsIntentActive()) return;
        if (args.Suppress || InvalidatesCapture(input))
        {
            // Other features may already have claimed a key. Either way, its host effect could
            // change text/caret state, so the old sentence is no longer authorized to display.
            CancelAndHide();
        }
    }

    private void BeginCaptureIntentFromHook(LearningRuntimeSettings settings, CaptureOrigin? origin, ScreenRect anchor)
    {
        long generation = BeginIntent(origin);
        _ = _dispatcher.BeginInvoke(() => _ = CaptureAndStartAsync(generation, settings, origin, anchor), DispatcherPriority.Background);
    }

    private void BeginCaptureIntent(LearningRuntimeSettings settings, CaptureOrigin? origin, ScreenRect anchor)
    {
        long generation = BeginIntent(origin);
        _ = CaptureAndStartAsync(generation, settings, origin, anchor);
    }

    /// <summary>
    /// Advances the user intent before UIA work begins. Any earlier capture or translation loses
    /// authority immediately, even while the new capture is still waiting on a host process.
    /// </summary>
    private long BeginIntent(CaptureOrigin? origin = null)
    {
        CapturedSentence? prior;
        long generation;
        lock (_gate)
        {
            if (_disposed) return -1;
            generation = ++_intentGeneration;
            Interlocked.Increment(ref _stateValidationGeneration);
            _pendingLoadingState = null;
            _loadingStateDispatchQueued = false;
            _intentActive = true;
            _pendingOrigin = origin;
            prior = _activeCapture;
            if (prior?.Lifetime is { } lifetime) _ = lifetime.Retire();
            _activeCapture = null;
            _activeRequestId = null;
            RetireDictionaryLocked();
            RetireExplanationLocked();
        }

        CancelMonitor(prior?.Monitor);
        void CancelPreviousOnUi()
        {
            if (!IsPendingIntent(generation)) return;
            _coordinator.Cancel();
            _window?.HidePreview();
            _dictionaryWindow?.HideCard();
        }

        if (_dispatcher.CheckAccess()) CancelPreviousOnUi();
        else _dispatcher.BeginInvoke(CancelPreviousOnUi, DispatcherPriority.Normal);
        return generation;
    }

    private async Task CaptureAndStartAsync(
        long generation, LearningRuntimeSettings settings, CaptureOrigin? origin, ScreenRect fallbackAnchor)
    {
        if (generation < 0 || RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return;

        var attempt = await Task.Run(() => Capture(settings, origin, generation)).ConfigureAwait(false);
        if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return;

        if (attempt.Capture is null)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return;
                EnsureWindow().ShowCaptureFailure(attempt.ErrorMessage ?? UnsupportedFieldMessage,
                    attempt.ErrorAnchor.IsEmpty ? fallbackAnchor : attempt.ErrorAnchor);
            }, DispatcherPriority.Background);
            return;
        }

        var capture = attempt.Capture;
        var availabilityFailure = LearningAvailabilityFailure(capture.Settings);
        if (availabilityFailure is not null)
        {
            ClearCacheIfBatterySaverPaused(capture.Settings);
            await _dispatcher.InvokeAsync(() =>
            {
                if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return;
                CancelAndHide();
                EnsureWindow().ShowCaptureFailure(availabilityFailure, capture.Field.CaretRect);
            }, DispatcherPriority.Background);
            return;
        }

        if (capture.TranslationOptions.SourceLanguage == capture.TranslationOptions.TargetLanguage)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (!RetireForInlineCapture(generation) && IsCurrentIntent(generation))
                    EnsureWindow().ShowCaptureFailure(SameLanguageMessage, capture.Field.CaretRect);
            }, DispatcherPriority.Background);
            return;
        }

        // A second off-thread UIA read proves the exact capture is still the current editor state
        // before the translation coordinator can hand any sentence to an engine.
        bool isCurrent = await Task.Run(() => !RetireForInlineCapture(generation) && IsCaptureCurrent(capture)).ConfigureAwait(false);
        if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return;
        if (!isCurrent)
        {
            var currentAvailabilityFailure = LearningAvailabilityFailure(capture.Settings);
            ClearCacheIfBatterySaverPaused(capture.Settings);
            await _dispatcher.InvokeAsync(() =>
            {
                if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return;
                CancelAndHide();
                EnsureWindow().ShowCaptureFailure(
                    currentAvailabilityFailure ?? ChangedFieldMessage,
                    currentAvailabilityFailure is null ? fallbackAnchor : capture.Field.CaretRect);
            }, DispatcherPriority.Background);
            return;
        }

        await _dispatcher.InvokeAsync(() => { if (!RetireForInlineCapture(generation)) BeginPreview(generation, capture); }, DispatcherPriority.Background);
    }

    private CaptureAttempt Capture(LearningRuntimeSettings settings, CaptureOrigin? origin, long generation)
    {
        if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return new CaptureAttempt(null, null, ScreenRect.Empty);
        var availabilityFailure = LearningAvailabilityFailure(settings);
        if (availabilityFailure is not null)
        {
            ClearCacheIfBatterySaverPaused(settings);
            return new CaptureAttempt(null, availabilityFailure, origin?.Field.CaretRect ?? ScreenRect.Empty);
        }

        if (origin is null)
            return new CaptureAttempt(null, UnsupportedFieldMessage, ScreenRect.Empty);

        // These gates depend only on stable focus metadata and are checked before a fresh UIA read,
        // so a manual chord never asks a blocked or credential field for another text snapshot.
        var preflightFailure = FieldEnvironmentFailure(origin.Field, settings);
        if (preflightFailure is not null)
            return new CaptureAttempt(null, preflightFailure, origin.Field.CaretRect);
        if (!CompositionEvidencePolicy.IsClear(origin.Field))
            return new CaptureAttempt(null, CompositionMessage, origin.Field.CaretRect);

        FocusedField? field;
        try
        {
            // UIA is a cross-process call that can stall on a host. This method is only reached
            // from Task.Run, never from the low-level hook or the WPF dispatcher.
            if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return new CaptureAttempt(null, null, ScreenRect.Empty);
            field = _focus.Refresh();
        }
        catch (Exception)
        {
            return new CaptureAttempt(null, UnsupportedFieldMessage, ScreenRect.Empty);
        }

        if (RetireForInlineCapture(generation) || !IsCurrentIntent(generation)) return new CaptureAttempt(null, null, ScreenRect.Empty);
        if (field is null) return new CaptureAttempt(null, UnsupportedFieldMessage, ScreenRect.Empty);
        if (!origin.Matches(field))
            return new CaptureAttempt(null, ChangedFieldMessage, field.CaretRect);
        string? rejection = FieldCaptureFailure(field, settings);
        if (rejection is not null) return new CaptureAttempt(null, rejection, field.CaretRect);

        try
        {
            var collapsedCaret = new LearningTextRange(field.CaretOffset, 0);
            var range = LearningTextScopePolicy.ConversionRange(
                field.Text, collapsedCaret, _sentenceTokenizer, settings.SentenceSegmentationLanguage);
            var source = LearningTextScopePolicy.Text(field.Text, range);

            // The sentence boundary locale and the translation direction are separate decisions.
            // ICU needs a locale before the sentence is known; multi-source direction selection
            // must still use every enabled candidate and never fall back to that locale.
            var candidates = settings.SourceLanguages
                .Where(language => language != settings.Options.TargetLanguage)
                .ToArray();
            if (candidates.Length == 0)
            {
                var message = settings.SourceLanguages.Count == 0
                    ? NoSourceLanguageMessage
                    : SameLanguageMessage;
                return new CaptureAttempt(null, message, field.CaretRect);
            }

            LearningLanguage? sourceLanguage;
            if (candidates.Length == 1)
            {
                // A single user choice is explicit in the Mac contract and needs no detector.
                sourceLanguage = candidates[0];
            }
            else if (_languageDetector is null)
            {
                return new CaptureAttempt(null, MultiSourceDetectorUnavailableMessage, field.CaretRect);
            }
            else
            {
                sourceLanguage = LearningSourceLanguageResolver.Resolve(
                    source, settings.SourceLanguages, settings.Options.TargetLanguage, _languageDetector);
            }

            if (sourceLanguage is null)
                return new CaptureAttempt(null, UncertainSourceLanguageMessage, field.CaretRect);

            return new CaptureAttempt(
                new CapturedSentence(field, range, source, settings,
                    settings.Options with { SourceLanguage = sourceLanguage.Value }), null, field.CaretRect);
        }
        catch (LearningTranslationException error)
        {
            return new CaptureAttempt(null, error.Message, field.CaretRect);
        }
        catch (Exception)
        {
            return new CaptureAttempt(null, UnreliableSelectionMessage, field.CaretRect);
        }
    }

    private static string? FieldCaptureFailure(FocusedField field, LearningRuntimeSettings settings)
    {
        var environmentFailure = FieldEnvironmentFailure(field, settings);
        if (environmentFailure is not null) return environmentFailure;
        if (field.IsSecure) return SecureFieldMessage;
        if (!CompositionEvidencePolicy.IsClear(field))
            return CompositionMessage;
        if (field.HasSelection) return SelectedTextMessage;
        if (!field.HasReliableSelection || field.CaretOffset < 0 || field.CaretOffset > field.Text.Length)
            return UnreliableSelectionMessage;
        if (field.IsTextWindowTruncated) return IncompleteTextMessage;
        if (field.ProcessId <= 0 || string.IsNullOrWhiteSpace(field.FieldId) || field.FocusChangeSequence <= 0)
            return UnsupportedFieldMessage;
        return null;
    }

    private static string? FieldEnvironmentFailure(FocusedField field, LearningRuntimeSettings settings)
    {
        if (field.ProcessId == Environment.ProcessId) return OwnFieldMessage;
        if (settings.BlockedApps.Contains(field.ProcessName)) return BlockedAppMessage;
        // Learning follows the Mac availability call's explicit false terminal option, regardless
        // of autocomplete's integrated-terminal preference.
        if (TerminalAppDetector.IsTerminal(field.ProcessName)
            || TerminalAppDetector.IsIntegratedTerminal(field.ClassName)) return TerminalFieldMessage;
        if (CredentialFieldDetector.IsCredentialField(field)) return CredentialFieldDetector.BlockedReason;
        return null;
    }

    private string? LearningAvailabilityFailure(LearningRuntimeSettings settings)
    {
        if (!settings.TranslationEnabled)
            return "Sentence translation is off. Enable it in Learning settings before requesting a preview.";
        return SharedAvailabilityFailure(settings);
    }

    private bool IsLearningAvailable(LearningRuntimeSettings settings) =>
        InlineCaptureActive?.Invoke() != true && LearningAvailabilityFailure(settings) is null;

    private string? SharedAvailabilityFailure(LearningRuntimeSettings settings)
    {
        bool batterySaverOn = settings.AvailabilitySettings.PauseOnBatterySaver && _batterySaverOn();
        return SuggestionAvailability.DisabledReason(settings.AvailabilitySettings, DateTimeOffset.Now, batterySaverOn);
    }

    private bool IsBatterySaverPauseActive(LearningRuntimeSettings settings) =>
        settings.AvailabilitySettings.PauseOnBatterySaver && _batterySaverOn();

    private void ClearCacheIfBatterySaverPaused(LearningRuntimeSettings settings)
    {
        if (IsBatterySaverPauseActive(settings))
        {
            _coordinator.ClearCache();
            // The capture monitor is also a fallback if Windows omits a power-status event.
            // Resource reconciliation reads current settings, never this possibly old capture.
            ReconcileLocalResources();
        }
    }

    /// <summary>
    /// Adapts the Mac learning collection's release decision without prewarming weights. The
    /// runtime records retirement synchronously, then drains on its worker. Recording that intent
    /// under the same gate as new captures prevents a delayed UI action from retiring a newer task.
    /// </summary>
    private void ReconcileLocalResources()
    {
        Task? release = null;
        lock (_gate)
        {
            if (_disposed || _settings is not { } settings) return;
            bool paused = settings.AvailabilitySettings.Pause?.IsActive(DateTimeOffset.Now) == true
                || IsBatterySaverPauseActive(settings);
            var next = LocalRuntimeResidencyPolicy.Residency(
                usesLocalModel: settings.Backend == LearningTranslationBackend.Local,
                enabled: settings.TranslationEnabled && settings.AvailabilitySettings.Enabled,
                paused: paused,
                keepWarm: true);
            bool allocationChanged = _desiredLocalResidency == LocalRuntimeResidency.Warm
                && next == LocalRuntimeResidency.Warm
                && !SameLocalAllocation(_desiredLocalConfiguration, settings.LocalConfiguration);
            bool shouldRelease = allocationChanged
                || (next == LocalRuntimeResidency.Released && _desiredLocalResidency != next);
            _desiredLocalResidency = next;
            _desiredLocalConfiguration = next == LocalRuntimeResidency.Warm ? settings.LocalConfiguration : null;
            if (shouldRelease) release = _localRuntime.UnloadAsync();
        }
        if (release is not null) _ = ObserveLocalReleaseAsync(release);
    }

    private static bool SameLocalAllocation(
        LearningLocalModelConfiguration? first, LearningLocalModelConfiguration? second) =>
        first is null || second is null ? first is null && second is null :
            string.Equals(first.ModelPath, second.ModelPath, StringComparison.OrdinalIgnoreCase)
            && first.ContextWindowTokens == second.ContextWindowTokens
            && first.GpuLayerCount == second.GpuLayerCount
            && (first.RequestedTemplate == LearningChatTemplate.Automatic)
                == (second.RequestedTemplate == LearningChatTemplate.Automatic);

    private async Task ObserveLocalReleaseAsync(Task release)
    {
        try { await release.ConfigureAwait(false); }
        catch (Exception)
        {
            // A native exception may include paths. Resource cleanup has no user-text payload and
            // must not turn into an unobserved task fault or a raw diagnostic in the preview card.
            _logger.LogDebug("Learning local resource release ended with a cleanup failure.");
        }
    }

    // Cached tracker reads close a dispatcher gap without blocking it on cross-process UIA.
    // Dictionary and explanation children share the parent card's captured-field authority.
    private bool CachedCaptureCurrent(CapturedSentence capture) =>
        _settings is { } settings && settings.HasSameTranslationInputs(capture.Settings) && IsLearningAvailable(settings)
        && _focus.Current is { } field && SameCapturedField(capture.Field, field)
        && FieldCaptureFailure(field, capture.Settings) is null;

    // All learning children revalidate off-thread and retire only the same captured intent.
    private async Task<bool> ValidateLearningCaptureAsync(CapturedSentence capture, long intent)
    {
        if (await Task.Run(() => IsCaptureCurrent(capture)).ConfigureAwait(false)) return true;
        await _dispatcher.InvokeAsync(() =>
        {
            lock (_gate)
            {
                // A failed old validation must not close a newer capture, including a retry
                // sharing its intent. Retire the stale current card without waiting for polling.
                if (!_disposed && _intentActive && _intentGeneration == intent
                    && ReferenceEquals(_activeCapture, capture)) CancelAndHide();
            }
        }, DispatcherPriority.Background);
        return false;
    }

    private bool IsCaptureCurrent(CapturedSentence capture)
    {
        bool CurrentSettings()
        {
            lock (_gate)
            {
                return !_disposed && _settings is { } settings
                    && settings.HasSameTranslationInputs(capture.Settings)
                    && IsLearningAvailable(settings)
                    && (capture.Lifetime is null || ReferenceEquals(_activeCapture?.Lifetime, capture.Lifetime));
            }
        }
        if (!CurrentSettings()) return false;
        try
        {
            var current = _focus.Refresh();
            return current is not null
                && SameCapturedField(capture.Field, current)
                && FieldCaptureFailure(current, capture.Settings) is null
                && CurrentSettings();
        }
        catch (Exception) { return false; }
    }

    private static bool SameCapturedField(FocusedField captured, FocusedField current) =>
        current.ProcessId == captured.ProcessId
        && string.Equals(current.FieldId, captured.FieldId, StringComparison.Ordinal)
        && current.FocusChangeSequence == captured.FocusChangeSequence
        && CompositionEvidencePolicy.IsSameReceipt(captured, current)
        && string.Equals(current.Text, captured.Text, StringComparison.Ordinal)
        && current.CaretOffset == captured.CaretOffset
        && !current.HasSelection
        && current.HasReliableSelection
        && !current.IsTextWindowTruncated
        && !current.IsSecure;

    private void BeginPreview(long generation, CapturedSentence capture)
    {
        _dispatcher.VerifyAccess();
        if (!IsCurrentIntent(generation)) return;
        var currentSettings = Volatile.Read(ref _settings);
        var latest = _focus.Current;
        if (currentSettings is null || !IsLearningAvailable(currentSettings))
        {
            if (currentSettings is not null) ClearCacheIfBatterySaverPaused(currentSettings);
            CancelAndHide();
            return;
        }

        if (!currentSettings.HasSameTranslationInputs(capture.Settings)
            || latest is null
            || !SameCapturedField(capture.Field, latest)
            || FieldCaptureFailure(latest, capture.Settings) is not null)
        {
            CancelAndHide();
            return;
        }

        var monitor = new CancellationTokenSource();
        capture = capture with { Monitor = monitor, Lifetime = new LearningRequestLifetime() };
        ILearningTranslationEngine engine = new CurrentCaptureLearningEngine(
            this, capture, CreateEngine(capture.Settings));
        CapturedSentence? previousCapture;
        Task request;
        LearningPreviewState state;
        lock (_gate)
        {
            if (_disposed || _intentGeneration != generation)
            {
                monitor.Dispose();
                _ = DisposeEngineAsync(engine);
                return;
            }

            previousCapture = _activeCapture;
            if (previousCapture?.Lifetime is { } previousLifetime) _ = previousLifetime.Retire();
            _pendingOrigin = null;
            _activeCapture = capture;
            // Begin the Core request while holding the intent gate. A concurrent key/focus
            // invalidation then either wins before this point or cancels the request after it has
            // an identity; it cannot slip between a freshness check and request creation.
            request = _coordinator.PreviewAsync(
                capture.Source, capture.TranslationOptions, engine, capture.Settings.CacheScope);
            state = _coordinator.State;
            _activeRequestId = state.RequestId;
        }
        CancelMonitor(previousCapture?.Monitor);

        _ = ObserveRequestAsync(request, engine);
        _ = MonitorCaptureAsync(generation, capture with { Monitor = monitor }, monitor.Token);
    }

    private ILearningTranslationEngine CreateEngine(LearningRuntimeSettings settings)
    {
        if (settings.Backend == LearningTranslationBackend.Local)
        {
            // The app-lifetime runtime is independent of autocomplete and remains lazy. This
            // request facade receives only the captured local profile and owns its operation lease.
            return settings.LocalConfiguration is { } localConfiguration
                ? new LearningLocalTranslationEngine(localConfiguration, _localRuntime)
                : UnavailableLocalEngine;
        }

        // Endpoint selection and task-specific consent are both required. Settings load and local
        // model selection never construct an endpoint engine or trigger a connection.
        if (!settings.EndpointEnabled || settings.Options.ModelName.Length == 0)
            return UnavailableLearningTranslationEngine.Instance;

        try
        {
            var endpoint = new OpenAICompatibleEndpointConfiguration(
                settings.EndpointBaseUrl,
                settings.Options.ModelName,
                OpenAICompatibleApiMode.ChatCompletions);
            return new OpenAICompatibleLearningTranslationEngine(endpoint, _credentials, _learningHttpClient);
        }
        catch (OpenAICompatibleEndpointException)
        {
            return UnavailableLearningTranslationEngine.Instance;
        }
    }

    private async Task ObserveRequestAsync(Task request, ILearningTranslationEngine engine)
    {
        try
        {
            await request.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The coordinator already converts provider failures to fixed UI messages. This
            // catch only observes the task so app shutdown does not leave a background fault.
            _logger.LogDebug("Learning preview task ended after its coordinator snapshot.");
        }
        finally
        {
            await DisposeEngineAsync(engine).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A local planner may hold an exclusive native operation lease even when the coordinator
    /// serves a completed cache entry or rejects planning. Always drain that lease asynchronously;
    /// synchronous disposal on the dispatcher can block native decode or model shutdown.
    /// </summary>
    private static async Task DisposeEngineAsync(ILearningTranslationEngine engine)
    {
        try
        {
            if (engine is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (engine is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception)
        {
            // Disposal is cleanup after a request has already reached its terminal state. Do not
            // allow a provider cleanup failure to fault the WPF dispatcher or shutdown task.
        }
    }

    private async Task MonitorCaptureAsync(long generation, CapturedSentence capture, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(CapturePollInterval, cancellationToken).ConfigureAwait(false);
                if (!IsLearningAvailable(capture.Settings))
                {
                    ClearCacheIfBatterySaverPaused(capture.Settings);
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        if (IsCurrentIntent(generation)) CancelAndHide();
                    });
                    return;
                }

                bool current = await Task.Run(() => IsCaptureCurrent(capture), cancellationToken).ConfigureAwait(false);
                if (!current)
                {
                    ClearCacheIfBatterySaverPaused(capture.Settings);
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        if (IsCurrentIntent(generation)) CancelAndHide();
                    });
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Hiding, editing or changing the source owns the cancellation; there is no UI error.
        }
        finally
        {
            capture.Monitor?.Dispose();
        }
    }

    private void OnCoordinatorStateChanged(LearningPreviewState state)
    {
        // The Core event is deliberately not UI-thread-bound. Loading snapshots are coalesced into
        // one dispatcher callback and checked against the tracker's cached field; a token stream
        // therefore never creates one cross-process UIA validation per prefix.
        if (state.Status == LearningPreviewStatus.Loading)
        {
            bool shouldPost;
            lock (_gate)
            {
                if (_disposed) return;
                _pendingLoadingState = state;
                shouldPost = !_loadingStateDispatchQueued;
                _loadingStateDispatchQueued = true;
            }

            if (shouldPost)
                _ = _dispatcher.BeginInvoke(DrainPendingLoadingState, DispatcherPriority.Background);
            return;
        }

        lock (_gate)
        {
            if (_disposed) return;
            if (_pendingLoadingState?.RequestId == state.RequestId)
                _pendingLoadingState = null;
        }

        if (state.Status == LearningPreviewStatus.Cancelled)
        {
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (IsCurrentRequest(state.RequestId)) CancelAndHide();
            }, DispatcherPriority.Background);
            return;
        }

        if (state.Status is not (LearningPreviewStatus.Ready or LearningPreviewStatus.Failed)) return;
        var validation = Interlocked.Increment(ref _stateValidationGeneration);
        _ = _dispatcher.BeginInvoke(
            () => ValidateAndRenderTerminalState(state, validation), DispatcherPriority.Background);
    }

    private void DrainPendingLoadingState()
    {
        _dispatcher.VerifyAccess();
        LearningPreviewState? state;
        lock (_gate)
        {
            state = _pendingLoadingState;
            _pendingLoadingState = null;
            _loadingStateDispatchQueued = false;
        }

        if (state is null || _coordinator.State != state) return;

        CapturedSentence? capture;
        lock (_gate)
        {
            if (_disposed || !_intentActive || _activeCapture is null || _activeRequestId != state.RequestId) return;
            capture = _activeCapture;
        }

        if (!MatchesCapture(state, capture))
        {
            CancelAndHide();
            return;
        }

        var currentSettings = Volatile.Read(ref _settings);
        var latest = _focus.Current;
        if (currentSettings is null || !IsLearningAvailable(currentSettings))
        {
            if (currentSettings is not null) ClearCacheIfBatterySaverPaused(currentSettings);
            CancelAndHide();
            return;
        }

        if (!currentSettings.HasSameTranslationInputs(capture.Settings)
            || latest is null
            || !SameCapturedField(capture.Field, latest)
            || FieldCaptureFailure(latest, capture.Settings) is not null)
        {
            CancelAndHide();
            return;
        }

        // PartialResult is intentionally provisional and cannot authorize a write. The read-only
        // card may display it while the 400 ms off-thread capture monitor continues full UIA reads.
        EnsureWindow().ShowState(state, capture.Field.CaretRect, canRetry: false);
    }

    private void ValidateAndRenderTerminalState(LearningPreviewState state, long validation)
    {
        CapturedSentence? capture;
        long intent;
        lock (_gate)
        {
            if (_disposed || !_intentActive || _activeCapture is null || _activeRequestId != state.RequestId
                || validation != Interlocked.Read(ref _stateValidationGeneration)) return;
            capture = _activeCapture;
            intent = _intentGeneration;
        }

        if (!MatchesCapture(state, capture))
        {
            CancelAndHide();
            return;
        }

        _ = ValidateThenRenderTerminalAsync(state, capture, intent, validation);
    }

    private async Task ValidateThenRenderTerminalAsync(
        LearningPreviewState state, CapturedSentence capture, long intent, long validation)
    {
        bool current = await Task.Run(() => IsCaptureCurrent(capture)).ConfigureAwait(false);
        if (!current)
        {
            ClearCacheIfBatterySaverPaused(capture.Settings);
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (IsCurrentIntent(intent)) CancelAndHide();
            }, DispatcherPriority.Background);
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            if (validation != Interlocked.Read(ref _stateValidationGeneration)
                || !IsCurrentState(intent, state.RequestId)
                || _coordinator.State != state) return;
            if (!MatchesCapture(state, capture))
            {
                CancelAndHide();
                return;
            }
            // A key event normally revokes the intent first. This cached snapshot check also
            // closes the dispatcher gap when UIA published a focus/selection change meanwhile; it
            // performs no cross-process read on the WPF thread.
            var latest = _focus.Current;
            if (latest is null
                || !SameCapturedField(capture.Field, latest)
                || FieldCaptureFailure(latest, capture.Settings) is not null)
            {
                CancelAndHide();
                return;
            }

            var currentSettings = Volatile.Read(ref _settings);
            if (currentSettings is null || !IsLearningAvailable(currentSettings)
                || !currentSettings.HasSameTranslationInputs(capture.Settings))
            {
                if (currentSettings is not null) ClearCacheIfBatterySaverPaused(currentSettings);
                CancelAndHide();
                return;
            }

            EnsureWindow().ShowState(state, capture.Field.CaretRect,
                canRetry: state.Status == LearningPreviewStatus.Failed);
            PrepareSentenceExplanation(state, capture, intent);
            PrepareSentenceVocabulary(state, capture, intent);
        }, DispatcherPriority.Background);
    }

    private void RetryCurrentCapture()
    {
        _dispatcher.VerifyAccess();
        CapturedSentence? capture;
        long intent;
        LearningRuntimeSettings? settings;
        lock (_gate)
        {
            capture = _activeCapture;
            intent = _intentGeneration;
            settings = _settings;
        }

        if (capture is null || settings is null)
        {
            CancelAndHide();
            return;
        }

        var availabilityFailure = LearningAvailabilityFailure(settings);
        if (availabilityFailure is not null || !settings.HasSameTranslationInputs(capture.Settings))
        {
            ClearCacheIfBatterySaverPaused(settings);
            CancelAndHide();
            return;
        }

        _ = RetryAfterRevalidationAsync(capture, intent, settings);
    }

    private async Task RetryAfterRevalidationAsync(
        CapturedSentence capture, long intent, LearningRuntimeSettings settings)
    {
        bool current = await Task.Run(() => IsCaptureCurrent(capture)).ConfigureAwait(false);
        if (!IsCurrentIntent(intent)) return;
        await _dispatcher.InvokeAsync(() =>
        {
            if (!IsCurrentIntent(intent)) return;
            if (!current)
            {
                ClearCacheIfBatterySaverPaused(capture.Settings);
                CancelAndHide();
                return;
            }

            BeginPreview(intent, capture with { Settings = settings });
        }, DispatcherPriority.Background);
    }

    private void OnFocusChanged(object? sender, FocusedField? field)
    {
        CapturedSentence? capture;
        CaptureOrigin? origin;
        lock (_gate)
        {
            capture = _activeCapture;
            origin = _pendingOrigin;
        }

        if ((capture is not null && (field is null
                || !SameCapturedField(capture.Field, field)
                || FieldCaptureFailure(field, capture.Settings) is not null))
            || (capture is null && origin is not null && (field is null || !origin.Matches(field))))
        {
            CancelAndHide();
        }
    }

    private void CancelAndHide()
    {
        RetiredIntent retired;
        lock (_gate) retired = RetireIntentLocked();
        FinishRetirement(retired);
    }

    // Caller holds _gate. State retirement and the direct provider token change together, so
    // neither a new hotkey nor an old planner can enter the settings-publication gap.
    private RetiredIntent RetireIntentLocked()
    {
        var generation = ++_intentGeneration;
        Interlocked.Increment(ref _stateValidationGeneration);
        _pendingLoadingState = null;
        _loadingStateDispatchQueued = false;
        _intentActive = false;
        _pendingOrigin = null;
        var capture = _activeCapture;
        if (capture?.Lifetime is { } lifetime) _ = lifetime.Retire();
        _activeCapture = null;
        _activeRequestId = null;
        RetireDictionaryLocked();
        RetireExplanationLocked();
        return new RetiredIntent(generation, capture);
    }

    private void FinishRetirement(RetiredIntent retired)
    {
        CancelMonitor(retired.Capture?.Monitor);
        void CancelAndHideOnUi()
        {
            if (!IsRetiredIntent(retired.Generation)) return;
            _coordinator.Cancel();
            _window?.HidePreview();
            _dictionaryWindow?.HideCard();
        }
        if (_dispatcher.CheckAccess()) CancelAndHideOnUi();
        else _ = _dispatcher.BeginInvoke(CancelAndHideOnUi, DispatcherPriority.Normal);
    }

    private sealed record RetiredIntent(long Generation, CapturedSentence? Capture);

    private void CancelCurrentState() => CancelAndHide();

    private bool IsIntentActive()
    {
        lock (_gate) return _intentActive;
    }

    private bool IsCurrentIntent(long generation)
    {
        lock (_gate) return !_disposed && _intentActive && _intentGeneration == generation;
    }

    // An older queued worker must never retire a newer preview. Check the generation under the
    // intent gate before withdrawing, then dispatch presentation cleanup outside that gate.
    private bool RetireForInlineCapture(long generation)
    {
        if (InlineCaptureActive?.Invoke() != true) return false;
        RetiredIntent? retired = null;
        lock (_gate)
            if (!_disposed && _intentActive && _intentGeneration == generation) retired = RetireIntentLocked();
        if (retired is not null) FinishRetirement(retired);
        return true;
    }

    private bool IsPendingIntent(long generation)
    {
        lock (_gate)
            return !_disposed && _intentActive && _intentGeneration == generation
                && _activeCapture is null && _activeRequestId is null;
    }

    private bool IsRetiredIntent(long generation)
    {
        lock (_gate)
            return !_disposed && !_intentActive && _intentGeneration == generation
                && _activeCapture is null && _activeRequestId is null;
    }

    private bool IsCurrentState(long generation, string requestId)
    {
        lock (_gate)
            return !_disposed && _intentActive && _intentGeneration == generation
                && _activeRequestId == requestId && _activeCapture is not null;
    }

    private bool IsCurrentRequest(string requestId)
    {
        lock (_gate)
            return !_disposed && _intentActive && _activeRequestId == requestId && _activeCapture is not null;
    }

    private static bool MatchesCapture(LearningPreviewState state, CapturedSentence capture) =>
        string.Equals(state.Source, capture.Source, StringComparison.Ordinal)
        && state.Options == capture.TranslationOptions;

    private LearningPreviewWindow EnsureWindow()
    {
        _dispatcher.VerifyAccess();
        if (_window is not null) return _window;

        _window = new LearningPreviewWindow();
        if (_speechController is not null) _window.AttachSpeech(_speechController);
        _window.RetryRequested += RetryCurrentCapture;
        _window.CancelRequested += CancelAndHide;
        _window.DismissRequested += CancelAndHide;
        _window.LookupRequested += OnDictionaryLookupRequested;
        _window.ExplanationRequested += OnExplanationRequested;
        _window.CopyRequested += OnCopyRequested;
        _window.SelectedCopyRequested += OnPreviewSelectedCopy;
        _window.Closed += (_, _) =>
        {
            _window = null;
            CancelAndHide();
        };
        return _window;
    }

    private static bool InvalidatesCapture(KeyboardEvent input) => input.Kind is
        KeyKind.Character or KeyKind.Backspace or KeyKind.Delete or KeyKind.Arrow
        or KeyKind.Tab or KeyKind.Enter or KeyKind.Shortcut or KeyKind.Escape;

    private static void CancelMonitor(CancellationTokenSource? monitor)
    {
        if (monitor is null) return;
        try { monitor.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private sealed record CaptureAttempt(CapturedSentence? Capture, string? ErrorMessage, ScreenRect ErrorAnchor);

    /// <summary>Local selection errors stay local and never disguise themselves as endpoint setup.</summary>
    private sealed class UnavailableLocalLearningTranslationEngine : ILearningTranslationEngine
    {
        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromException<string>(new LearningTranslationException(
                LearningTranslationFailure.LocalModelNotConfigured));
        }
    }

    /// <summary>Field identity sampled from the tracker before off-thread UIA reads begin.</summary>
    private sealed record CaptureOrigin(FocusedField Field)
    {
        public bool Matches(FocusedField current) => current.ProcessId == Field.ProcessId
            && string.Equals(current.FieldId, Field.FieldId, StringComparison.Ordinal)
            && current.FocusChangeSequence == Field.FocusChangeSequence
            && CompositionEvidencePolicy.IsSameReceipt(Field, current);

        public static CaptureOrigin? From(FocusedField? field) =>
            field is { ProcessId: > 0, FieldId.Length: > 0, FocusChangeSequence: > 0 }
                ? new CaptureOrigin(field)
                : null;
    }

    /// <summary>
    /// The original field snapshot and exact sentence range stay beside, but never enter, the
    /// inference request. A monitor lifetime cancels preview work when text or caret state changes.
    /// </summary>
    private sealed record CapturedSentence(
        FocusedField Field,
        LearningTextRange Range,
        string Source,
        LearningRuntimeSettings Settings,
        LearningTranslationOptions TranslationOptions,
        CancellationTokenSource? Monitor = null,
        LearningRequestLifetime? Lifetime = null);

    /// <summary>
    /// Keeps the coordinator's cache boundary behind a final exact-field check. Mac validates the
    /// captured editor again before it promotes a complete stream into its translation cache; this
    /// wrapper performs that UIA read on the worker after the endpoint returns, never on WPF.
    /// </summary>
    private sealed class CurrentCaptureLearningEngine(
        LearningPreviewController owner,
        CapturedSentence capture,
        ILearningTranslationEngine inner) : ILearningTranslationEngine, ILearningTranslationRequestPlanner, IAsyncDisposable
    {
        private int _disposed;

        /// <summary>
        /// Preserve the inner engine's task-specific request planner. Local planners need to read
        /// the selected GGUF metadata and tokenize against their own runtime; endpoints use the
        /// shared deterministic factory. Freshness is checked on this worker both before and after
        /// the asynchronous plan so a slow model load cannot authorize a stale field capture.
        /// </summary>
        public async Task<LearningTranslationRequest> PlanAsync(
            string requestId, string source, LearningTranslationOptions options, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ct.ThrowIfCancellationRequested();
            var lifetime = capture.Lifetime ?? throw new OperationCanceledException();
            using var cancellation = ct.Register(() => _ = lifetime.Retire());
            var token = lifetime.Token;
            EnsureCaptureCurrent(token);

            var request = inner is ILearningTranslationRequestPlanner planner
                ? await planner.PlanAsync(requestId, source, options, token).ConfigureAwait(false)
                : LearningTranslationRequestFactory.Create(requestId, source, options);

            ct.ThrowIfCancellationRequested();
            EnsureCaptureCurrent(token);
            return request;
        }

        public Task<string> TranslateAsync(LearningTranslationRequest request, CancellationToken ct) =>
            TranslateAsync(request, null, ct);

        public async Task<string> TranslateAsync(
            LearningTranslationRequest request, Action<string>? onDelta, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ct.ThrowIfCancellationRequested();
            var lifetime = capture.Lifetime ?? throw new OperationCanceledException();
            using var cancellation = ct.Register(() => _ = lifetime.Retire());
            var token = lifetime.Token;
            EnsureCaptureCurrent(token);
            Action<string>? guardedDelta = onDelta is null ? null : delta =>
            {
                ct.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                onDelta(delta);
            };
            var result = await inner.TranslateAsync(request, guardedDelta, token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            EnsureCaptureCurrent(token);
            return result;
        }

        private void EnsureCaptureCurrent(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (owner.IsCaptureCurrent(capture)) return;
            owner.ClearCacheIfBatterySaverPaused(capture.Settings);
            throw new OperationCanceledException("The captured source changed during translation.", ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (inner is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (inner is IDisposable disposable)
                    disposable.Dispose();
            }
            finally
            {
                if (capture.Lifetime is { } lifetime)
                    await lifetime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>One immutable projection of only the settings that belong to translation.</summary>
    private sealed record LearningRuntimeSettings(
        LearningTranslationBackend Backend,
        bool TranslationEnabled,
        SuggestionSettings AvailabilitySettings,
        IReadOnlySet<string> BlockedApps,
        bool EndpointEnabled,
        KeyBinding Shortcut,
        bool ShortcutConflicts,
        string EndpointBaseUrl,
        IReadOnlyList<LearningLanguage> SourceLanguages,
        LearningTranslationOptions Options,
        LearningLocalModelConfiguration? LocalConfiguration,
        int CredentialRevision,
        string? CacheScope)
    {
        /// <summary>
        /// ICU requires a language at sentence-tokenization time, before constrained source
        /// resolution has the captured sentence. For a multi-source profile retain the legacy
        /// locale as a platform segmentation hint only; it never narrows the translation choices.
        /// </summary>
        public LearningLanguage SentenceSegmentationLanguage
        {
            get
            {
                var candidates = SourceLanguages
                    .Where(language => language != Options.TargetLanguage)
                    .Distinct()
                    .Take(2)
                    .ToArray();
                return candidates.Length == 1 ? candidates[0] : Options.SourceLanguage
                    ?? throw new InvalidOperationException("Writing preview requires a sentence segmentation language.");
            }
        }

        public bool HasSameTranslationInputs(LearningRuntimeSettings other) =>
            TranslationEnabled == other.TranslationEnabled
            && AvailabilitySettings.Enabled == other.AvailabilitySettings.Enabled
            && AvailabilitySettings.Pause == other.AvailabilitySettings.Pause
            && AvailabilitySettings.PauseOnBatterySaver == other.AvailabilitySettings.PauseOnBatterySaver
            && BlockedApps.SetEquals(other.BlockedApps)
            && Backend == other.Backend
            && (Backend != LearningTranslationBackend.Endpoint
                || (EndpointEnabled == other.EndpointEnabled
                    && string.Equals(EndpointBaseUrl, other.EndpointBaseUrl, StringComparison.Ordinal)))
            && (Backend != LearningTranslationBackend.Local || LocalConfiguration == other.LocalConfiguration)
            && SourceLanguages.SequenceEqual(other.SourceLanguages)
            && Options == other.Options
            && (Backend != LearningTranslationBackend.Endpoint || CredentialRevision == other.CredentialRevision)
            && string.Equals(CacheScope, other.CacheScope, StringComparison.Ordinal);

        public static LearningRuntimeSettings From(AppSettings settings, int credentialRevision)
        {
            var legacySource = Enum.IsDefined(settings.LearningSourceLanguage)
                ? settings.LearningSourceLanguage
                : LearningLanguage.Chinese;
            var sourceLanguages = Array.AsReadOnly(settings.EffectiveLearningSourceLanguages.ToArray());
            var target = Enum.IsDefined(settings.LearningTargetLanguage)
                ? settings.LearningTargetLanguage
                : LearningLanguage.English;
            // The prior singular direction remains the tokenizer locale when several source
            // choices are enabled. Translation later resolves against the full immutable list.
            var segmentationCandidates = sourceLanguages
                .Where(language => language != target)
                .Distinct()
                .Take(2)
                .ToArray();
            var sentenceLanguage = segmentationCandidates.Length == 1
                ? segmentationCandidates[0]
                : legacySource;
            var selection = settings.LearningTranslationActiveSelection;
            var backend = selection.Backend == LearningModelBackend.Endpoint ? LearningTranslationBackend.Endpoint : LearningTranslationBackend.Local;
            var endpointModel = selection.EndpointModel.Trim();
            var localModel = selection.LocalModel.Trim();
            var localProfile = settings.EffectiveLearningLocalGenerationProfile(localModel);
            var localContextTokens = settings.EffectiveLearningLocalContextTokens(localModel);
            var model = backend == LearningTranslationBackend.Local ? localModel : endpointModel;
            var style = backend == LearningTranslationBackend.Local
                ? localProfile.Style.Effective
                : (settings.LearningTranslationActiveEndpointGeneration.Style ?? new()).Effective;
            var blockedApps = (settings.BlockedApps ?? [])
                .Where(app => !string.IsNullOrWhiteSpace(app))
                .Select(app => app.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var availabilitySettings = new SuggestionSettings
            {
                Enabled = settings.Enabled,
                Pause = settings.Pause,
                PauseOnBatterySaver = settings.PauseOnBatterySaver,
                BlockedApps = blockedApps,
                // Learning is always excluded from integrated terminals, regardless of the
                // autocomplete-only preference that SuggestionSettings normally carries.
                SuggestInIntegratedTerminals = false,
            };
            var options = new LearningTranslationOptions
            {
                SourceLanguage = sentenceLanguage,
                TargetLanguage = target,
                Style = style,
                ModelName = model,
                EndpointSampling = backend == LearningTranslationBackend.Endpoint
                    ? (settings.LearningTranslationActiveEndpointGeneration.Sampling ?? new()).EndpointOnly : new(),
                ContextTokens = backend == LearningTranslationBackend.Local
                    ? localContextTokens
                    : settings.LearningTranslationActiveEndpointContext,
            };

            LearningLocalModelConfiguration? localConfiguration = null;
            if (backend == LearningTranslationBackend.Local
                && localModel.Length > 0
                && settings.IsLocalModelCandidate(localModel, LocalModelTask.Translation)
                && LocalModelTaskPolicy.PermitsTemplate(selection.Template, LocalModelTask.Translation)
                && string.Equals(Path.GetFileName(localModel), localModel, StringComparison.Ordinal)
                && string.Equals(Path.GetExtension(localModel), ".gguf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    localConfiguration = new LearningLocalModelConfiguration(
                        Path.Combine(ModelCatalog.DefaultLocalDirectory(), localModel),
                        Enum.IsDefined(selection.Template)
                            ? selection.Template
                            : LearningChatTemplate.Automatic,
                        localContextTokens,
                        localProfile.Sampling);
                }
                catch (LearningTranslationException)
                {
                    // An invalid persisted budget/template remains unavailable; do not clamp it
                    // into another profile or load autocomplete's active model as a fallback.
                }
            }

            var endpointBaseUrl = (settings.LearningEndpointBaseUrl ?? OpenAICompatibleEndpointConfiguration.DefaultBaseUrl).Trim();
            var cacheScope = backend == LearningTranslationBackend.Local
                ? CreateLocalCacheScope(localConfiguration, localProfile, model)
                : CreateEndpointCacheScope(settings.LearningTranslationActiveEndpointEnabled, endpointBaseUrl,
                    endpointModel, credentialRevision);

            return new LearningRuntimeSettings(
                backend,
                settings.LearningTranslationEnabled,
                availabilitySettings,
                blockedApps,
                settings.LearningTranslationActiveEndpointEnabled,
                settings.LearningTranslationShortcut,
                GlobalShortcut.LearningTranslation.Conflict(settings, settings.LearningTranslationShortcut) is not null,
                endpointBaseUrl,
                sourceLanguages,
                options,
                localConfiguration,
                credentialRevision,
                cacheScope);
        }

        private static string? CreateEndpointCacheScope(bool endpointEnabled, string endpoint, string model, int credentialRevision)
        {
            if (!endpointEnabled || model.Length == 0) return null;

            // Cache keys outlive one engine instance but never contain a bearer token. Hashing the
            // endpoint, model and local credential generation gives Core an opaque profile scope.
            var profile = $"learning-chat-v1\nlearning-dpapi-key\n{endpoint}\n{model}\n{credentialRevision}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
        }

        private static string? CreateLocalCacheScope(
            LearningLocalModelConfiguration? configuration,
            LearningGenerationProfile profile,
            string model)
        {
            if (configuration is null || model.Length == 0) return null;
            var sampling = profile.Sampling.Normalized;
            var identity = string.Join("\n",
                "learning-local-runtime-v1",
                Path.GetFullPath(configuration.ModelPath),
                configuration.RequestedTemplate,
                configuration.ContextWindowTokens,
                sampling.Temperature?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "recommended",
                sampling.TopP?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "recommended",
                sampling.TopK?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "recommended",
                sampling.RepetitionPenalty?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "recommended",
                sampling.SeedMode,
                sampling.FixedSeed);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }
    }

}
