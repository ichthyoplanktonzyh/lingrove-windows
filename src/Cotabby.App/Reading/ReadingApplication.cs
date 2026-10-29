using System.Windows.Threading;
using Cotabby.App.Settings;
using Cotabby.App.UI.Controls;
using Cotabby.App.UI.Settings.Panes;
using Cotabby.Core.Input;
using Cotabby.Core.Focus;
using Cotabby.Core.Context;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.Suggestions;
using Cotabby.Win32.Reading;
using Cotabby.App.Vocabulary;
using Cotabby.App.UI.Reading;
using Cotabby.Core.Runtime.Metrics;

namespace Cotabby.App.Reading;

/// <summary>Manual selected-text entry using existing hook, focus, translation and dictionary owners.</summary>
internal sealed class ReadingApplication : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IKeyboardHook _hook;
    private readonly Func<ReadingForeground?> _origin;
    private readonly IReadingSelectionReader _reader;
    private readonly Func<string, LearningLanguage?, IReadOnlyList<LearningDictionaryWord>> _words;
    private readonly ILearningDictionaryProviding _dictionary;
    private readonly Func<ReadingLookupInput, CancellationToken, Task<ReadingLookupResult>> _basicLookup;
    private readonly Func<ReadingLookupInput, ReadingLookupResult, CancellationToken, Task<string>> _detailsLookup;
    private readonly Action _configure;
    private Func<ReadingLookupInput, ReadingLookupResult, LearningExplanationInput, CancellationToken, Task<LearningExplanationRecord>>? _detailsRecord;
    private readonly IClipboardTextWriter _clipboard;
    private readonly IWritingVocabularyStore _vocabulary;
    private readonly Func<string, LearningLanguage?, IReadOnlyList<LearningTextRange>> _sentences;
    private readonly Func<AppSettings, int, Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>>> _translation;
    private readonly Action _beforeCapture;
    private readonly Func<bool> _otherInlineActive;
    private readonly Func<(int X, int Y)?> _pointerPosition;
    private readonly List<Task> _retired = [];
    private readonly object _lifecycleGate = new();
    private Preferences? _preferences;
    private ReadingSession? _session;
    private ReadingPresentation? _presentation;
    private Cotabby.App.Speech.LearningSpeechController? _speechController;
    internal void AttachSpeech(Cotabby.App.Speech.LearningSpeechController speech)
    {
        _dispatcher.VerifyAccess(); ArgumentNullException.ThrowIfNull(speech);
        if (_speechController is not null && !ReferenceEquals(_speechController, speech))
            throw new InvalidOperationException("Speech is already attached.");
        _speechController = speech; _presentation?.AttachSpeech(speech);
    }
    private int _active;
    private long _revision;
    private bool _disposed;
    private Task? _disposal;
    private IReadingPointerMonitor? _pointer;
    private ReadingAffordanceCoordinator? _affordance;
    private ReadingAffordanceWindow? _icon;
    private bool _pointerStarted;
    private IReadingScreenReader? _screenReader;
    private Action<PerformanceRequestObservation>? _observe;

    internal ReadingApplication(Dispatcher dispatcher, IKeyboardHook hook, Func<ReadingForeground?> origin,
        IReadingSelectionReader reader, Func<string, LearningLanguage?, IReadOnlyList<LearningDictionaryWord>> words,
        ILearningDictionaryProviding dictionary,
        Func<ReadingLookupInput, CancellationToken, Task<ReadingLookupResult>> basicLookup,
        Func<ReadingLookupInput, ReadingLookupResult, CancellationToken, Task<string>> detailsLookup, Action configure, IClipboardTextWriter clipboard,
        IWritingVocabularyStore vocabulary, Func<string, LearningLanguage?, IReadOnlyList<LearningTextRange>> sentences,
        Func<AppSettings, int, Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>>> translation,
        Action beforeCapture, Func<bool> otherInlineActive, Func<(int X, int Y)?>? pointerPosition = null)
    {
        _dispatcher = dispatcher; _hook = hook; _origin = origin; _reader = reader; _words = words;
        _dictionary = dictionary; _basicLookup = basicLookup; _detailsLookup = detailsLookup; _configure = configure; _clipboard = clipboard;
        _vocabulary = vocabulary; _sentences = sentences; _translation = translation;
        _beforeCapture = beforeCapture; _otherInlineActive = otherInlineActive;
        _pointerPosition = pointerPosition ?? ReadingPointerMonitor.CurrentPosition;
        hook.KeyEvent += OnKey;
    }
    internal bool IsActive => Volatile.Read(ref _active) != 0;
    internal void AttachDetails(Func<ReadingLookupInput, ReadingLookupResult, LearningExplanationInput, CancellationToken, Task<LearningExplanationRecord>> generate)
    { _dispatcher.VerifyAccess(); if (_detailsRecord is not null) throw new InvalidOperationException("Reading details already attached."); _detailsRecord = generate; }
    internal void AttachScreen(IReadingScreenReader reader)
    { _dispatcher.VerifyAccess(); if (_screenReader is not null) throw new InvalidOperationException("Screen reading already attached."); _screenReader = reader; }
    internal void AttachPerformance(Action<PerformanceRequestObservation> observe)
    { _dispatcher.VerifyAccess(); _observe = observe ?? throw new ArgumentNullException(nameof(observe)); }

    // AppHost supplies the single observation-only mouse owner. The reader and runtime remain shared.
    internal void AttachPointer(IReadingPointerMonitor pointer)
    {
        _dispatcher.VerifyAccess(); if (_pointer is not null) throw new InvalidOperationException("Reading pointer already attached.");
        _pointer = pointer; _icon = new ReadingAffordanceWindow();
        _affordance = new(_dispatcher, pointer, _reader, _origin, CanCapturePassive,
            origin => _preferences is { } p && ReadingAccessPolicy.IconAllowed(origin.ProcessName, p.IconEnabled, p.AppOverrides),
            (x, y) => _icon.Contains(x, y) || Volatile.Read(ref _presentation)?.Contains(x, y) == true,
            Withdraw,
            (lease, origin) => { if (_preferences is { } p) Start(p, null, origin, Interlocked.Read(ref _revision), lease); else { lease.Retire(); _retired.Add(lease.DisposeAsync().AsTask()); } },
            (_, x, y, activate) => _icon.ShowAt(x, y, activate), () => _icon.HideAffordance(),
            () => Volatile.Read(ref _screenReader)?.IsSelectingRegion == true);
        UpdatePointer();
    }

    private bool CanCapturePassive(ReadingForeground origin)
    {
        var p = Volatile.Read(ref _preferences);
        return p is not null && p.Enabled && !_otherInlineActive() && origin.ProcessId != Environment.ProcessId
            && SuggestionAvailability.DisabledReason(p.Availability, DateTimeOffset.UtcNow, p.BatterySaver) is null
            && !p.Availability.BlockedApps.Contains(origin.ProcessName) && !TerminalAppDetector.IsTerminal(origin.ProcessName)
            && (ReadingAccessPolicy.Allowed(ReadingSourceKind.Selection, origin.ProcessName, p.AppOverrides)
                || ReadingAccessPolicy.IconAllowed(origin.ProcessName, p.IconEnabled, p.AppOverrides));
    }
    private void UpdatePointer()
    {
        if (_pointer is null) return;
        bool enabled = _preferences is { } p && (p.Enabled || p.PointerEnabled || p.ScreenEnabled);
        if (enabled && !_pointerStarted) { _pointer.Start(); _pointerStarted = true; }
        else if (!enabled && _pointerStarted) { _pointer.Stop(); _pointerStarted = false; }
    }

    internal void Withdraw()
    {
        ReadingSession? session;
        lock (_lifecycleGate)
        {
            Interlocked.Increment(ref _revision);
            session = _session; session?.Withdraw();
        }
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        try { _dispatcher.BeginInvoke(() => { if (!_disposed && ReferenceEquals(session, _session)) Retire(); }); }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) { }
    }

    internal void ApplySettings(AppSettings settings, int credentialRevision, bool batterySaver)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        var availability = settings.ToSuggestionSettings();
        bool enabled = settings.LearningReadingEnabled && settings.LearningTranslationEnabled;
        var explanation = settings.LearningExplanationSnapshot();
        var explanationEndpoint = Cotabby.App.Learning.LearningModelDestinationText.Endpoint(explanation);
        var translationSelection = settings.LearningTranslationActiveSelection;
        var translation = translationSelection.Backend == LearningModelBackend.Endpoint
            ? new ReadingDestination("", true, settings.LearningTranslationActiveEndpointEnabled, settings.LearningEndpointBaseUrl,
                ModelDisplayText.Safe(translationSelection.EndpointModel))
            : new ReadingDestination(string.IsNullOrWhiteSpace(translationSelection.LocalModel)
                ? "Local translation model not configured" : "Local translation model: " + ModelDisplayText.Safe(translationSelection.LocalModel), false, true);
        var target = Enum.IsDefined(settings.LearningReadingTargetLanguage) ? settings.LearningReadingTargetLanguage : LearningLanguage.Chinese;
        var next = new Preferences(enabled, settings.LearningReadingShortcut,
            GlobalShortcut.LearningReading.Conflict(settings, settings.LearningReadingShortcut) is not null,
            availability, batterySaver, target, translation,
            // A conservative complete snapshot invalidates old work on any settings mutation.
            System.Text.Json.JsonSerializer.Serialize(settings), credentialRevision,
            _translation(settings, credentialRevision), (settings.ShortcutOverrides ?? []).ToArray(), settings.LearningReadingIconEnabled)
        {
            LookupLanguage = settings.ResolvedLearningExplanationLanguage,
            PointerEnabled = settings.LearningReadingPointerEnabled && settings.LearningTranslationEnabled,
            PointerShortcut = settings.LearningReadingPointerShortcut,
            PointerConflict = GlobalShortcut.ReadingPointer.Conflict(settings, settings.LearningReadingPointerShortcut) is not null,
            ScreenEnabled = settings.LearningReadingScreenshotEnabled && settings.LearningTranslationEnabled,
            ScreenShortcut = settings.LearningReadingScreenshotShortcut,
            ScreenConflict = GlobalShortcut.ReadingScreenshot.Conflict(settings, settings.LearningReadingScreenshotShortcut) is not null,
            TranslationProfile = translationSelection,
            TranslationModelName = translationSelection.Backend == LearningModelBackend.Endpoint
                ? ModelDisplayText.Safe(translationSelection.EndpointModel) : ModelDisplayText.Safe(translationSelection.LocalModel),
            LookupProfile = explanation,
            LookupDestination = explanationEndpoint is not null
                ? new("", true, true, explanationEndpoint.BaseUrl.AbsoluteUri, ModelDisplayText.Safe(explanationEndpoint.ModelName))
                : new(Cotabby.App.Learning.LearningModelDestinationText.Explanation(explanation) + " · bundled dictionary available", false, true),
        };
        if (_preferences is { } old && old.Signature == next.Signature && old.CredentialRevision == credentialRevision
            && old.BatterySaver == batterySaver && old.LookupProfile == next.LookupProfile && old.TranslationProfile == next.TranslationProfile) return;
        Volatile.Write(ref _preferences, next); Interlocked.Increment(ref _revision); _affordance?.Invalidate(); Retire(); UpdatePointer();
    }

    private void OnKey(object? sender, KeyEventArgs args)
    {
        var preferences = Volatile.Read(ref _preferences); var origin = _origin(); var key = args.Event;
        if (preferences is null || args.Suppress || !key.IsKeyDown || _otherInlineActive() || origin is null
            || SuggestionAvailability.DisabledReason(preferences.Availability, DateTimeOffset.UtcNow, preferences.BatterySaver) is not null
            || origin.ProcessId == Environment.ProcessId || preferences.Availability.BlockedApps.Contains(origin.ProcessName)
            || TerminalAppDetector.IsTerminal(origin.ProcessName)) return;
        bool selected = preferences.Enabled && !preferences.Conflict && preferences.Shortcut.Matches(key)
            && ReadingAccessPolicy.Allowed(ReadingSourceKind.Selection, origin.ProcessName, preferences.AppOverrides);
        bool pointer = preferences.PointerEnabled && !preferences.PointerConflict && preferences.PointerShortcut.Matches(key)
            && ReadingAccessPolicy.Allowed(ReadingSourceKind.Pointer, origin.ProcessName, preferences.AppOverrides)
            && _reader is IReadingPointerReader;
        bool screen = preferences.ScreenEnabled && !preferences.ScreenConflict && preferences.ScreenShortcut.Matches(key)
            && ReadingAccessPolicy.Allowed(ReadingSourceKind.Screenshot, origin.ProcessName, preferences.AppOverrides)
            && Volatile.Read(ref _screenReader) is not null;
        if (!selected && !pointer && !screen) return;
        var point = pointer ? _pointerPosition() : null;
        if (pointer && point is null) return;
        args.Suppress = true;
        if (key.IsRepeat) return;
        if (selected) _affordance?.ArmShortcut(origin, key.VirtualKey);
        long revision = Interlocked.Read(ref _revision);
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed || revision != Interlocked.Read(ref _revision) || origin != _origin() || _otherInlineActive()) return;
            if (selected && _affordance is not null && _affordance.TryConsumeOrWait(origin, out var pending))
            { _retired.Add(pending); return; }
            if (pointer || screen) _affordance?.Invalidate();
            Start(preferences, key.VirtualKey, origin, Interlocked.Read(ref _revision), pointer: point, screen: screen);
        });
    }

    private void Start(Preferences preferences, int? key, ReadingForeground origin, long revision, IReadingSelectionLease? existing = null,
        (int X, int Y)? pointer = null, bool screen = false)
    {
        Retire(); _beforeCapture();
        var presentation = new ReadingPresentation(_dispatcher, _dictionary, _basicLookup, _detailsLookup, _configure, _clipboard,
            _vocabulary, _sentences, preferences.LookupLanguage, _detailsRecord);
        if (_speechController is not null) presentation.AttachSpeech(_speechController);
        IReadingSelectionReader reader = existing is not null ? new CapturedReader(existing) : screen && _screenReader is not null ? _screenReader
            : pointer is { } point && _reader is IReadingPointerReader pointerReader ? new PointerReader(pointerReader, point) : _reader;
        var session = new ReadingSession(_dispatcher, new OriginReader(reader, _origin, origin), presentation,
            _ => new(null, ReadingLanguageEvidence.Unavailable),
            (text, language) => _words(text, language).Count,
            preferences.Translate,
            async (selection, route, source, target, current, token) =>
            {
                var words = await Task.Run(() => { token.ThrowIfCancellationRequested(); return _words(route.LookupText!, source); }, token);
                token.ThrowIfCancellationRequested();
                await presentation.LookupAsync(selection, route, source, target, current, token, words);
            }, _observe);
        presentation.DismissRequested += session.Dismiss;
        presentation.Hidden += () => { if (ReferenceEquals(_session, session)) Volatile.Write(ref _active, 0); };
        lock (_lifecycleGate)
        {
            if (revision != Interlocked.Read(ref _revision))
            {
                if (existing is not null) { existing.Retire(); _retired.Add(existing.DisposeAsync().AsTask()); }
                _retired.Add(RetireAsync(session, presentation)); return;
            }
            _presentation = presentation; Volatile.Write(ref _session, session);
            session.Begin(new(true, true, true, preferences.Target, preferences.Destination,
                preferences.LookupDestination, revision) { Source = screen ? ReadingSourceKind.Screenshot : pointer is null ? ReadingSourceKind.Selection : ReadingSourceKind.Pointer,
                    TranslationModelName = preferences.TranslationModelName }, key);
            Volatile.Write(ref _active, session.IsActive ? 1 : 0);
        }
    }

    private void Retire()
    {
        _dispatcher.VerifyAccess(); Volatile.Write(ref _active, 0);
        ReadingSession? session; ReadingPresentation? presentation;
        lock (_lifecycleGate) { session = _session; presentation = _presentation; Volatile.Write(ref _session, null); _presentation = null; }
        if (session is not null && presentation is not null) _retired.Add(RetireAsync(session, presentation));
        _retired.RemoveAll(task => task.IsCompletedSuccessfully);
    }
    private static async Task RetireAsync(ReadingSession session, ReadingPresentation presentation)
    { try { await session.DisposeAsync(); } finally { await presentation.DisposeAsync(); } }
    public ValueTask DisposeAsync()
    {
        if (!_dispatcher.CheckAccess()) return new(_dispatcher.InvokeAsync(() => DisposeAsync().AsTask()).Task.Unwrap());
        return new(_disposal ??= DisposeOnDispatcherAsync());
    }
    private async Task DisposeOnDispatcherAsync()
    {
        if (_disposed) return;
        _disposed = true; Interlocked.Increment(ref _revision); _hook.KeyEvent -= OnKey;
        if (_affordance is not null) await _affordance.DisposeAsync();
        if (_pointerStarted) { _pointer!.Stop(); _pointerStarted = false; }
        _icon?.Close(); Retire();
        await Task.WhenAll(_retired);
    }
    private sealed record Preferences(bool Enabled, KeyBinding Shortcut, bool Conflict, SuggestionSettings Availability,
        bool BatterySaver, LearningLanguage Target, ReadingDestination Destination, string Signature, int CredentialRevision,
        Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>> Translate,
        IReadOnlyList<PerAppShortcutOverride> AppOverrides, bool IconEnabled)
    {
        internal ReadingDestination LookupDestination { get; init; } = new("Local AI explanation model · bundled dictionary available", false, true);
        internal LearningModelSnapshot? LookupProfile { get; init; }
        internal LearningPowerProfile? TranslationProfile { get; init; }
        internal LearningLanguage LookupLanguage { get; init; }
        internal bool PointerEnabled { get; init; }
        internal KeyBinding PointerShortcut { get; init; } = KeyBinding.Unbound;
        internal bool PointerConflict { get; init; }
        internal bool ScreenEnabled { get; init; }
        internal KeyBinding ScreenShortcut { get; init; } = KeyBinding.Unbound;
        internal bool ScreenConflict { get; init; }
        internal string TranslationModelName { get; init; } = "";
    }

    private sealed class PointerReader(IReadingPointerReader reader, (int X, int Y) point) : IReadingSelectionReader
    {
        public Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null) =>
            reader.CapturePointerAsync(point.X, point.Y, token, initiatingVirtualKey);
    }

    private sealed class CapturedReader(IReadingSelectionLease lease) : IReadingSelectionReader
    {
        private IReadingSelectionLease? _lease = lease;
        public Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
        {
            var result = Interlocked.Exchange(ref _lease, null);
            return Task.FromResult(result);
        }
    }

    // A queued shortcut is authorized for the focus snapshot that won arbitration, not a later
    // process/field that happens to become foreground while native capture is pending.
    private sealed class OriginReader(IReadingSelectionReader inner, Func<ReadingForeground?> current, ReadingForeground origin) : IReadingSelectionReader
    {
        public async Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
        {
            var lease = await inner.CaptureAsync(token, initiatingVirtualKey).ConfigureAwait(false);
            if (lease is null) return null;
            var selection = lease.Selection;
            if (selection.ProcessId == origin.ProcessId && selection.FocusEpoch == origin.FocusEpoch && current() == origin) return new OriginLease(lease, origin.ProcessName);
            lease.Retire(); await lease.DisposeAsync().ConfigureAwait(false); return null;
        }
    }
    private sealed class OriginLease(IReadingSelectionLease inner, string processName) : IReadingSelectionLease
    {
        public ReadingSelection Selection => inner.Selection with { SourceApplicationId = processName };
        public bool IsLive => inner.IsLive;
        public event Action? Invalidated { add => inner.Invalidated += value; remove => inner.Invalidated -= value; }
        public Task<bool> IsCurrentAsync(CancellationToken token) => inner.IsCurrentAsync(token);
        public Task EnrichAsync(CancellationToken token) => inner.EnrichAsync(token);
        public void ArmActivationKey(int virtualKey) => inner.ArmActivationKey(virtualKey);
        public void Retire() => inner.Retire();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
