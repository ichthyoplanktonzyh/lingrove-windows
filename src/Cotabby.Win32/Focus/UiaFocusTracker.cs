using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Cotabby.Core.Focus;
using Cotabby.Win32.Interop;
using Microsoft.Extensions.Logging;

namespace Cotabby.Win32.Focus;

/// <summary>
/// UI Automation implementation of <see cref="IFocusTracker"/>.
/// </summary>
/// <remarks>
/// Focus events arrive on a thread-pool thread, so consumers that touch UI must switch to their
/// own <see cref="SynchronizationContext"/>. A read against an element can throw at any time,
/// for example when the host process exits, so every read is guarded and falls back to null.
/// </remarks>
public sealed class UiaFocusTracker : IFocusTracker
{
    private readonly ILogger<UiaFocusTracker> _logger;
    private readonly object _gate = new();
    private readonly ICompositionEvidenceOwner? _composition;
    private readonly Action<AutomationFocusChangedEventHandler> _addHandler;
    private readonly Action<AutomationFocusChangedEventHandler> _removeHandler;
    private readonly Func<long, FocusedField?>? _captureForTest;

    private AutomationFocusChangedEventHandler? _handler;
    private FocusedField? _current;
    private long _focusChangeSequence;
    private long _captureSequence;
    private long _startGeneration;
    private long _compositionNotificationSequence;
    private int _compositionRefreshQueued;
    private bool _running;
    private bool _disposed;

    public UiaFocusTracker(ILogger<UiaFocusTracker> logger)
    {
        _logger = logger;
        _addHandler = Automation.AddAutomationFocusChangedEventHandler;
        _removeHandler = Automation.RemoveAutomationFocusChangedEventHandler;
        if (OperatingSystem.IsWindowsVersionAtLeast(8, 1))
            _composition = new UiaTextEditEvidence(OnCompositionChanged, OnCompositionFailure);
    }

    internal UiaFocusTracker(ILogger<UiaFocusTracker> logger, ICompositionEvidenceOwner composition,
        Action<AutomationFocusChangedEventHandler> addHandler,
        Action<AutomationFocusChangedEventHandler> removeHandler, Func<long, FocusedField?> capture)
    {
        _logger = logger;
        _composition = composition;
        _addHandler = addHandler;
        _removeHandler = removeHandler;
        _captureForTest = capture;
    }

    public FocusedField? Current
    {
        get
        {
            var current = Volatile.Read(ref _current);
            if (current is { CompositionSequence: > 0 }
                && _composition?.Version != current.CompositionSequence)
                return null;
            return current;
        }
    }

    public event EventHandler<FocusedField?>? FocusChanged;
    // Synchronous, metadata-only revocation signal for request owners. Unlike the asynchronously
    // refreshed snapshot event, this cannot hide an intervening focus transition after a round trip.
    internal event Action<nint>? FocusTransition;
    // Read-only selection leases borrow this existing focus epoch without requiring an editable snapshot.
    internal long ActiveFocusEpoch => Volatile.Read(ref _running) ? Interlocked.Read(ref _focusChangeSequence) : 0;

    // The expression session borrows this same MTA owner; no second global listener is created.
    internal UiaTextEditEvidence? ExpressionCompositionOwner =>
        OperatingSystem.IsWindowsVersionAtLeast(8, 1) ? _composition as UiaTextEditEvidence : null;

    public void Start()
    {
        AutomationFocusChangedEventHandler handler;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running) return;
            _running = true;
            Interlocked.Increment(ref _focusChangeSequence);

