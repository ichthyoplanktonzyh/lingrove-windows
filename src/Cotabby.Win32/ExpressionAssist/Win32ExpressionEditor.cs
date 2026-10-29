using System.Runtime.Versioning;
using System.Windows.Automation;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Win32.Clipboard;
using Cotabby.Win32.Focus;
using Cotabby.Win32.Insertion;
using Cotabby.Win32.Interop;

namespace Cotabby.Win32.ExpressionAssist;

/// <summary>
/// Request-owned editor capture and one-shot write adapter. It borrows the app's focus, keyboard,
/// UIA composition owner and insertion gate; it never creates a global listener or stores text.
/// </summary>
[SupportedOSPlatform("windows8.1")]
public sealed class Win32ExpressionEditor : IAsyncDisposable
{
    private readonly UiaFocusTracker _focus;
    private readonly IKeyboardHook _keyboard;
    private readonly InsertionTransactionGate _gate;
    private readonly ClipboardTextReader? _clipboard;
    private readonly object _sync = new();
    private readonly HashSet<Task> _work = [];
    private ExpressionEditorSession? _active;
    private bool _disposed;
    private Task? _disposeTask;

    public Win32ExpressionEditor(UiaFocusTracker focus, IKeyboardHook keyboard, InsertionTransactionGate gate, ClipboardTextReader? clipboard = null)
    {
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _clipboard = clipboard;
    }

