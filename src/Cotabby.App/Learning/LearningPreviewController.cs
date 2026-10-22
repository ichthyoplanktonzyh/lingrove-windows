using System.Net.Http;
using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.Learning;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Suggestions;
using Cotabby.Inference.LanguageLearning;
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
internal sealed class LearningPreviewController : IAsyncDisposable
{
    private static readonly TimeSpan CapturePollInterval = TimeSpan.FromMilliseconds(400);
    private const string SecureFieldMessage = "Sentence translation is unavailable in secure fields.";
    private const string OwnFieldMessage = "Cotabby does not translate text from its own windows.";
    private const string BlockedAppMessage = "Sentence translation is disabled in this app.";
    private const string SelectedTextMessage = "Collapse the text selection and leave the caret in the sentence you want to translate.";
    private const string IncompleteTextMessage = "The editor did not expose the complete field text, so the current sentence cannot be verified.";
    private const string UnreliableSelectionMessage = "The editor did not expose a reliable caret position for this sentence.";
    private const string UnsupportedFieldMessage = "Focus a supported text field and try the sentence shortcut again.";
    private const string ChangedFieldMessage = "The sentence changed before its preview could open. Try again.";
    private const string SameLanguageMessage = "Choose different source and target languages in Learning settings.";

    private readonly IKeyboardHook _hook;
    private readonly IFocusTracker _focus;
    private readonly Dispatcher _dispatcher;
    private readonly LearningPreviewCoordinator _coordinator = new();
    private readonly ILearningSentenceTokenizer _sentenceTokenizer;
    private readonly HttpClient _learningHttpClient;
    private readonly IOpenAICompatibleCredentialStore _credentials;
    private readonly ILogger<LearningPreviewController> _logger;
    private readonly object _gate = new();

    private LearningPreviewWindow? _window;
    private LearningRuntimeSettings? _settings;
    private CapturedSentence? _activeCapture;
    private CaptureOrigin? _pendingOrigin;
    private string? _activeRequestId;
    private bool _intentActive;
    private bool _disposed;
    private long _intentGeneration;
    private long _stateValidationGeneration;

    public LearningPreviewController(
        IKeyboardHook hook,
        IFocusTracker focus,
        Dispatcher dispatcher,
        ILearningSentenceTokenizer sentenceTokenizer,
        HttpClient learningHttpClient,
        IOpenAICompatibleCredentialStore credentials,
        ILogger<LearningPreviewController> logger)
    {
        _hook = hook;
        _focus = focus;
        _dispatcher = dispatcher;
        _sentenceTokenizer = sentenceTokenizer;
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
        LearningRuntimeSettings? previous;
        lock (_gate)
        {
            if (_disposed) return;
            previous = _settings;
            Volatile.Write(ref _settings, next);
        }

        if (previous is not null && !previous.HasSameTranslationInputs(next))
        {
            // Shortcut edits only change how a later request starts. Changing prompt direction,
            // provider consent, endpoint, credential, style or budget retires the current preview.
            CancelAndHide();
        }
    }

