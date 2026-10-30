using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Focus;
using Cotabby.Win32.Text;
using Native = Cotabby.Win32.Interop.NativeMethods;

namespace Cotabby.Win32.Reading;

/// <summary>Focused-field evidence only; this value does not authorize pixel capture for a region.</summary>
public sealed record ReadingScreenGuardEvidence(int ProcessId, string FieldId, long FocusEpoch, long CompositionSequence, ScreenRect FieldBounds)
{
    public ReadingVisualOrigin? WindowOrigin { get; init; }
}
public sealed record ReadingScreenPixels(ReadingBitmap Bitmap, ScreenRect DisplayBounds);

/// <summary>Request-scoped selected-text capture. It borrows the existing focus, keyboard and UIA owners.</summary>
[SupportedOSPlatform("windows8.1")]
public sealed class Win32ReadingSelectionReader : IAsyncDisposable
{
    private readonly UiaFocusTracker _focus;
    private readonly IKeyboardHook _keyboard;
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private Win32ReadingSelectionLease? _active;
    private bool _disposed;
    private Task? _dispose;

    public Win32ReadingSelectionReader(UiaFocusTracker focus, IKeyboardHook keyboard)
    { _focus = focus ?? throw new ArgumentNullException(nameof(focus)); _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard)); }

    public Task<Win32ReadingSelectionLease?> CaptureAsync(CancellationToken cancellationToken, int? initiatingVirtualKey = null) =>
        CaptureCoreAsync(null, false, cancellationToken, initiatingVirtualKey);
    public Task<Win32ReadingSelectionLease?> CapturePointerAsync(int x, int y, CancellationToken cancellationToken, int? initiatingVirtualKey = null) =>
        CaptureCoreAsync(new System.Drawing.Point(x, y), false, cancellationToken, initiatingVirtualKey);
    public Task<Win32ReadingSelectionLease?> CaptureScreenGuardAsync(CancellationToken cancellationToken, int? initiatingVirtualKey = null) =>
        CaptureCoreAsync(null, true, cancellationToken, initiatingVirtualKey);
    private async Task<Win32ReadingSelectionLease?> CaptureCoreAsync(System.Drawing.Point? pointer, bool screenGuard, CancellationToken cancellationToken, int? initiatingVirtualKey)
    {
        if (_focus.ExpressionCompositionOwner is not { } owner) return null;
        long epoch = _focus.ActiveFocusEpoch;
        nint host = Native.GetForegroundWindow();
        if (epoch <= 0 || host == 0 || Native.GetWindowThreadProcessId(host, out uint pid) == 0
            || pid == 0 || pid == (uint)Environment.ProcessId || pid > int.MaxValue) return null;
        Win32ReadingSelectionLease lease;
        Task<bool> work;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active is { } previous) { previous.Retire(); Track(previous.DisposeAsync().AsTask()); }
            lease = new(_focus, _keyboard, owner, host, (int)pid, epoch, initiatingVirtualKey, pointer: pointer, screenGuard: screenGuard);
            lease.NotificationGroup = this;
            _active = lease;
            work = Task.Run(() => lease.Capture(cancellationToken), CancellationToken.None);
            lease.Track(work); Track(work);
        }
        try
        {
            if (await work.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false))
            {
                lock (_gate) if (!_disposed && ReferenceEquals(_active, lease) && lease.IsLive && !cancellationToken.IsCancellationRequested) return lease;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { }
        lease.Retire();
        return null;
    }

    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { lock (_gate) _work.Remove(t); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public void ArmPendingActivationKey(int virtualKey)
    {
        lock (_gate) _active?.ArmActivationKey(virtualKey);
    }

    internal void AttachForTest(Win32ReadingSelectionLease lease)
    {
        lock (_gate) { lease.NotificationGroup = this; _active = lease; }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_dispose is null)
            {
                _disposed = true;
                var lease = _active;
                lease?.Retire();
                var tasks = _work.ToArray();
                _dispose = Task.Run(async () =>
                {
                    try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
                    if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
                });
            }
            return Win32ReadingSelectionLease.InNotificationFor(this) ? ValueTask.CompletedTask : new(_dispose);
        }
    }
}

