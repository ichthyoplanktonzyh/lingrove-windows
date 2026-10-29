using System.Diagnostics;
using System.Windows.Threading;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>Committed interaction and eligibility snapshot. Revision covers explanation profile,
/// target/explanation language, pause, disabled applications and shortcut-conflict configuration.</summary>
internal sealed record ExpressionAssistFeatureSettings(bool Enabled = false, string Trigger = "??", int ShortcutKey = 0,
    KeyModifiers ShortcutModifiers = KeyModifiers.None, bool Available = false, long Revision = 0);

/// <summary>Routes existing shared input and focus events into one explicit expression session.
/// It never starts or owns the hook/focus tracker. ShortcutConflict must be a cheap, thread-safe
/// pure test of the active field and committed shortcut configuration; it performs no UIA reads.
/// GuardedRefresh must recheck its permit before every native read phase and publication; there is
/// deliberately no unguarded IFocusTracker.Refresh fallback.</summary>
internal sealed class ExpressionAssistFeature : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly IFocusTracker _focus;
    private readonly IKeyboardHook _input;
    private readonly Func<Func<bool>, FocusedField?> _guardedRefresh;
    private readonly Func<FocusedField, (ExpressionAssistSession Session, Action Close)> _factory;
    private readonly Func<FocusedField?, ExpressionAssistFeatureSettings, bool> _shortcutConflict;
    private readonly Func<FocusedField, bool> _fieldAvailable;
    private readonly ExpressionAssistTriggerObserver _observer = new();
    private readonly HashSet<Task> _work = [];
    private ExpressionAssistFeatureSettings _settings = new();
    private FocusedField? _field;
    private ExpressionAssistSession? _session;
    private Action? _close;
    private CancellationTokenSource? _probe;
    private readonly Dictionary<CancellationTokenSource, Task> _probeCancellations = [];
    private long _epoch, _observerEpoch;
    private bool _running, _active, _disposed;
    private Task? _disposal;

    internal bool IsActive { get { lock (_gate) return _active; } }

    internal ExpressionAssistFeature(Dispatcher dispatcher, IFocusTracker focus, IKeyboardHook input,
        Func<Func<bool>, FocusedField?> guardedRefresh,
        Func<FocusedField, (ExpressionAssistSession Session, Action Close)> factory,
        Func<FocusedField?, ExpressionAssistFeatureSettings, bool> shortcutConflict,
        Func<FocusedField, bool>? fieldAvailable = null)
    {
        dispatcher.VerifyAccess(); _dispatcher = dispatcher; _focus = focus; _input = input;
        _guardedRefresh = guardedRefresh ?? throw new ArgumentNullException(nameof(guardedRefresh));
        _factory = factory; _shortcutConflict = shortcutConflict;
        _fieldAvailable = fieldAvailable ?? (static _ => true);
    }

    internal void Start(ExpressionAssistFeatureSettings settings)
    {
        _dispatcher.VerifyAccess();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running) return;
            _settings = settings; _field = _focus.Current; Interlocked.Increment(ref _epoch); Volatile.Write(ref _running, true);
            _focus.FocusChanged += FocusChanged; _input.KeyEvent += KeyEvent;
        }
    }

    internal void SettingsChanged(ExpressionAssistFeatureSettings settings)
    {
        _dispatcher.VerifyAccess();
        lock (_gate)
        {
            if (_disposed || _settings == settings) return;
            _settings = settings; Interlocked.Increment(ref _epoch);
        }
        CancelProbe(); _observer.Cancel(); _session?.Invalidate();
    }

    private void KeyEvent(object? sender, KeyEventArgs args) => HandleKey(args);

    internal void HandleKey(KeyEventArgs args)
    {
        var key = args.Event;
        if (!key.IsKeyDown || args.Suppress) return;
        long epoch; FocusedField? field; ExpressionAssistFeatureSettings settings;
        lock (_gate)
        {
            if (!_running || _disposed || _active) return;
            settings = _settings; field = _field;
            if (field is null || !FieldAvailable(field)) return;
            bool shortcut = settings.Enabled && settings.Available && settings.ShortcutKey != 0
                && settings.ShortcutKey is not (0x0D or 0x1B) && settings.ShortcutModifiers != KeyModifiers.None
                && key.VirtualKey == settings.ShortcutKey && key.Modifiers == settings.ShortcutModifiers
                && field is { IsSecure: false } && CompositionEvidencePolicy.IsClear(field);
            if (shortcut) { try { shortcut = !_shortcutConflict(field, settings); } catch { shortcut = false; } }
            if (shortcut)
            {
                args.Suppress = true;
                if (key.IsRepeat) return;
                epoch = Interlocked.Increment(ref _epoch);
                _dispatcher.BeginInvoke(() => BeginIfCurrent(null, key.VirtualKey, field, epoch));
                return;
            }
            if (!settings.Enabled || !settings.Available || key.Kind is not (KeyKind.Character or KeyKind.Backspace or KeyKind.Delete or KeyKind.Enter)) return;
            epoch = Interlocked.Increment(ref _epoch);
        }
        string characters = key.Kind == KeyKind.Character && key.Character != '\0' ? key.Character.ToString() : "";
        _dispatcher.BeginInvoke(() => Mutation(field, characters, settings, epoch));
    }

    private void FocusChanged(object? sender, FocusedField? field)
    {
        long epoch; bool changed;
        lock (_gate)
        {
            if (!_running || _disposed) return;
            changed = !_active && (_field?.ProcessId != field?.ProcessId || _field?.FieldId != field?.FieldId
                || _field?.FocusChangeSequence != field?.FocusChangeSequence || field?.IsSecure == true);
            _field = field;
            epoch = changed ? Interlocked.Increment(ref _epoch) : _epoch;
        }
        _dispatcher.BeginInvoke(() =>
        {
            if (!Current(epoch)) return;
            if (changed) { CancelProbe(); _observer.Cancel(); }
            Observe(field, epoch);
        });
    }

    private void Mutation(FocusedField? field, string characters, ExpressionAssistFeatureSettings settings, long epoch)
    {
        if (!Current(epoch) || field is null || !FieldAvailable(field) || _session?.IsActive == true) return;
        CancelProbe();
        var armed = _observer.Mutation(field, characters, settings.Trigger, Now());
        _observerEpoch = armed.Epoch;
        if (!armed.Probe) return;
        var source = new CancellationTokenSource(); _probe = source;
        Track(ProbeAsync(epoch, armed.Epoch, source));
    }

    private async Task ProbeAsync(long epoch, long observerEpoch, CancellationTokenSource source)
    {
        try
        {
            for (int i = 0; i < 4; i++)
            {
                source.Token.ThrowIfCancellationRequested();
                if (!Current(epoch) || _session?.IsActive == true) return;
                // Native calls this under its own locks: do not acquire our gate or invoke UI here.
                bool Permit() => Volatile.Read(ref _running) && !Volatile.Read(ref _disposed)
                    && Interlocked.Read(ref _epoch) == epoch && !source.IsCancellationRequested;
                var field = await Task.Run(() => Permit() ? _guardedRefresh(Permit) : null);
                if (!Current(epoch) || source.IsCancellationRequested) return;
                // Refresh publishes Current but does not necessarily raise FocusChanged. Admit
                // only the very snapshot still owned by the focus tracker after the async read.
                lock (_gate)
                    if (_epoch == epoch && field is not null && ReferenceEquals(_focus.Current, field)) _field = field;
                var trigger = _observer.Observe(field, observerEpoch, Now());
                if (trigger is not null) { BeginIfCurrent(trigger, null, field, epoch); return; }
                if (i != 3) await Task.Delay(35, source.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_probe, source)) _probe = null;
            _probeCancellations.Remove(source, out var cancellation);
            Track(DisposeProbeAsync(source, cancellation ?? Task.CompletedTask));
        }
    }

    private void Observe(FocusedField? field, long epoch)
    {
        if (!Current(epoch)) return;
        if (_session is { IsActive: true } session)
        {
            if (!session.HasInputFocus) session.FocusLost();
            return;
        }
        var trigger = _observer.Observe(field, _observerEpoch, Now());
        if (trigger is not null) BeginIfCurrent(trigger, null, field, epoch);
    }

    private void BeginIfCurrent(string? trigger, int? initiatingKey, FocusedField? field, long epoch)
    {
        if (!Current(epoch) || field is null || field.IsSecure || !FieldAvailable(field)
            || !CompositionEvidencePolicy.IsClear(field) || _session?.IsActive == true) return;
        lock (_gate)
        {
            var current = _focus.Current;
            if (!_settings.Enabled || !_settings.Available || _field is null || _field.ProcessId != field.ProcessId
                || _field.FieldId != field.FieldId || _field.FocusChangeSequence != field.FocusChangeSequence
                || !CompositionEvidencePolicy.IsSameReceipt(field, _field) || current is null || current.IsSecure
                || current.ProcessId != field.ProcessId || current.FieldId != field.FieldId
                || current.FocusChangeSequence != field.FocusChangeSequence || !CompositionEvidencePolicy.IsSameReceipt(field, current)) return;
            Interlocked.Increment(ref _epoch); _active = true;
        }
        CancelProbe(); _observer.Cancel();
        ExpressionAssistSession session; Action close;
        try { (session, close) = _factory(field); }
        catch { lock (_gate) _active = false; return; }
        _session = session; _close = close;
        session.ActivityChanged += ActivityChanged;
        try
        {
            session.Begin(trigger, initiatingKey);
            if (ReferenceEquals(_session, session) && !session.IsActive) RetireSession(session);
        }
        catch { RetireSession(session); }
    }

    private void ActivityChanged(bool active)
    {
        lock (_gate) _active = active;
        if (!active && _session is { } session) RetireSession(session);
    }
    private void RetireSession(ExpressionAssistSession session)
    {
        if (!ReferenceEquals(_session, session)) return;
        session.ActivityChanged -= ActivityChanged; _session = null;
        var close = _close; _close = null;
        lock (_gate) _active = false;
        Track(DrainSessionAsync(session, close));
    }
    private static async Task DrainSessionAsync(ExpressionAssistSession session, Action? close)
    {
        // An activity callback can occur inside session.Invalidate; defer disposal until that
        // call has finished retiring its own capture and cancellation sources.
        await Dispatcher.Yield(DispatcherPriority.Background);
        try { await session.DisposeAsync(); } finally { close?.Invoke(); }
    }
    private bool Current(long epoch) { lock (_gate) return _running && !_disposed && _epoch == epoch; }
    private bool FieldAvailable(FocusedField field)
    { try { return _fieldAvailable(field); } catch { return false; } }
    private static TimeSpan Now() => Stopwatch.GetElapsedTime(0);
    private void CancelProbe()
    {
        var source = _probe; _probe = null;
        if (source is not null) _probeCancellations[source] = source.CancelAsync();
    }
    private static async Task DisposeProbeAsync(CancellationTokenSource source, Task cancellation)
    { try { await cancellation.ConfigureAwait(false); } finally { source.Dispose(); } }
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; _dispatcher.BeginInvoke(() => _work.Remove(t)); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public ValueTask DisposeAsync()
    {
        _dispatcher.VerifyAccess();
        if (_disposal is not null) return new(_disposal);
        lock (_gate)
        {
            Volatile.Write(ref _disposed, true); Volatile.Write(ref _running, false); Interlocked.Increment(ref _epoch);
            _focus.FocusChanged -= FocusChanged; _input.KeyEvent -= KeyEvent;
        }
        CancelProbe(); _observer.Cancel();
        _session?.Invalidate();
        return new(_disposal = DrainAsync());
    }
    private async Task DrainAsync()
    {
        while (_work.Count != 0)
        {
            var work = _work.ToArray();
            try { await Task.WhenAll(work); } catch { }
            foreach (var task in work) _work.Remove(task);
        }
    }
}
