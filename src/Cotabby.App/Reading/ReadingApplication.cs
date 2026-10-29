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
    private readonly IClipboardTextWriter _clipboard;
    private readonly IWritingVocabularyStore _vocabulary;
    private readonly Func<string, LearningLanguage?, IReadOnlyList<LearningTextRange>> _sentences;
    private readonly Func<AppSettings, int, Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>>> _translation;
    private readonly Action _beforeCapture;
    private readonly Func<bool> _otherInlineActive;
    private readonly List<Task> _retired = [];
    private readonly object _lifecycleGate = new();
    private Preferences? _preferences;
    private ReadingSession? _session;
    private ReadingPresentation? _presentation;
    private int _active;
    private long _revision;
    private bool _disposed;
    private Task? _disposal;

    internal ReadingApplication(Dispatcher dispatcher, IKeyboardHook hook, Func<ReadingForeground?> origin,
        IReadingSelectionReader reader, Func<string, LearningLanguage?, IReadOnlyList<LearningDictionaryWord>> words,
        ILearningDictionaryProviding dictionary,
        Func<ReadingLookupInput, CancellationToken, Task<ReadingLookupResult>> basicLookup,
        Func<ReadingLookupInput, ReadingLookupResult, CancellationToken, Task<string>> detailsLookup, Action configure, IClipboardTextWriter clipboard,
        IWritingVocabularyStore vocabulary, Func<string, LearningLanguage?, IReadOnlyList<LearningTextRange>> sentences,
        Func<AppSettings, int, Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>>> translation,
        Action beforeCapture, Func<bool> otherInlineActive)
    {
        _dispatcher = dispatcher; _hook = hook; _origin = origin; _reader = reader; _words = words;
        _dictionary = dictionary; _basicLookup = basicLookup; _detailsLookup = detailsLookup; _configure = configure; _clipboard = clipboard;
        _vocabulary = vocabulary; _sentences = sentences; _translation = translation;
        _beforeCapture = beforeCapture; _otherInlineActive = otherInlineActive;
        hook.KeyEvent += OnKey;
    }
    internal bool IsActive => Volatile.Read(ref _active) != 0;

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
        var translation = settings.EffectiveLearningBackend == LearningTranslationBackend.Endpoint
            ? new ReadingDestination("", true, settings.LearningEndpointEnabled, settings.LearningEndpointBaseUrl,
                ModelDisplayText.Safe(settings.LearningEndpointModel))
            : new ReadingDestination(string.IsNullOrWhiteSpace(settings.LearningLocalModel)
                ? "Local translation model not configured" : "Local translation model: " + ModelDisplayText.Safe(settings.LearningLocalModel), false, true);
        var target = Enum.IsDefined(settings.LearningReadingTargetLanguage) ? settings.LearningReadingTargetLanguage : LearningLanguage.Chinese;
        var next = new Preferences(enabled, settings.LearningReadingShortcut,
            GlobalShortcut.LearningReading.Conflict(settings, settings.LearningReadingShortcut) is not null,
            availability, batterySaver, target, translation,
            // A conservative complete snapshot invalidates old work on any settings mutation.
            System.Text.Json.JsonSerializer.Serialize(settings), credentialRevision,
            _translation(settings, credentialRevision))
        {
            LookupLanguage = settings.ResolvedLearningExplanationLanguage,
            LookupLabel = string.IsNullOrWhiteSpace(settings.LearningExplanationSnapshot().LocalModel)
                ? "Local AI explanation model not configured · bundled dictionary available"
                : "Local AI explanation model: " + ModelDisplayText.Safe(settings.LearningExplanationSnapshot().LocalModel)
                    + " · bundled dictionary available",
        };
        if (_preferences is { } old && old.Signature == next.Signature && old.CredentialRevision == credentialRevision && old.BatterySaver == batterySaver) return;
        Volatile.Write(ref _preferences, next); Interlocked.Increment(ref _revision); Retire();
    }

    private void OnKey(object? sender, KeyEventArgs args)
    {
        var preferences = Volatile.Read(ref _preferences); var origin = _origin(); var key = args.Event;
        if (preferences is null || !preferences.Enabled || preferences.Conflict || args.Suppress || !key.IsKeyDown
            || !preferences.Shortcut.Matches(key) || _otherInlineActive() || origin is null
            || SuggestionAvailability.DisabledReason(preferences.Availability, DateTimeOffset.UtcNow, preferences.BatterySaver) is not null
            || origin.ProcessId == Environment.ProcessId || preferences.Availability.BlockedApps.Contains(origin.ProcessName)
            || TerminalAppDetector.IsTerminal(origin.ProcessName)) return;
        args.Suppress = true;
        if (key.IsRepeat) return;
        long revision = Interlocked.Read(ref _revision);
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed || revision != Interlocked.Read(ref _revision) || origin != _origin() || _otherInlineActive()) return;
            Start(preferences, key.VirtualKey, origin, revision);
        });
    }

    private void Start(Preferences preferences, int key, ReadingForeground origin, long revision)
    {
        Retire(); _beforeCapture();
        var presentation = new ReadingPresentation(_dispatcher, _dictionary, _basicLookup, _detailsLookup, _configure, _clipboard,
            _vocabulary, _sentences, preferences.LookupLanguage);
        var session = new ReadingSession(_dispatcher, new OriginReader(_reader, _origin, origin), presentation,
            _ => new(null, ReadingLanguageEvidence.Unavailable),
            (text, language) => _words(text, language).Count,
            preferences.Translate,
            async (selection, route, source, target, current, token) =>
            {
                var words = await Task.Run(() => { token.ThrowIfCancellationRequested(); return _words(route.LookupText!, source); }, token);
                token.ThrowIfCancellationRequested();
                await presentation.LookupAsync(selection, route, source, target, current, token, words);
            });
        presentation.DismissRequested += session.Dismiss;
        presentation.Hidden += () => { if (ReferenceEquals(_session, session)) Volatile.Write(ref _active, 0); };
        lock (_lifecycleGate)
        {
            if (revision != Interlocked.Read(ref _revision)) { _retired.Add(RetireAsync(session, presentation)); return; }
            _presentation = presentation; Volatile.Write(ref _session, session);
            session.Begin(new(true, true, true, preferences.Target, preferences.Destination,
                new(preferences.LookupLabel, false, true), revision), key);
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
        _disposed = true; Interlocked.Increment(ref _revision); _hook.KeyEvent -= OnKey; Retire();
        await Task.WhenAll(_retired);
    }
    private sealed record Preferences(bool Enabled, KeyBinding Shortcut, bool Conflict, SuggestionSettings Availability,
        bool BatterySaver, LearningLanguage Target, ReadingDestination Destination, string Signature, int CredentialRevision,
        Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>> Translate)
    {
        internal string LookupLabel { get; init; } = "Local AI explanation model · bundled dictionary available";
        internal LearningLanguage LookupLanguage { get; init; }
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
        public void Retire() => inner.Retire();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