            handler = OnFocusChanged;
            _handler = handler;
            generation = ++_startGeneration;
        }
        if (_composition?.Execute(() =>
        {
            bool current;
            lock (_gate) current = _running && _startGeneration == generation && ReferenceEquals(_handler, handler);
            if (current) _addHandler(handler);
        }) != true)
        {
            Stop(generation);
            return;
        }
        lock (_gate) { if (!_running || _startGeneration != generation) return; }
        _logger.LogInformation("UIA focus tracker started.");
        Refresh();
        // Metadata-only consumers need an initial origin even when focus never changes after Start.
        try { FocusTransition?.Invoke(NativeMethods.GetForegroundWindow()); }
        catch (Exception) { _logger.LogWarning("Focus transition notification failed."); }
    }

    public void Stop() => Stop(null);

    private void Stop(long? expectedGeneration)
    {
        AutomationFocusChangedEventHandler? handler;
        long retirementEpoch;
        lock (_gate)
        {
            if (!_running || (expectedGeneration.HasValue && expectedGeneration.Value != _startGeneration)) return;
            _running = false;
            ++_startGeneration;
            Interlocked.Increment(ref _focusChangeSequence);
            retirementEpoch = _composition?.Invalidate() ?? 0;
            handler = _handler;
            _handler = null;
            Volatile.Write(ref _current, null);
            _logger.LogInformation("UIA focus tracker stopped.");
        }
        try { FocusTransition?.Invoke(0); }
        catch (Exception) { _logger.LogWarning("Focus transition notification failed."); }
        if (handler is not null)
        {
            _composition?.RetireAsync(retirementEpoch);
            _composition?.Execute(() => _removeHandler(handler));
        }
    }

    public FocusedField? Refresh() => RefreshCore(static () => true, false);

    /// <summary>Refreshes while a revocable request permit remains valid.</summary>
    /// <remarks>The predicate must be atomic and nonblocking. It must not call UIA or synchronously
    /// dispatch to a UI thread. Already-started provider calls cannot be interrupted.</remarks>
    public FocusedField? RefreshGuarded(Func<bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        return RefreshCore(allowed, true);
    }

    private FocusedField? RefreshCore(Func<bool> allowed, bool bounded)
    {
        long capture, sequence;
        lock (_gate)
        {
            if (!_running || !allowed()) return null;
            capture = Interlocked.Increment(ref _captureSequence);
            sequence = Interlocked.Read(ref _focusChangeSequence);
        }
        FocusedField? snap;
        if (_captureForTest is not null) snap = _captureForTest(sequence);
        else TryCaptureFocused(sequence, allowed, bounded, out snap);
        lock (_gate)
        {
            // UIA reads cross processes and can finish after a newer focus event. Publishing
            // that old read would make an unrelated field look current to an insertion task.
            if (!allowed() || !_running || sequence != Interlocked.Read(ref _focusChangeSequence)
                || capture != Interlocked.Read(ref _captureSequence)) return null;
            if (snap is { CompositionSequence: > 0 } && _composition?.Version != snap.CompositionSequence)
                snap = snap with { Composition = InputCompositionState.Unknown, CompositionSequence = 0 };
            Volatile.Write(ref _current, snap);
            return snap;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _composition?.Dispose();
    }

    internal void OnCompositionChanged()
    {
        lock (_gate)
        {
            if (!_running) return;
            ++_compositionNotificationSequence;
            Volatile.Write(ref _current, null);
        }
        // Never run consumer code from the COM callback; the receipt was already retired.
        QueueCompositionRefresh();
    }

    internal void OnCompositionFailure()
    {
        lock (_gate)
        {
            if (!_running) return;
            Volatile.Write(ref _current, null);
        }
        // A failed poll invalidates work but is not a new native composition event and must not
        // schedule another poll. The next real event or user action may attempt a fresh capture.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { FocusChanged?.Invoke(this, null); }
            catch (Exception) { _logger.LogWarning("Composition failure notification failed."); }
        });
    }

    private void QueueCompositionRefresh()
    {
        if (Interlocked.Exchange(ref _compositionRefreshQueued, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ => RefreshComposition());
    }

    private void RefreshComposition()
    {
        long handledNotification = -1;
        try
        {
            try { FocusChanged?.Invoke(this, null); }
            catch (Exception) { _logger.LogWarning("Composition change notification failed."); }
            while (true)
            {
                long notification, focus;
                lock (_gate)
                {
                    if (!_running) return;
                    notification = _compositionNotificationSequence;
                    handledNotification = notification;
                    focus = _focusChangeSequence;
                }
                var snap = Refresh();
                lock (_gate)
                {
                    if (!_running || focus != _focusChangeSequence) return;
                    if (notification != _compositionNotificationSequence) continue;
                    if (snap is null || !ReferenceEquals(snap, _current)) return;
                }
                FocusChanged?.Invoke(this, snap);
                return;
            }
        }
        catch (Exception) { _logger.LogWarning("Composition refresh failed."); }
        finally
        {
            Interlocked.Exchange(ref _compositionRefreshQueued, 0);
            // Do not lose an event that arrived between the last read and clearing the queued bit.
            lock (_gate)
            {
                if (_running && _compositionNotificationSequence != handledNotification) QueueCompositionRefresh();
            }
        }
    }

    private void OnFocusChanged(object? sender, AutomationFocusChangedEventArgs e)
    {
        long eventSequence;
        long retirementEpoch;
        lock (_gate)
        {
            if (!_running) return;
            eventSequence = Interlocked.Increment(ref _focusChangeSequence);
            retirementEpoch = _composition?.Invalidate() ?? 0;
            Volatile.Write(ref _current, null);
        }
        try { FocusTransition?.Invoke(NativeMethods.GetForegroundWindow()); }
        catch (Exception) { _logger.LogWarning("Focus transition notification failed."); }
        _composition?.RetireAsync(retirementEpoch);
        ThreadPool.QueueUserWorkItem(_ => PublishFocusChange(eventSequence));
    }

    private void PublishFocusChange(long eventSequence)
    {
        try
        {
            FocusChanged?.Invoke(this, null);
            var snap = Refresh();
            if (eventSequence != Interlocked.Read(ref _focusChangeSequence)) return;
            // Focus on a field that is not editable ends the session, so no ghost text is left behind.
            if (snap is null)
            {
                FocusChanged?.Invoke(this, null);
                return;
            }

            // Every focus event is passed on. Listeners compare FieldId to tell a new field.
            FocusChanged?.Invoke(this, snap);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Focus change handling failed.");
        }
    }

    private bool TryCaptureFocused(long sequence, Func<bool> allowed, bool bounded, out FocusedField? snapshot)
    {
        snapshot = null;
        AutomationElement? element;
        try
        {
            if (!allowed()) return false;
            element = AutomationElement.FocusedElement;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AutomationElement.FocusedElement threw.");
            return false;
        }
        if (element is null || !allowed()) return false;

        try
        {
            if (!IsEditableField(element, allowed)) return false;
            snapshot = BuildSnapshot(element, sequence, allowed, bounded);
            return snapshot is not null;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Snapshot build failed.");
            return false;
        }
    }

    private static bool IsEditableField(AutomationElement element, Func<bool> allowed)
    {
        try
        {
            if (!allowed()) return false;
            var info = element.Current;
            if (!allowed() || !info.IsEnabled || !allowed()) return false;
            // IsKeyboardFocusable is not checked. The Windows 11 Notepad document control reports
            // false and still accepts keystrokes.

            var ct = info.ControlType;
            if (!allowed()) return false;
            if (ct != ControlType.Edit && ct != ControlType.Document && ct != ControlType.Pane)
            {
                return false;
            }

            // Read-only fields get no suggestions. Not every editor exposes ValuePattern.
            if (!allowed()) return false;
            bool hasReadOnlyPattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw);
            if (!allowed()) return false;
            if (hasReadOnlyPattern && raw is ValuePattern vp && vp.Current.IsReadOnly)
            {
                return false;
            }

            // Without TextPattern or ValuePattern there is no text to read.
            if (!allowed()) return false;
            bool hasText = element.TryGetCurrentPattern(TextPattern.Pattern, out _);
            if (!allowed()) return false;
            bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
            return allowed() && (hasText || hasValue);
        }
        catch (Exception) { return false; }
    }

    private FocusedField? BuildSnapshot(AutomationElement element, long sequence, Func<bool> allowed, bool bounded)
    {
        if (!allowed()) return null;
        var info = element.Current;
        if (!allowed()) return null;
        int pid = info.ProcessId;
        if (!allowed()) return null;
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero
            || NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid) == 0
            || foregroundPid != (uint)pid) return null;
        string procName = TryGetProcessName(pid);
        if (!allowed()) return null;
        string label = info.Name ?? string.Empty;
        if (!allowed()) return null;
        string helpText = info.HelpText ?? string.Empty;
        if (!allowed()) return null;
        bool isPassword = info.IsPassword;
        if (!allowed()) return null;
        string localizedType = isPassword ? string.Empty : info.LocalizedControlType;
        bool isSecure = isPassword || SecureFieldDetector.IsSecure(label, helpText, localizedType);
        if (!allowed()) return null;

        // Password-like controls are classified before requesting their text. They still publish
        // an ineligible snapshot so consumers can close an existing capture without reading secrets.
        string fieldId = ReadFieldId(element);
        if (!allowed()) return null;
        var initialComposition = !isSecure && _composition is not null
            ? _composition.Observe(pid, fieldId, sequence, allowed) : default;
        if (!allowed()) return null;
        var (text, caretOffset, hasSelection, reliableSelection, truncatedWindow, selectionLength, supportsRangeReplacement) = isSecure
            ? (string.Empty, 0, false, false, true, 0, false)
            : ReadTextAndCaret(pid, fieldId, allowed);
        if (!allowed()) return null;
        var caretRect = isSecure ? ScreenRect.Empty : ReadCaretRect(element, allowed);
        if (!allowed()) return null;
        var fieldRect = ToScreenRect(info.BoundingRectangle);
        if (!allowed()) return null;
        var finalComposition = !isSecure && _composition is not null
            ? _composition.Observe(pid, fieldId, sequence, allowed) : default;
        if (!allowed()) return null;
        finalComposition = CompositionEvidencePolicy.Reconcile(initialComposition, finalComposition);
        var controlType = info.ControlType;
        if (!allowed()) return null;
        string automationId = info.AutomationId ?? string.Empty;
        if (!allowed()) return null;
        string className = info.ClassName ?? string.Empty;
        if (!allowed()) return null;

        // UIA can lag behind foreground activation. A cross-process read which straddled that
        // transition cannot establish the destination of a future SendInput batch.
        if (NativeMethods.GetForegroundWindow() != foreground) return null;

        return new FocusedField
        {
            ProcessId = pid,
            ProcessName = procName,
            Text = text,
            CaretOffset = caretOffset,
            CaretRect = caretRect.IsEmpty ? fieldRect : caretRect,
            CaretIsEstimated = caretRect.IsEmpty,
            FieldRect = fieldRect,
            IsSingleLine = FieldShape.IsSingleLine(controlType == ControlType.Edit, fieldRect, caretRect),
            IsSecure = isSecure,
            HasSelection = hasSelection,
            SelectionLength = selectionLength,
            SupportsRangeReplacement = supportsRangeReplacement,
            HasReliableSelection = reliableSelection,
            Composition = finalComposition.State,
            CompositionSequence = finalComposition.Version,
            IsTextWindowTruncated = truncatedWindow,
            FocusChangeSequence = sequence,
            FieldId = fieldId,
            Label = label,
            HelpText = helpText,
            AutomationId = automationId,
            ClassName = className,
            WindowTitle = bounded ? string.Empty : ForegroundWindowTitle(),
        };
    }

    private (string text, int caretOffset, bool hasSelection, bool reliableSelection, bool truncatedWindow, int selectionLength, bool supportsRangeReplacement) ReadTextAndCaret(int pid, string fieldId, Func<bool> allowed)
    {
        if (!allowed()) return (string.Empty, 0, false, false, true, 0, false);
        // Range text is read by the shared MTA native client, after rechecking identity there.
        var selection = _composition?.ReadFocused(pid, fieldId, allowed);
        if (selection is { } value && allowed())
        {
            return (value.Text, value.Caret, value.HasSelection, value.ReliableSelection, value.Truncated, value.SelectionLength, value.SupportsRangeReplacement);
        }

        // ValuePattern has neither a bounded getter nor reliable caret evidence.
        return (string.Empty, 0, false, false, true, 0, false);
    }

    internal static string ReadFieldId(AutomationElement element)
    {
        int[]? runtimeId = null;
        try { runtimeId = element.GetRuntimeId(); }
        catch (Exception) { /* fall through */ }

        return runtimeId is { Length: > 0 }
            ? string.Join('.', runtimeId)
            // AutomationId/control type can be identical in two fields. An absent runtime ID
            // stays unknown so the write policy can reject it rather than fabricate identity.
            : string.Empty;
    }

    private static ScreenRect ReadCaretRect(AutomationElement element, Func<bool> allowed)
    {
        if (!allowed()) return ScreenRect.Empty;
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var raw)
            && raw is TextPattern textPattern)
        {
            try
            {
                if (!allowed()) return ScreenRect.Empty;
                var sel = textPattern.GetSelection();
                if (!allowed()) return ScreenRect.Empty;
                if (sel is { Length: > 0 })
                {
                    if (!allowed()) return ScreenRect.Empty;
                    var rects = sel[0].GetBoundingRectangles();
                    if (rects.Length > 0)
                    {
                        return ToScreenRect(rects[^1]);
                    }
                }
            }
            catch (Exception) { /* fall through */ }
        }
        return ScreenRect.Empty;
    }

    private static ScreenRect ToScreenRect(System.Windows.Rect r) =>
        new(r.X, r.Y, r.Width, r.Height);

    private static unsafe string ForegroundWindowTitle()
    {
        const int MaxTitleChars = 256;
        char* buffer = stackalloc char[MaxTitleChars];
        int length = NativeMethods.GetWindowText(NativeMethods.GetForegroundWindow(), buffer, MaxTitleChars);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception) { return string.Empty; }
    }
}