    /// <summary>Starts the same manual current-sentence command from a tray menu item.</summary>
    public void RequestFromCurrentField()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(RequestFromCurrentField);
            return;
        }

        var settings = Volatile.Read(ref _settings);
        if (settings is null || !settings.TranslationEnabled)
        {
            BeginIntent();
            var anchor = _focus.Current?.CaretRect ?? ScreenRect.Empty;
            EnsureWindow().ShowCaptureFailure(
                "Sentence translation is off. Enable it in Learning settings before requesting a preview.", anchor);
            return;
        }

        if (!settings.AppEnabled || settings.Pause?.IsActive(DateTimeOffset.Now) == true)
        {
            BeginIntent();
            var anchor = _focus.Current?.CaretRect ?? ScreenRect.Empty;
            var reason = settings.AppEnabled ? "Cotabby is paused." : "Cotabby is disabled.";
            EnsureWindow().ShowCaptureFailure(reason + " Resume it before requesting a sentence preview.", anchor);
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
        }

        _hook.KeyEvent -= OnKeyEvent;
        _focus.FocusChanged -= OnFocusChanged;
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
        CancelCurrentState();

        if (_dispatcher.CheckAccess())
        {
            _window?.Close();
            _window = null;
        }
        else
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _window?.Close();
                _window = null;
            });
        }

        // Core waits only for its documented bounded interval. The dedicated endpoint client is
        // owned by AppHost and stays alive until this drain has completed.
        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }

    private void OnKeyEvent(object? sender, KeyEventArgs args)
    {
        var input = args.Event;
        var settings = Volatile.Read(ref _settings);
        var focused = _focus.Current;
        bool isLearningChord = input.IsKeyDown && !input.IsRepeat && !args.Suppress
            && settings is { CanStart: true, ShortcutConflicts: false }
            && focused is not null && FieldEnvironmentFailure(focused, settings) is null
            && settings.Shortcut.Matches(input);

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
            _intentActive = true;
            _pendingOrigin = origin;
            prior = _activeCapture;
            _activeCapture = null;
            _activeRequestId = null;
        }

        CancelMonitor(prior?.Monitor);
        void CancelPreviousOnUi()
        {
            if (!IsPendingIntent(generation)) return;
            _coordinator.Cancel();
            _window?.HidePreview();
        }

        if (_dispatcher.CheckAccess()) CancelPreviousOnUi();
        else _dispatcher.BeginInvoke(CancelPreviousOnUi, DispatcherPriority.Normal);
        return generation;
    }

    private async Task CaptureAndStartAsync(
        long generation, LearningRuntimeSettings settings, CaptureOrigin? origin, ScreenRect fallbackAnchor)
    {
        if (generation < 0 || !IsCurrentIntent(generation)) return;

        var attempt = await Task.Run(() => Capture(settings, origin)).ConfigureAwait(false);
        if (!IsCurrentIntent(generation)) return;

        if (attempt.Capture is null)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentIntent(generation)) return;
                EnsureWindow().ShowCaptureFailure(attempt.ErrorMessage ?? UnsupportedFieldMessage,
                    attempt.ErrorAnchor.IsEmpty ? fallbackAnchor : attempt.ErrorAnchor);
            }, DispatcherPriority.Background);
            return;
        }

        var capture = attempt.Capture;
        if (capture.Settings.Options.SourceLanguage == capture.Settings.Options.TargetLanguage)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (IsCurrentIntent(generation))
                    EnsureWindow().ShowCaptureFailure(SameLanguageMessage, capture.Field.CaretRect);
            }, DispatcherPriority.Background);
            return;
        }

        // A second off-thread UIA read proves the exact capture is still the current editor state
        // before the translation coordinator can hand any sentence to an engine.
        bool isCurrent = await Task.Run(() => IsCaptureCurrent(capture)).ConfigureAwait(false);
        if (!IsCurrentIntent(generation)) return;
        if (!isCurrent)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (IsCurrentIntent(generation))
                    EnsureWindow().ShowCaptureFailure(ChangedFieldMessage, fallbackAnchor);
            }, DispatcherPriority.Background);
            return;
        }

        await _dispatcher.InvokeAsync(() => BeginPreview(generation, capture), DispatcherPriority.Background);
    }

    private CaptureAttempt Capture(LearningRuntimeSettings settings, CaptureOrigin? origin)
    {
        if (origin is null)
            return new CaptureAttempt(null, UnsupportedFieldMessage, ScreenRect.Empty);

        // These gates depend only on stable focus metadata and are checked before a fresh UIA read,
        // so a manual chord never asks a blocked or credential field for another text snapshot.
        var preflightFailure = FieldEnvironmentFailure(origin.Field, settings);
        if (preflightFailure is not null)
            return new CaptureAttempt(null, preflightFailure, origin.Field.CaretRect);

        FocusedField? field;
        try
        {
            // UIA is a cross-process call that can stall on a host. This method is only reached
            // from Task.Run, never from the low-level hook or the WPF dispatcher.
            field = _focus.Refresh();
        }
        catch (Exception)
        {
            return new CaptureAttempt(null, UnsupportedFieldMessage, ScreenRect.Empty);
        }

        if (field is null) return new CaptureAttempt(null, UnsupportedFieldMessage, ScreenRect.Empty);
        if (!origin.Matches(field))
            return new CaptureAttempt(null, ChangedFieldMessage, field.CaretRect);
        string? rejection = FieldCaptureFailure(field, settings);
        if (rejection is not null) return new CaptureAttempt(null, rejection, field.CaretRect);

        try
        {
            var collapsedCaret = new LearningTextRange(field.CaretOffset, 0);
            var range = LearningTextScopePolicy.ConversionRange(
                field.Text, collapsedCaret, _sentenceTokenizer, settings.Options.SourceLanguage);
            var source = LearningTextScopePolicy.Text(field.Text, range);

            // FocusedField currently has no native marked-text/composition value. The capture is
            // therefore preserved as unknown input-method evidence, which the Mac readonly preview
            // permits; no live-IME state is fabricated from key timing and this path cannot write.
            return new CaptureAttempt(
                new CapturedSentence(field, range, source, settings), null, field.CaretRect);
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
        if (CredentialFieldDetector.IsCredentialField(field)) return CredentialFieldDetector.BlockedReason;
        return null;
    }

    private bool IsCaptureCurrent(CapturedSentence capture)
    {
        try
        {
            var current = _focus.Refresh();
            return current is not null
                && SameCapturedField(capture.Field, current)
                && FieldCaptureFailure(current, capture.Settings) is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SameCapturedField(FocusedField captured, FocusedField current) =>
        current.ProcessId == captured.ProcessId
        && string.Equals(current.FieldId, captured.FieldId, StringComparison.Ordinal)
        && current.FocusChangeSequence == captured.FocusChangeSequence
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
        if (currentSettings is null
            || !currentSettings.CanStart
            || !currentSettings.HasSameTranslationInputs(capture.Settings)
            || latest is null
            || !SameCapturedField(capture.Field, latest)
            || FieldCaptureFailure(latest, capture.Settings) is not null)
        {
            CancelAndHide();
            return;
        }

        var monitor = new CancellationTokenSource();
        ILearningTranslationEngine engine = CreateEngine(capture.Settings);
        CapturedSentence? previousCapture;
        Task request;
        LearningPreviewState state;
        lock (_gate)
        {
            if (_disposed || _intentGeneration != generation)
            {
                monitor.Dispose();
                if (engine is IDisposable disposable) disposable.Dispose();
                return;
            }

            previousCapture = _activeCapture;
            _pendingOrigin = null;
            _activeCapture = capture with { Monitor = monitor };
            // Begin the Core request while holding the intent gate. A concurrent key/focus
            // invalidation then either wins before this point or cancels the request after it has
            // an identity; it cannot slip between a freshness check and request creation.
            request = _coordinator.PreviewAsync(capture.Source, capture.Settings.Options, engine);
            state = _coordinator.State;
            _activeRequestId = state.RequestId;
        }
        CancelMonitor(previousCapture?.Monitor);

        _ = ObserveRequestAsync(request, engine);
        _ = MonitorCaptureAsync(generation, capture with { Monitor = monitor }, monitor.Token);
    }

    private ILearningTranslationEngine CreateEngine(LearningRuntimeSettings settings)
    {
        // There is no local translation runtime in this Windows slice. The endpoint is a second,
        // explicit user opt-in, and its engine is created only in response to a manual request.
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
            if (engine is IDisposable disposable) disposable.Dispose();
        }
    }

    private async Task MonitorCaptureAsync(long generation, CapturedSentence capture, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(CapturePollInterval, cancellationToken).ConfigureAwait(false);
                bool current = await Task.Run(() => IsCaptureCurrent(capture), cancellationToken).ConfigureAwait(false);
                if (!current)
                {
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
        // The Core event is deliberately not UI-thread-bound. Post its immutable snapshot before
        // starting another UIA validation so neither the coordinator lock nor hook can touch WPF.
        _ = _dispatcher.BeginInvoke(() => ValidateAndRenderState(state), DispatcherPriority.Background);
    }

    private void ValidateAndRenderState(LearningPreviewState state)
    {
        CapturedSentence? capture;
        long intent;
        long validation = Interlocked.Increment(ref _stateValidationGeneration);
        lock (_gate)
        {
            if (_disposed || !_intentActive || _activeCapture is null || _activeRequestId != state.RequestId) return;
            capture = _activeCapture;
            intent = _intentGeneration;
        }

        _ = ValidateThenRenderAsync(state, capture, intent, validation);
    }

    private async Task ValidateThenRenderAsync(
        LearningPreviewState state, CapturedSentence capture, long intent, long validation)
    {
        if (state.Status == LearningPreviewStatus.Cancelled) return;
        bool current = await Task.Run(() => IsCaptureCurrent(capture)).ConfigureAwait(false);
        if (!current)
        {
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (IsCurrentIntent(intent)) CancelAndHide();
            }, DispatcherPriority.Background);
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            if (validation != Interlocked.Read(ref _stateValidationGeneration)
                || !IsCurrentState(intent, state.RequestId)) return;
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

            EnsureWindow().ShowState(state, capture.Field.CaretRect,
                canRetry: state.Status == LearningPreviewStatus.Failed);
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

        if (capture is null || settings is null || !settings.HasSameTranslationInputs(capture.Settings))
        {
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
        CapturedSentence? capture;
        long generation;
        lock (_gate)
        {
            if (_disposed && !_intentActive && _activeCapture is null) return;
            generation = ++_intentGeneration;
            _intentActive = false;
            _pendingOrigin = null;
            capture = _activeCapture;
            _activeCapture = null;
            _activeRequestId = null;
        }

        CancelMonitor(capture?.Monitor);
        void CancelAndHideOnUi()
        {
            if (!IsRetiredIntent(generation)) return;
            _coordinator.Cancel();
            _window?.HidePreview();
        }

        if (_dispatcher.CheckAccess()) CancelAndHideOnUi();
        else _ = _dispatcher.BeginInvoke(CancelAndHideOnUi, DispatcherPriority.Normal);
    }

    private void CancelCurrentState() => CancelAndHide();

    private bool IsIntentActive()
    {
        lock (_gate) return _intentActive;
    }

    private bool IsCurrentIntent(long generation)
    {
        lock (_gate) return !_disposed && _intentActive && _intentGeneration == generation;
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

    private LearningPreviewWindow EnsureWindow()
    {
        _dispatcher.VerifyAccess();
        if (_window is not null) return _window;

        _window = new LearningPreviewWindow();
        _window.RetryRequested += RetryCurrentCapture;
        _window.CancelRequested += CancelAndHide;
        _window.DismissRequested += CancelAndHide;
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

    /// <summary>Field identity sampled from the tracker before off-thread UIA reads begin.</summary>
    private sealed record CaptureOrigin(FocusedField Field)
    {
        public bool Matches(FocusedField current) => current.ProcessId == Field.ProcessId
            && string.Equals(current.FieldId, Field.FieldId, StringComparison.Ordinal)
            && current.FocusChangeSequence == Field.FocusChangeSequence;

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
        CancellationTokenSource? Monitor = null);

    /// <summary>One immutable projection of only the settings that belong to translation.</summary>
    private sealed record LearningRuntimeSettings(
        bool TranslationEnabled,
        bool AppEnabled,
        PauseState? Pause,
        IReadOnlySet<string> BlockedApps,
        bool EndpointEnabled,
        KeyBinding Shortcut,
        bool ShortcutConflicts,
        string EndpointBaseUrl,
        LearningTranslationOptions Options,
        int CredentialRevision)
    {
        public bool CanStart => TranslationEnabled && AppEnabled && Pause?.IsActive(DateTimeOffset.Now) != true;

        public bool HasSameTranslationInputs(LearningRuntimeSettings other) =>
            TranslationEnabled == other.TranslationEnabled
            && AppEnabled == other.AppEnabled
            && (Pause?.IsActive(DateTimeOffset.Now) == true) == (other.Pause?.IsActive(DateTimeOffset.Now) == true)
            && BlockedApps.SetEquals(other.BlockedApps)
            && EndpointEnabled == other.EndpointEnabled
            && string.Equals(EndpointBaseUrl, other.EndpointBaseUrl, StringComparison.Ordinal)
            && Options == other.Options
            && CredentialRevision == other.CredentialRevision;

        public static LearningRuntimeSettings From(AppSettings settings, int credentialRevision)
        {
            var source = Enum.IsDefined(settings.LearningSourceLanguage)
                ? settings.LearningSourceLanguage
                : LearningLanguage.Chinese;
            var target = Enum.IsDefined(settings.LearningTargetLanguage)
                ? settings.LearningTargetLanguage
                : LearningLanguage.English;
            var register = Enum.IsDefined(settings.LearningStyleRegister)
                ? settings.LearningStyleRegister
                : LearningTranslationRegister.Natural;
            var model = (settings.LearningEndpointModel ?? string.Empty).Trim();
            var style = new LearningTranslationStyle(register, settings.LearningCustomInstruction ?? string.Empty).Effective;
            var blockedApps = (settings.BlockedApps ?? [])
                .Where(app => !string.IsNullOrWhiteSpace(app))
                .Select(app => app.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var options = new LearningTranslationOptions
            {
                SourceLanguage = source,
                TargetLanguage = target,
                Style = style,
                ModelName = model,
                ContextTokens = Math.Clamp(settings.LearningContextTokens,
                    2_048, ModelContextBudget.MaximumEndpointTokens),
            };

            return new LearningRuntimeSettings(
                settings.LearningTranslationEnabled,
                settings.Enabled,
                settings.Pause,
                blockedApps,
                settings.LearningEndpointEnabled,
                settings.LearningTranslationShortcut,
                GlobalShortcut.LearningTranslation.Conflict(settings, settings.LearningTranslationShortcut) is not null,
                (settings.LearningEndpointBaseUrl ?? OpenAICompatibleEndpointConfiguration.DefaultBaseUrl).Trim(),
                options,
                credentialRevision);
        }
    }

}