/// <summary>A read-only occurrence receipt. No native pointer or write authority escapes this owner.</summary>
[SupportedOSPlatform("windows8.1")]
public sealed class Win32ReadingSelectionLease : IAsyncDisposable
{
    private readonly UiaFocusTracker _focus;
    private readonly IKeyboardHook _keyboard;
    private readonly IReadingSelectionEvidence _owner;
    private readonly Func<long> _currentEpoch;
    private readonly Func<nint> _foreground;
    private readonly Func<nint, int> _windowProcess;
    private readonly nint _host;
    private readonly int _pid;
    private readonly long _epoch;
    private readonly int? _initialKey;
    private readonly System.Drawing.Point? _pointer;
    private readonly bool _screenGuard;
    internal readonly record struct HostFrame(int Left, int Top, int Right, int Bottom);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(nint parent, nint child);
    private static HostFrame? WindowFrame(nint window) => GetWindowRect(window, out var rect)
        && rect.Right > rect.Left && rect.Bottom > rect.Top ? new(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    private readonly Func<nint, HostFrame?> _currentFrame;
    private readonly Func<nint, nint, bool> _isChild;
    private readonly HostFrame? _initialFrame;
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private UiaTextEditEvidence.ReadingLease? _native;
    private ReadingSelection? _selection;
    private int _retired, _capturing = 1, _initialKeyUp = 1;
    private int _activationDown, _activationUp;
    private Action? _invalidated;
    private bool _disposing;
    private Task? _disposal;
    private Task? _enrichment;
    private bool _screenCaptureStarted;
    internal Func<Task>? BeforeValidationPublicationForTest;
    internal Func<nint, nint, ScreenRect, ScreenRect, Func<bool>, (ReadingBitmap Bitmap, ScreenRect Display)?>? CapturePixelsForTest;
    internal Action? RetireCommittedForTest;
    [ThreadStatic] private static Win32ReadingSelectionLease? _notifying;
    internal object? NotificationGroup;
    internal static bool InNotificationFor(object group) => ReferenceEquals(_notifying?.NotificationGroup, group);
    /// <summary>Nonblocking receipt eligibility, independent of notification delivery.</summary>
    public bool IsLive => Volatile.Read(ref _retired) == 0 && _currentEpoch() == _epoch
        && _foreground() == _host && _windowProcess(_host) == _pid
        && (_initialFrame is { } frame ? _currentFrame(_host) == frame : _pointer is null && !_screenGuard)
        && (Volatile.Read(ref _capturing) != 0 || (Volatile.Read(ref _native) is { } native && Volatile.Read(ref native.Active) != 0
            && (!_screenGuard || (native.FieldWindow != 0 && (native.FieldWindow == _host || _isChild(_host, native.FieldWindow)) && _windowProcess(native.FieldWindow) == _pid
                && _currentFrame(native.FieldWindow) == new HostFrame(native.FieldLeft, native.FieldTop, native.FieldRight, native.FieldBottom)))));

    internal Win32ReadingSelectionLease(UiaFocusTracker focus, IKeyboardHook keyboard, IReadingSelectionEvidence owner,
        nint host, int pid, long epoch, int? initialKey, Func<long>? currentEpoch = null,
        Func<nint>? foreground = null, Func<nint, int>? windowProcess = null, System.Drawing.Point? pointer = null, bool screenGuard = false,
        Func<nint, HostFrame?>? currentFrame = null, Func<nint, nint, bool>? isChild = null)
    {
        _focus = focus; _keyboard = keyboard; _owner = owner; _host = host; _pid = pid; _epoch = epoch; _initialKey = initialKey; _pointer = pointer; _screenGuard = screenGuard;
        _currentEpoch = currentEpoch ?? (() => _focus.ActiveFocusEpoch);
        _currentFrame = currentFrame ?? WindowFrame;
        _isChild = isChild ?? IsChild;
        _initialFrame = _currentFrame(host);
        _foreground = foreground ?? (() => Native.GetForegroundWindow());
        _windowProcess = windowProcess ?? (hwnd => Native.GetWindowThreadProcessId(hwnd, out uint process) == 0 ? 0 : (int)process);
        _focus.FocusTransition += OnFocus;
        _keyboard.KeyEvent += OnKey;
    }

    public ReadingSelection Selection => _selection ?? throw new InvalidOperationException("Selection is unavailable.");
    public int? PendingActivationKey => Volatile.Read(ref _activationUp) is > 0 and var up ? up
        : Volatile.Read(ref _activationDown) is > 0 and var down ? down
        : Volatile.Read(ref _initialKeyUp) == 1 ? _initialKey : null;
    public ReadingScreenGuardEvidence? ScreenGuardEvidence
    {
        get
        {
            lock (_gate)
            {
                var native = _native;
                return _screenGuard && native is not null && IsLive
                    ? new(_pid, native.FieldId, _epoch, Interlocked.Read(ref native.Version),
                        new ScreenRect(native.FieldLeft, native.FieldTop, (double)native.FieldRight - native.FieldLeft, (double)native.FieldBottom - native.FieldTop)) : null;
            }
        }
    }
    /// <summary>Late subscriptions replay the terminal invalidation until disposal begins.</summary>
    public event Action? Invalidated
    {
        add
        {
            if (value is null) return;
            lock (_gate)
            {
                if (_disposing) return;
                if (_retired == 0) _invalidated += value;
                else QueueNotification(value);
            }
        }
        remove { lock (_gate) _invalidated -= value; }
    }

    private bool Allowed(CancellationToken ct) => !ct.IsCancellationRequested && IsLive;

    internal bool Capture(CancellationToken ct)
    {
        try
        {
            var native = _screenGuard ? _owner.CaptureScreenGuard(_host, _pid, () => Allowed(ct), Retire) : _pointer is { } point
                ? _owner.CapturePointer(_host, _pid, point, () => Allowed(ct), Retire)
                : _owner.CaptureReading(_host, _pid, () => Allowed(ct), Retire);
            if (native is null) return false;
            if (!Allowed(ct)) { _owner.RetireReading(native); return false; }
            var capturedAt = DateTimeOffset.UtcNow;
            lock (_gate)
            {
                if (!Allowed(ct)) { _owner.RetireReading(native); return false; }
                _native = native;
                if (_screenGuard) { Volatile.Write(ref _capturing, 0); return IsLive; }
                _selection = new(Guid.NewGuid(), native.Text, _pid, native.FieldId, _epoch, null, native.Text.Length,
                    false, true, InputCompositionState.Clear, native.Version,
                    _pointer is { } p ? new ScreenRect(p.X, p.Y, 1, 1) : ScreenRect.Empty, SelectionAnchorId: native.Anchor)
                    { CapturedAt = capturedAt, Source = _pointer is null ? ReadingSourceKind.Selection : ReadingSourceKind.Pointer, SelectionBounds = native.SelectionBounds,
                        OriginWindowBounds = _initialFrame is { } frame ? new(frame.Left, frame.Top, (double)frame.Right - frame.Left, (double)frame.Bottom - frame.Top) : null };
                ReadingSelectionPolicy.ValidateReceipt(_selection);
                Volatile.Write(ref _capturing, 0);
                return IsLive;
            }
        }
        catch (Exception) { return false; }
    }

    /// <summary>Reads surrounding context only after an explicit action. The retained occurrence
    /// and its original capture time remain authoritative; timeout never triggers another read.</summary>
    public async Task EnrichAsync(CancellationToken ct)
    {
        Task task;
        lock (_gate)
        {
            if (_native is not { } native || _selection is not { } selection || !Allowed(ct)) return;
            if (_enrichment is null)
            {
                long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * 0.18);
                bool Permit() => Allowed(ct) && System.Diagnostics.Stopwatch.GetTimestamp() < deadline;
                _enrichment = Task.Run(() =>
                {
                    ReadingContext? context = null;
                    try
                    {
                        if (Permit() && _owner.ReadReadingContext(native, Permit) is { } window && Permit()
                            && ReadingContextPolicy.Align(window, selection.Text) is { } aligned)
                            context = ReadingContextPolicy.Resolve(aligned, selection.Text, new IcuLearningEvidenceSentenceTokenizer().TokenizeUnknown);
                    }
                    catch (Exception) { /* Unsupported context remains unavailable. */ }
                    lock (_gate)
                        if (Permit() && ReferenceEquals(_native, native) && ReferenceEquals(_selection, selection))
                            _selection = selection with { Context = context ?? selection.Context };
                }, CancellationToken.None);
                Track(_enrichment);
            }
            task = _enrichment;
        }
        try { await task.WaitAsync(TimeSpan.FromMilliseconds(180), ct).ConfigureAwait(false); }
        catch (TimeoutException) { /* The tracked MTA work is joined during disposal. */ }
    }

