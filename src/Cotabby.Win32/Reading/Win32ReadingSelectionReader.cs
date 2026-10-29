using System.Runtime.Versioning;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Focus;
using Native = Cotabby.Win32.Interop.NativeMethods;

namespace Cotabby.Win32.Reading;

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

    public async Task<Win32ReadingSelectionLease?> CaptureAsync(CancellationToken cancellationToken, int? initiatingVirtualKey = null)
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
            lease = new(_focus, _keyboard, owner, host, (int)pid, epoch, initiatingVirtualKey);
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
    private readonly object _gate = new();
    private readonly HashSet<Task> _work = [];
    private UiaTextEditEvidence.ReadingLease? _native;
    private ReadingSelection? _selection;
    private int _retired, _capturing = 1, _initialKeyUp = 1;
    private Action? _invalidated;
    private bool _disposing;
    private Task? _disposal;
    internal Func<Task>? BeforeValidationPublicationForTest;
    internal Action? RetireCommittedForTest;
    [ThreadStatic] private static Win32ReadingSelectionLease? _notifying;
    internal object? NotificationGroup;
    internal static bool InNotificationFor(object group) => ReferenceEquals(_notifying?.NotificationGroup, group);
    /// <summary>Nonblocking receipt eligibility, independent of notification delivery.</summary>
    public bool IsLive => Volatile.Read(ref _retired) == 0 && _currentEpoch() == _epoch
        && _foreground() == _host && _windowProcess(_host) == _pid
        && (Volatile.Read(ref _capturing) != 0 || (Volatile.Read(ref _native) is { } native && Volatile.Read(ref native.Active) != 0));

    internal Win32ReadingSelectionLease(UiaFocusTracker focus, IKeyboardHook keyboard, IReadingSelectionEvidence owner,
        nint host, int pid, long epoch, int? initialKey, Func<long>? currentEpoch = null,
        Func<nint>? foreground = null, Func<nint, int>? windowProcess = null)
    {
        _focus = focus; _keyboard = keyboard; _owner = owner; _host = host; _pid = pid; _epoch = epoch; _initialKey = initialKey;
        _currentEpoch = currentEpoch ?? (() => _focus.ActiveFocusEpoch);
        _foreground = foreground ?? (() => Native.GetForegroundWindow());
        _windowProcess = windowProcess ?? (hwnd => Native.GetWindowThreadProcessId(hwnd, out uint process) == 0 ? 0 : (int)process);
        _focus.FocusTransition += OnFocus;
        _keyboard.KeyEvent += OnKey;
    }

    public ReadingSelection Selection => _selection ?? throw new InvalidOperationException("Selection is unavailable.");
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
            var native = _owner.CaptureReading(_host, _pid, () => Allowed(ct), Retire);
            if (native is null) return false;
            if (!Allowed(ct)) { _owner.RetireReading(native); return false; }
            lock (_gate)
            {
                if (!Allowed(ct)) { _owner.RetireReading(native); return false; }
                _native = native;
                _selection = new(Guid.NewGuid(), native.Text, _pid, native.FieldId, _epoch, null, native.Text.Length,
                    false, true, InputCompositionState.Clear, native.Version, ScreenRect.Empty, SelectionAnchorId: native.Anchor);
                ReadingSelectionPolicy.ValidateReceipt(_selection);
                Volatile.Write(ref _capturing, 0);
                return IsLive;
            }
        }
        catch (Exception) { return false; }
    }

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
        if (!e.Event.IsKeyDown && Volatile.Read(ref _capturing) != 0 && _initialKey == e.Event.VirtualKey
            && Interlocked.Exchange(ref _initialKeyUp, 0) == 1) return;
        Retire();
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