    /// <summary>
    /// Panel HWND must already exist and belong to this process; opening it remains the UI's
    /// responsibility. The keyboard-ownership callback must be a nonblocking thread-safe snapshot
    /// because focus and keyboard notifications can arrive on native callback threads.
    /// </summary>
    public async Task<ExpressionEditorSession?> CaptureAsync(string? trigger, nint panelHwnd,
        Func<bool> panelOwnsKeyboard, int? initiatingVirtualKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(panelOwnsKeyboard);
        if (trigger is not null && (trigger.Length == 0 || trigger.Any(c => c < 0x20 || c > 0x7e)))
            throw new NotSupportedException("This trigger cannot be safely replaced in this Windows editor. Use an ASCII trigger or the shortcut.");
        if (panelHwnd == 0 || NativeMethods.GetWindowThreadProcessId(panelHwnd, out uint panelPid) == 0
            || panelPid != (uint)Environment.ProcessId || _focus.ExpressionCompositionOwner is not { } composition) return null;
        ExpressionEditorSession session;
        Task<bool> work;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active is not null) { _active.Retire(); Track(_active.DisposeAsync().AsTask()); }
            session = new(_focus, _keyboard, composition, _gate, _clipboard, panelHwnd, panelOwnsKeyboard, initiatingVirtualKey);
            session.NotificationGroup = this;
            _active = session;
            work = Task.Run(() => session.Capture(trigger, ct), CancellationToken.None);
            session.Track(work);
            Track(work);
        }
        return await PublishCaptureAsync(session, work, ct).ConfigureAwait(false);
    }

    // The optional barrier is a test-only seam for the completed-work/publication interval.
    internal async Task<ExpressionEditorSession?> PublishCaptureAsync(ExpressionEditorSession session, Task<bool> work,
        CancellationToken ct, Func<Task>? beforePublish = null)
    {
        try
        {
            if (await work.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false))
            {
                if (beforePublish is not null) await beforePublish().ConfigureAwait(false);
                lock (_sync)
                {
                    if (!_disposed && ReferenceEquals(_active, session) && session.IsPublishable && !ct.IsCancellationRequested)
                        return session;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
        session.Retire();
        return null;
    }

    internal void Track(Task task)
    {
        lock (_sync) _work.Add(task);
        _ = task.ContinueWith(t => { lock (_sync) _work.Remove(t); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                var active = _active;
                active?.Retire();
                var work = _work.ToArray();
                // Begin on another thread so an Invalidated callback cannot cause the shared
                // drain to omit itself. External callers always join the complete drain.
                _disposeTask = Task.Run(() => DrainAsync(active, work));
            }
            // A synchronous notification callback cannot wait for the drain that includes it.
            return ExpressionEditorSession.IsNotificationFor(this) ? ValueTask.CompletedTask : new(_disposeTask);
        }
    }

    private static async Task DrainAsync(ExpressionEditorSession? active, Task[] work)
    {
        Task[] all = active is null ? work : [.. work, active.DisposeAsync().AsTask()];
        try { await Task.WhenAll(all).ConfigureAwait(false); } catch { }
    }

    internal void AttachSessionForTest(ExpressionEditorSession session)
    {
        lock (_sync) { session.NotificationGroup = this; _active = session; }
    }
}

/// <summary>Owns one exact capture until dismissal or one attempted replacement.</summary>
[SupportedOSPlatform("windows8.1")]
public sealed class ExpressionEditorSession : IAsyncDisposable
{
    private enum Phase { Capturing, Captured, Panel, Returning, Dispatching, Retired }
    private readonly object _sync = new();
    private readonly UiaFocusTracker _focus;
    private readonly IKeyboardHook _keyboard;
    private readonly IExpressionRetainedEvidence _composition;
    private readonly InsertionTransactionGate _gate;
    private readonly IExpressionSessionEffects _effects;
    private readonly nint _panel;
    private readonly Func<bool> _panelOwnsKeyboard;
    private readonly int? _initiatingKey;
    private Phase _phase = Phase.Capturing;
    private bool _initialKeyUpAvailable = true;
    private bool _returnFocusEvent;
    private bool _panelFocusEvent;
    private int _retiredFlag;
    private int _capturedFlag;
    private nint _host;
    private AutomationElement? _element;
    private long _retainedToken;
    private long _observationVersion;
    private ExpressionEditorReceipt? _receipt;
    private ExpressionEditorRoundTrip? _roundTrip;
    private readonly HashSet<Task> _work = [];
    private Task? _notificationTask;
    [ThreadStatic] private static ExpressionEditorSession? _notificationOwner;
    internal object? NotificationGroup { get; set; }
    internal static bool IsNotificationFor(object owner) => ReferenceEquals(_notificationOwner?.NotificationGroup, owner);

    internal ExpressionEditorSession(UiaFocusTracker focus, IKeyboardHook keyboard, UiaTextEditEvidence composition,
        InsertionTransactionGate gate, ClipboardTextReader? clipboard, nint panel, Func<bool> panelOwnsKeyboard, int? initiatingKey)
        : this(focus, keyboard, composition, gate, new Win32ExpressionSessionEffects(clipboard, composition), panel, panelOwnsKeyboard, initiatingKey)
    { }

    internal ExpressionEditorSession(UiaFocusTracker focus, IKeyboardHook keyboard, IExpressionRetainedEvidence composition,
        InsertionTransactionGate gate, IExpressionSessionEffects effects, nint panel, Func<bool> panelOwnsKeyboard, int? initiatingKey)
    {
        _focus = focus; _keyboard = keyboard; _composition = composition; _gate = gate;
        _effects = effects; _panel = panel;
        _panelOwnsKeyboard = panelOwnsKeyboard; _initiatingKey = initiatingKey;
        _focus.FocusTransition += OnFocusTransition;
        _keyboard.KeyEvent += OnKey;
    }

    // Internal-only deterministic seam: all subsequent validation and dispatch ordering remains
    // the real session code, while tests supply fake native effects and an owned receipt.
    internal void SetCapturedForTest(ExpressionEditorReceipt receipt, nint host, long token)
    {
        lock (_sync)
        {
            if (_phase != Phase.Capturing) throw new InvalidOperationException();
            _receipt = receipt; _host = host; _retainedToken = token;
            _observationVersion = receipt.Original.ObservationVersion;
            _roundTrip = new(receipt, host, _panel, Environment.ProcessId);
            _phase = Phase.Captured;
            Volatile.Write(ref _capturedFlag, 1);
        }
    }

    public ExpressionEditorReceipt Receipt => _receipt ?? throw new InvalidOperationException("Capture is unavailable.");
    public event EventHandler? Invalidated;

    /// <summary>Checks whether the captured host is still foreground before showing the panel.</summary>
    /// <remarks>This is a metadata-only preflight, not authorization for an edit. The session can
    /// still be invalidated immediately after this check; callers must observe Invalidated.</remarks>
    public bool CanPresentPanel => IsPublishable && _host != 0 && _effects.Foreground == _host && IsPublishable;
    /// <summary>Metadata-only feedback gate, also usable after a one-shot edit retires the receipt.</summary>
    public bool IsHostForeground => _host != 0 && _effects.Foreground == _host;

    internal bool Capture(string? trigger, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!IsLive()) return false;
            nint host = _effects.Foreground;
            bool RequestAllowed() => !ct.IsCancellationRequested && IsLive() && _effects.Foreground == host;
            var first = _focus.RefreshGuarded(RequestAllowed);
            if (ct.IsCancellationRequested || !IsLive()) return false;
            if (!EligibleFocus(first) || host == 0 || host == _panel || first!.ProcessId == Environment.ProcessId) return false;
            if (_effects.Foreground != host || NativeMethods.GetWindowThreadProcessId(host, out uint pid) == 0 || pid != (uint)first.ProcessId) return false;
            if (!RequestAllowed()) return false;
            var element = AutomationElement.FocusedElement;
            if (ct.IsCancellationRequested || !IsLive() || _effects.Foreground != host || element is null) return false;
            if (UiaFocusTracker.ReadFieldId(element) != first.FieldId || ct.IsCancellationRequested || !IsLive() || _effects.Foreground != host
                || element.Current.ProcessId != first.ProcessId) return false;
            if (ct.IsCancellationRequested || !IsLive() || _effects.Foreground != host) return false;
            long? token = _composition.Register(first.ProcessId, first.FieldId, host, _panel, Retire, RequestAllowed, ct);
            if (token is null) return false;
            lock (_sync)
            {
                if (_phase != Phase.Capturing) { _composition.Retire(token.Value); return false; }
                _host = host; _element = element; _retainedToken = token.Value;
            }
            var before = _composition.Observe(token.Value, () => false, ct);
            if (before.State != InputCompositionState.Clear) return false;
            bool Allowed() => !ct.IsCancellationRequested && _effects.Foreground == host
                && _composition.IsCurrent(token.Value, before.Version) && IsLive();
            var one = _effects.Read(element, token.Value, Allowed);
            if (!Allowed()) return false;
            var two = _effects.Read(element, token.Value, Allowed);
            if (!Allowed()) return false;
            var after = _composition.Observe(token.Value, () => false, ct);
            if (ct.IsCancellationRequested || !IsLive()) return false;
            var last = _focus.RefreshGuarded(RequestAllowed);
            if (ct.IsCancellationRequested || one is null || one != two || before != after || !SameFocus(first, last)
                || _effects.Foreground != host || first.HasSelection != (one.Value.Selection.Length > 0)
                || first.CaretOffset != one.Value.Selection.Start) return false;
            var observation = new ExpressionEditorObservation(first.ProcessId, first.FieldId, first.FocusChangeSequence,
                Interlocked.Increment(ref _observationVersion), one.Value.Value, one.Value.Selection, false, true, before.State, before.Version);
            var receipt = ExpressionEditorReceiptPolicy.Capture(observation, trigger);
            if (receipt is null) return false;
            lock (_sync)
            {
                if (_phase != Phase.Capturing || ct.IsCancellationRequested) return false;
                _receipt = receipt; _roundTrip = new(receipt, host, _panel, Environment.ProcessId); _phase = Phase.Captured;
                Volatile.Write(ref _capturedFlag, 1);
                return true;
            }
        }
        catch (Exception) { return false; }
    }

    public bool EnterPanel()
    {
        lock (_sync)
        {
            if (_phase != Phase.Captured || !PanelOwnsInput() || _roundTrip?.EnterPanel(_panel, Environment.ProcessId, true) != true)
            { Retire(); return false; }
            Volatile.Write(ref _capturedFlag, 0);
            _phase = Phase.Panel;
            return true;
        }
    }

    public async Task<bool> IsUnchangedAsync(CancellationToken ct)
    {
        Task<bool> task;
        lock (_sync)
        {
            if (_phase != Phase.Panel) return false;
            task = Task.Run(() => ReadPanelReceipt(ct), CancellationToken.None);
            Track(task);
        }
        try { return await task.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { Retire(); return false; }
    }

    private bool ReadPanelReceipt(CancellationToken ct)
    {
        try
        {
            lock (_sync) if (_phase != Phase.Panel || !PanelOwnsInput()) { Retire(); return false; }
            var observation = ReadRetained(true, ct);
            lock (_sync)
            {
                bool valid = _phase == Phase.Panel && PanelOwnsInput() && observation is not null
                    && ExpressionEditorReceiptPolicy.MatchesOriginal(Receipt, observation);
                if (!valid) Retire();
                return valid;
            }
        }
        catch (Exception) { Retire(); return false; }
    }

    /// <summary>Call while the registered panel still owns input, before it is hidden.</summary>
    public bool BeginReturn()
    {
        lock (_sync)
        {
            if (_phase != Phase.Panel || !PanelOwnsInput() || _roundTrip?.BeginReturn(_panel, Environment.ProcessId, true) != true)
            { Retire(); return false; }
            _phase = Phase.Returning;
        }
        if (_effects.Foreground != _panel || !_effects.Activate(_host)) { Retire(); return false; }
        return true;
    }

    /// <summary>
    /// Recaptures the original host twice, then takes the shared edit gate for one final read and
    /// one input batch. A failed or unverified dispatch is never retried or rolled back.
    /// </summary>
    public Task<ExpressionEditOutcome> ResumeAndReplaceAsync(string replacement, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_phase != Phase.Returning) return Task.FromResult(ExpressionEditOutcome.Rejected);
            var task = ResumeAndReplaceCoreAsync(replacement, ct);
            Track(task);
            return task;
        }
    }

    private async Task<ExpressionEditOutcome> ResumeAndReplaceCoreAsync(string replacement, CancellationToken ct)
    {
        if (!InsertionTargetPolicy.IsValidInsertionText(replacement)) { Retire(); return ExpressionEditOutcome.Rejected; }
        try
        {
            lock (_sync) if (_phase != Phase.Returning) return ExpressionEditOutcome.Rejected;
            bool confirmed = false;
            for (int i = 0; i < 12 && !confirmed; i++)
            {
                ct.ThrowIfCancellationRequested();
                var current = await ReadReturnedAsync(ct).ConfigureAwait(false);
                if (current is not null)
                {
                    lock (_sync)
                    {
                        if (_phase != Phase.Returning) return ExpressionEditOutcome.Rejected;
                        confirmed = _roundTrip!.ObserveReturnedHost(_host, current.Value.Observation);
                    }
                }
                if (!confirmed) await Task.Delay(35, ct).ConfigureAwait(false);
            }
            if (!confirmed) { Retire(); return ExpressionEditOutcome.Rejected; }
            return await _gate.RunAsync(token => ReplaceUnderGateAsync(replacement, token), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { Retire(); return ExpressionEditOutcome.Rejected; }
        catch (Exception) { Retire(); return ExpressionEditOutcome.Unverified; }
    }

    private async Task<ExpressionEditOutcome> ReplaceUnderGateAsync(string replacement, CancellationToken ct)
    {
        IClipboardPasteLease? lease = null;
        bool mayHavePasted = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var current = await ReadReturnedAsync(ct).ConfigureAwait(false);
            if (current is null) return ExpressionEditOutcome.Rejected;
            var saved = Receipt;
            var plan = ExpressionReplacementPlan.Create(saved, replacement);
            if (plan is null) return ExpressionEditOutcome.Rejected;

            if (!SyntheticKeys.CanType(replacement))
            {
                lease = await _effects.PreparePasteAsync(replacement, ct).ConfigureAwait(false);
                if (lease is null || !lease.IsCurrent || ct.IsCancellationRequested) return ExpressionEditOutcome.Rejected;
                current = await ReadReturnedAsync(ct).ConfigureAwait(false);
                if (current is null || !lease.IsCurrent) return ExpressionEditOutcome.Rejected;
            }

            lock (_sync)
            {
                if (_phase != Phase.Returning || ct.IsCancellationRequested || _roundTrip!.Consume(_host, current.Value.Observation) is null)
                    return ExpressionEditOutcome.Rejected;
                _phase = Phase.Dispatching;
            }
            if (ct.IsCancellationRequested) return ExpressionEditOutcome.Rejected;
            bool dispatched;
            bool StillAuthorized()
            {
                lock (_sync) return _phase == Phase.Dispatching && !ct.IsCancellationRequested
                    && _effects.Foreground == _host
                    && _composition.IsCurrent(_retainedToken, current.Value.Observation.CompositionSequence)
                    && SameFocus(current.Value.Field, _focus.Current);
            }
            if (lease is null) dispatched = _effects.SendUnicode(current.Value.Field, plan.Backspaces, replacement, StillAuthorized);
            else
            {
                mayHavePasted = true;
                dispatched = _effects.SendPaste(current.Value.Field, plan.Backspaces, () => lease.IsCurrent && StillAuthorized());
            }
            if (!dispatched) return ExpressionEditOutcome.Unverified;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(60, ct).ConfigureAwait(false);
                var verification = Task.Run(() => IsExpectedValue(current.Value.Observation, plan.ExpectedValue, ct), CancellationToken.None);
                Track(verification);
                bool verified = await verification.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                if (verified) return ExpressionEditOutcome.Verified;
            }
            return ExpressionEditOutcome.Unverified;
        }
        catch (OperationCanceledException) { return ExpressionEditOutcome.Unverified; }
        catch (Exception) { return ExpressionEditOutcome.Unverified; }
        finally
        {
            try
            {
                if (lease is not null)
                {
                    if (mayHavePasted) { try { await Task.Delay(80).ConfigureAwait(false); } catch { } }
                    try { await lease.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception) { throw new InvalidOperationException("Clipboard restoration was not confirmed."); }
                }
            }
            finally { Retire(); }
        }
    }

    private (ExpressionEditorObservation Observation, FocusedField Field)? ReadReturned(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !IsLive() || _effects.Foreground != _host) return null;
        bool Allowed() => !ct.IsCancellationRequested && IsLive() && _effects.Foreground == _host;
        var first = _focus.RefreshGuarded(Allowed);
        if (ct.IsCancellationRequested || !IsLive()) return null;
        if (!EligibleFocus(first) || first!.ProcessId != Receipt.Original.ProcessId || first.FieldId != Receipt.Original.FieldId) return null;
        var observation = ReadRetained(false, ct);
        if (ct.IsCancellationRequested || !IsLive()) return null;
        var last = _focus.RefreshGuarded(Allowed);
        if (observation is null || !SameFocus(first, last) || observation.FocusEpoch != first.FocusChangeSequence
            || first.CaretOffset != observation.Selection.Start || first.HasSelection != (observation.Selection.Length > 0)
            || _effects.Foreground != _host || !ExpressionEditorReceiptPolicy.MatchesOriginal(Receipt, observation)) return null;
        return (observation, first);
    }

    private async Task<(ExpressionEditorObservation Observation, FocusedField Field)?> ReadReturnedAsync(CancellationToken ct)
    {
        var task = Task.Run(() => ReadReturned(ct), CancellationToken.None);
        Track(task);
        return await task.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
    }

    private bool IsExpectedValue(ExpressionEditorObservation captured, string expected, CancellationToken ct)
    {
        try
        {
            bool Allowed() => !ct.IsCancellationRequested && IsLive() && _effects.Foreground == _host;
            if (!Allowed()) return false;
            var first = _focus.RefreshGuarded(Allowed);
            if (!IsLive()) return false;
            if (!EligibleFocus(first) || first!.ProcessId != captured.ProcessId || first.FieldId != captured.FieldId
                || first.FocusChangeSequence != captured.FocusEpoch) return false;
            var value = _effects.Read(_element, _retainedToken, () => !ct.IsCancellationRequested && _effects.Foreground == _host
                && IsLive()
                && _focus.Current is { } latest && latest.ProcessId == captured.ProcessId && latest.FieldId == captured.FieldId
                && latest.FocusChangeSequence == captured.FocusEpoch);
            if (!IsLive()) return false;
            var second = _focus.RefreshGuarded(Allowed);
            return value is not null && string.Equals(value.Value.Value, expected, StringComparison.Ordinal)
                && SameFocus(first, second) && _effects.Foreground == _host;
        }
        catch (Exception) { return false; }
    }

    private ExpressionEditorObservation? ReadRetained(bool panel, CancellationToken ct)
    {
        if (_retainedToken <= 0 || ct.IsCancellationRequested || !IsLive()) return null;
        if (panel && !PanelOwnsInput()) return null;
        var before = _composition.Observe(_retainedToken, () => panel && PanelOwnsInput(), ct);
        if (before.State != InputCompositionState.Clear) return null;
        bool Allowed() => !ct.IsCancellationRequested && IsLive()
            && _composition.IsCurrent(_retainedToken, before.Version)
            && (panel ? PanelOwnsInput() : _effects.Foreground == _host);
        var read = _effects.Read(_element, _retainedToken, Allowed);
        if (ct.IsCancellationRequested || !IsLive()) return null;
        var after = _composition.Observe(_retainedToken, () => panel && PanelOwnsInput(), ct);
        if (read is null || before != after || ct.IsCancellationRequested || (panel && !PanelOwnsInput())) return null;
        var original = Receipt.Original;
        long focusEpoch = panel ? original.FocusEpoch : _focus.Current?.FocusChangeSequence ?? 0;
        return new(original.ProcessId, original.FieldId, focusEpoch, Interlocked.Increment(ref _observationVersion),
            read.Value.Value, read.Value.Selection, false, true, before.State, before.Version);
    }

    private static bool EligibleFocus(FocusedField? field) => field is { ProcessId: > 0, FocusChangeSequence: > 0,
        HasReliableSelection: true, IsTextWindowTruncated: false, IsSecure: false, Composition: InputCompositionState.Clear, CompositionSequence: > 0 }
        && !string.IsNullOrWhiteSpace(field.FieldId) && !CredentialFieldDetector.IsCredentialField(field);

    private static bool SameFocus(FocusedField? a, FocusedField? b) => EligibleFocus(a) && EligibleFocus(b)
        && a!.ProcessId == b!.ProcessId && a.FieldId == b.FieldId && a.FocusChangeSequence == b.FocusChangeSequence
        && a.CompositionSequence == b.CompositionSequence && a.CaretOffset == b.CaretOffset && a.HasSelection == b.HasSelection
        && string.Equals(a.Text, b.Text, StringComparison.Ordinal);

    private bool PanelOwnsInput()
    {
        try { return _effects.Foreground == _panel && _panelOwnsKeyboard(); }
        catch (Exception) { return false; }
    }

    private bool IsLive() => Volatile.Read(ref _retiredFlag) == 0;
    internal bool IsPublishable => Volatile.Read(ref _capturedFlag) != 0 && IsLive();

    private void OnKey(object? sender, KeyEventArgs e)
    {
        lock (_sync)
        {
            if (_phase == Phase.Retired) return;
            if (_phase == Phase.Dispatching) { Retire(); return; }
            var decision = ExpressionInputLifetimePolicy.Classify(e.Event, _phase == Phase.Capturing,
                _initialKeyUpAvailable, _initiatingKey, (_phase is Phase.Panel or Phase.Captured) && PanelOwnsInput());
            if (decision == ExpressionInputLifetimePolicy.Decision.ConsumeInitialKeyUpAllowance) _initialKeyUpAvailable = false;
            else if (decision == ExpressionInputLifetimePolicy.Decision.Retire) Retire();
        }
    }

    private void OnFocusTransition(nint foreground)
    {
        lock (_sync)
        {
            if (_phase == Phase.Retired) return;
            var phase = _phase switch
            {
                Phase.Capturing => ExpressionFocusTransitionPolicy.Phase.Capturing,
                Phase.Captured => ExpressionFocusTransitionPolicy.Phase.Captured,
                Phase.Panel => ExpressionFocusTransitionPolicy.Phase.Panel,
                Phase.Returning => ExpressionFocusTransitionPolicy.Phase.Returning,
                _ => ExpressionFocusTransitionPolicy.Phase.Dispatching,
            };
            var decision = ExpressionFocusTransitionPolicy.Classify(phase, foreground, _host, _panel,
                _panelFocusEvent, _returnFocusEvent, _phase == Phase.Panel && PanelOwnsInput());
            if (decision == ExpressionFocusTransitionPolicy.Decision.PanelTransition) _panelFocusEvent = true;
            else if (decision == ExpressionFocusTransitionPolicy.Decision.HostTransition) _returnFocusEvent = true;
            else Retire();
        }
    }

    public void Retire()
    {
        Interlocked.Exchange(ref _retiredFlag, 1);
        bool notify;
        lock (_sync)
        {
            notify = _phase != Phase.Retired;
            _phase = Phase.Retired; _roundTrip?.Retire();
            if (_retainedToken > 0) _composition.Retire(_retainedToken);
            if (notify)
            {
                _notificationTask = Task.Run(() =>
                {
                    _notificationOwner = this;
                    try { Invalidated?.Invoke(this, EventArgs.Empty); }
                    catch (Exception) { }
                    finally { _notificationOwner = null; }
                });
                Track(_notificationTask);
            }
        }
        if (!notify) return;
        _focus.FocusTransition -= OnFocusTransition;
        _keyboard.KeyEvent -= OnKey;
    }

    internal void Track(Task task)
    {
        lock (_sync) _work.Add(task);
        _ = task.ContinueWith(t => { lock (_sync) _work.Remove(t); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        Retire();
        bool calledFromNotification = ReferenceEquals(_notificationOwner, this);
        while (true)
        {
            Task[] pending;
            lock (_sync) pending = _work.Where(task => !calledFromNotification || !ReferenceEquals(task, _notificationTask)).ToArray();
            if (pending.Length == 0)
            {
                await _composition.DrainAsync().ConfigureAwait(false);
                lock (_sync) { _element = null; _receipt = null; _roundTrip = null; }
                return;
            }
            try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { }
        }
    }

}

/// <summary>Only Verified reports observed resulting text; Unverified must not be retried automatically.</summary>
public enum ExpressionEditOutcome { Rejected, Unverified, Verified }