    /// <summary>Bind observation-only gesture metadata to this exact receipt, without native reads.</summary>
    public void BindGesture(ReadingSelectionGesture gesture)
    {
        lock (_gate)
            if (IsLive && _selection is { Source: ReadingSourceKind.Selection } selection && gesture.SelectionId == selection.Id)
                _selection = selection with { Gesture = gesture };
    }

    /// <summary>Captures one explicitly selected crop only after the field and its retained
    /// evidence are current. The returned bytes are request-owned and must be cleared by the caller.</summary>
    public Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposing || !_screenGuard || _screenCaptureStarted || !Allowed(ct)) return Task.FromResult<ReadingScreenPixels?>(null);
            _screenCaptureStarted = true;
            var operation = CaptureRegionCoreAsync(region, ct); Track(operation); return operation;
        }
    }
    private async Task<ReadingScreenPixels?> CaptureRegionCoreAsync(ScreenRect region, CancellationToken ct)
    {
        if (!_screenGuard || !await IsCurrentAsync(ct).ConfigureAwait(false)) return null;
        Task<(ReadingBitmap Bitmap, ScreenRect Display)?> task;
        lock (_gate)
        {
            if (_native is not { } native || ScreenGuardEvidence is not { } proof || !Allowed(ct)
                || !ReadingRegionPolicy.Contains(proof.FieldBounds, proof.FieldBounds, region)) return null;
            long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + 2 * System.Diagnostics.Stopwatch.Frequency;
            bool Permit() => Allowed(ct) && System.Diagnostics.Stopwatch.GetTimestamp() < deadline;
            task = Task.Run(() => (CapturePixelsForTest ?? WindowsReadingPixels.Capture)(_host, native.FieldWindow, proof.FieldBounds, region, Permit));
            Track(task);
        }
        (ReadingBitmap Bitmap, ScreenRect Display)? result = null; bool transferred = false;
        try
        {
            result = await task.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            if (result is null) return null;
            if (!ReadingRegionPolicy.Contains(ScreenGuardEvidence?.FieldBounds ?? ScreenRect.Empty, result.Value.Display, region)
                || !Allowed(ct) || !await IsCurrentAsync(ct).ConfigureAwait(false) || !Allowed(ct)) return null;
            var accepted = new ReadingScreenPixels(result.Value.Bitmap, result.Value.Display);
            transferred = true; return accepted;
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            if (result is null) lock (_gate) Track(DiscardPixelsAsync(task));
            return null;
        }
        finally { if (!transferred && result is { } pixels) Array.Clear(pixels.Bitmap.Bgra); }
    }
    private static async Task DiscardPixelsAsync(Task<(ReadingBitmap Bitmap, ScreenRect Display)?> task)
    { try { if (await task.ConfigureAwait(false) is { } pixels) Array.Clear(pixels.Bitmap.Bgra); } catch { } }

    public async Task<bool> IsCurrentAsync(CancellationToken ct)
    {
        Task<bool> task;
        lock (_gate)
        {
            if (_native is not { } native || !Allowed(ct)) { Retire(); return false; }
            task = Task.Run(() => _owner.IsReadingCurrent(native, () => Allowed(ct)), CancellationToken.None);
            Track(task);
        }
        try
        {
            bool valid = await task.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            if (BeforeValidationPublicationForTest is { } barrier) await barrier().ConfigureAwait(false);
            valid = valid && Allowed(ct);
            if (!valid) Retire();
            return valid;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException) { Retire(); return false; }
    }

    private void OnFocus(nint _) => Retire();
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Event.IsKeyDown && e.Suppress && e.Event.VirtualKey != 0
            && Interlocked.CompareExchange(ref _activationDown, 0, e.Event.VirtualKey) == e.Event.VirtualKey)
        { Volatile.Write(ref _activationUp, e.Event.VirtualKey); return; }
        if (!e.Event.IsKeyDown && e.Event.VirtualKey != 0
            && Interlocked.CompareExchange(ref _activationUp, 0, e.Event.VirtualKey) == e.Event.VirtualKey) return;
        // Modifier releases do not commit text. In particular, the modifiers of the explicit
        // reading chord are normally released after its primary key and must not retire the picker.
        if (!e.Event.IsKeyDown && e.Event.VirtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return;
        if (!e.Event.IsKeyDown && _initialKey == e.Event.VirtualKey
            && Interlocked.Exchange(ref _initialKeyUp, 0) == 1) return;
        Retire();
    }
    public void ArmActivationKey(int virtualKey)
    {
        if (!_screenGuard && _pointer is null && virtualKey > 0 && IsLive)
            Interlocked.CompareExchange(ref _activationDown, virtualKey, 0);
    }

    public void Retire()
    {
        lock (_gate)
        {
            if (_retired != 0) return;
            Volatile.Write(ref _retired, 1);
            RetireCommittedForTest?.Invoke();
            var native = _native;
            var handlers = _invalidated;
            _invalidated = null;
            Task cleanup = Task.Run(() =>
            {
                _focus.FocusTransition -= OnFocus;
                _keyboard.KeyEvent -= OnKey;
                if (native is not null) _owner.RetireReading(native);
            });
            Track(cleanup);
            if (handlers is not null) QueueNotification(handlers);
        }
    }

    // Caller holds _gate, so registration and disposal observe the same tracked work.
    private void QueueNotification(Action handlers)
    {
        Task notification = Task.Run(() =>
        {
            _notifying = this;
            try { foreach (Action handler in handlers.GetInvocationList()) { try { handler(); } catch { } } }
            finally { _notifying = null; }
        });
        Track(notification);
    }

    internal void Track(Task task)
    {
        lock (_gate) _work.Add(task);
        _ = task.ContinueWith(t => { lock (_gate) _work.Remove(t); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public ValueTask DisposeAsync()
    {
        Retire();
        lock (_gate)
        {
            _disposing = true;
            _disposal ??= Task.Run(DrainAsync);
            return ReferenceEquals(_notifying, this) ? ValueTask.CompletedTask : new(_disposal);
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_gate) pending = _work.ToArray();
            if (pending.Length == 0) break;
            try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { }
        }
        await _owner.DrainRetainedAsync().ConfigureAwait(false);
        lock (_gate) { _native = null; _selection = null; }
    }
}
