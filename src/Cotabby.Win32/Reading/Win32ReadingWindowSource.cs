using System.Runtime.Versioning;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Focus;

namespace Cotabby.Win32.Reading;

/// <summary>Explicit screenshot acquisition borrows the existing origin, keyboard and UIA MTA.
/// Window/password metadata is independent of editable-field/TextPattern support. This owner never
/// captures pixels until the caller explicitly requests a selected region or frozen pointer frame.</summary>
[SupportedOSPlatform("windows8.1")]
public sealed class Win32ReadingWindowSource : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<ReadingForeground?> _origin;
    private readonly Func<ReadingForeground, Func<bool>, ReadingScreenSecurity?> _security;
    private readonly Func<ReadingForeground, System.Drawing.Point, Func<bool>, ReadingScreenSecurity?>? _pointSecurity;
    private readonly Func<ReadingForeground, System.Drawing.Point, ReadingPointerWindow?>? _frame;
    private readonly Func<nint, ScreenRect?> _bounds;
    private readonly Func<ScreenRect> _desktop;
    private readonly Func<ScreenRect, ScreenRect, Func<bool>, ReadingScreenPixels?> _capture;
    private readonly IKeyboardHook _keyboard;
    private readonly Action<Action<nint>> _subscribe, _unsubscribe;
    private readonly HashSet<Task> _work = [];
    private Win32ReadingWindowLease? _active;
    private bool _closed;
    private Task? _disposal;

    public Win32ReadingWindowSource(UiaFocusTracker focus, IKeyboardHook keyboard, Win32ReadingOriginTracker origin)
        : this(() => origin.Current, keyboard, handler => focus.FocusTransition += handler, handler => focus.FocusTransition -= handler,
            (source, allowed) => focus.Current is { IsSecure: true } or { Composition: InputCompositionState.Composing } ? null
                : focus.ExpressionCompositionOwner?.ReadScreenSecurity(source.ProcessId, source.Window, allowed),
            ReadingWindowTopology.WindowBounds, ReadingWindowTopology.DesktopBounds,
            (bounds, region, allowed) => WindowsReadingPixels.CaptureExplicitRegion(bounds, region, allowed) is { } result
                ? new(result.Bitmap, result.Display) : null,
            (source, point, allowed) => focus.Current is { IsSecure: true } or { Composition: InputCompositionState.Composing } ? null
                : focus.ExpressionCompositionOwner?.ReadScreenSecurity(source.ProcessId, source.Window, allowed, point),
            ReadingWindowTopology.VisualFrameAt) { }

    internal Win32ReadingWindowSource(Func<ReadingForeground?> origin, IKeyboardHook keyboard,
        Action<Action<nint>> subscribe, Action<Action<nint>> unsubscribe,
        Func<ReadingForeground, Func<bool>, ReadingScreenSecurity?> security, Func<nint, ScreenRect?> bounds,
        Func<ScreenRect> desktop, Func<ScreenRect, ScreenRect, Func<bool>, ReadingScreenPixels?> capture,
        Func<ReadingForeground, System.Drawing.Point, Func<bool>, ReadingScreenSecurity?>? pointSecurity = null,
        Func<ReadingForeground, System.Drawing.Point, ReadingPointerWindow?>? frame = null)
    {
        _origin = origin; _keyboard = keyboard; _subscribe = subscribe; _unsubscribe = unsubscribe;
        _security = security; _bounds = bounds; _desktop = desktop; _capture = capture; _pointSecurity = pointSecurity; _frame = frame;
    }

    public Task<Win32ReadingWindowLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
        => CaptureCoreAsync(null, token, initiatingVirtualKey);
    public Task<Win32ReadingWindowLease?> CapturePointerAsync(int x, int y, CancellationToken token, int? initiatingVirtualKey = null)
        => CaptureCoreAsync(new(x, y), token, initiatingVirtualKey);
    private Task<Win32ReadingWindowLease?> CaptureCoreAsync(System.Drawing.Point? pointer, CancellationToken token, int? initiatingVirtualKey)
    {
        lock (_gate)
        {
            if (_closed || token.IsCancellationRequested || _origin() is not { ProcessId: > 0, FocusEpoch: > 0, Window: not 0 } source
                || source.ProcessId == Environment.ProcessId) return Task.FromResult<Win32ReadingWindowLease?>(null);
            if (_active is { } prior) { prior.Retire(); Track(prior.DisposeAsync().AsTask()); }
            var lease = new Win32ReadingWindowLease(source, _origin, _keyboard, _subscribe, _unsubscribe, _security, _bounds, _desktop, _capture,
                initiatingVirtualKey, pointer, _pointSecurity, _frame);
            _active = lease; var task = PublishAsync(lease, token, pointer is not null); Track(task); return task;
        }
    }
    private static async Task<Win32ReadingWindowLease?> PublishAsync(Win32ReadingWindowLease lease, CancellationToken token, bool pointer)
    {
        bool published = false;
        try
        {
            bool initialized = await lease.InitializeAsync(token).ConfigureAwait(false);
            if (pointer && (lease.IsRetired || token.IsCancellationRequested || initialized && !lease.IsLive)) throw new OperationCanceledException();
            if (initialized && lease.IsLive && !token.IsCancellationRequested) { published = true; return lease; }
            return null;
        }
        finally { if (!published) { lease.Retire(); await lease.DisposeAsync().ConfigureAwait(false); } }
    }
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; lock (_gate) _work.Remove(t); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _closed = true; var active = _active; active?.Retire();
            var work = _work.ToArray(); return new(_disposal = DrainAsync(active, work));
        }
    }
    private static async Task DrainAsync(Win32ReadingWindowLease? active, Task[] work)
    {
        try { try { await Task.WhenAll(work).ConfigureAwait(false); } catch { } }
        finally { if (active is not null) await active.DisposeAsync().ConfigureAwait(false); }
    }
}

/// <summary>A read-only origin-window lease. Native handles/password metadata stay here; portable
/// receipts carry an opaque origin ID and geometry, never fabricated caret/composition evidence.</summary>
public sealed class Win32ReadingWindowLease : IAsyncDisposable
{
    [ThreadStatic] private static Win32ReadingWindowLease? _notifying;
    private readonly object _gate = new();
    private readonly ReadingForeground _source;
    private readonly Func<ReadingForeground?> _origin;
    private readonly IKeyboardHook _keyboard;
    private readonly Action<Action<nint>> _unsubscribe;
    private readonly Func<ReadingForeground, Func<bool>, ReadingScreenSecurity?> _security;
    private readonly System.Drawing.Point? _pointer;
    private readonly Func<ReadingForeground, System.Drawing.Point, Func<bool>, ReadingScreenSecurity?>? _pointSecurity;
    private readonly Func<ReadingForeground, System.Drawing.Point, ReadingPointerWindow?>? _frame;
    private ReadingPointerWindow? _pointerWindow;
    private ScreenRect _originBounds;
    public ReadingVisualFrame? PointerFrame => _pointerWindow?.Frame;
    private readonly Func<nint, ScreenRect?> _bounds;
    private readonly Func<ScreenRect> _desktop;
    private readonly Func<ScreenRect, ScreenRect, Func<bool>, ReadingScreenPixels?> _capture;
    private readonly HashSet<Task> _work = [];
    private ReadingScreenSecurity? _securityReceipt;
    private ReadingScreenGuardEvidence? _evidence;
    private Action? _invalidated;
    private int _retired, _initialKeyUp = 1;
    private readonly int? _initialKey;
    private Task? _disposal;

    internal Win32ReadingWindowLease(ReadingForeground source, Func<ReadingForeground?> origin, IKeyboardHook keyboard,
        Action<Action<nint>> subscribe, Action<Action<nint>> unsubscribe,
        Func<ReadingForeground, Func<bool>, ReadingScreenSecurity?> security, Func<nint, ScreenRect?> bounds,
        Func<ScreenRect> desktop, Func<ScreenRect, ScreenRect, Func<bool>, ReadingScreenPixels?> capture, int? initialKey,
        System.Drawing.Point? pointer = null,
        Func<ReadingForeground, System.Drawing.Point, Func<bool>, ReadingScreenSecurity?>? pointSecurity = null,
        Func<ReadingForeground, System.Drawing.Point, ReadingPointerWindow?>? frame = null)
    {
        _source = source; _origin = origin; _keyboard = keyboard; _unsubscribe = unsubscribe; _security = security;
        _bounds = bounds; _desktop = desktop; _capture = capture; _initialKey = initialKey;
        _pointer = pointer; _pointSecurity = pointSecurity; _frame = frame;
        lock (_gate)
        {
            subscribe(OnFocus);
            if (_retired == 0) _keyboard.KeyEvent += OnKey;
            if (_retired != 0) { _unsubscribe(OnFocus); _keyboard.KeyEvent -= OnKey; }
        }
    }
    public ReadingScreenGuardEvidence? Evidence => Volatile.Read(ref _evidence);
    internal bool IsRetired => Volatile.Read(ref _retired) != 0;
    private bool OriginCurrent => Volatile.Read(ref _retired) == 0 && _origin() == _source;
    public bool IsLive => OriginCurrent && Evidence?.WindowOrigin is not null && _bounds(_source.Window) == _originBounds
        && (_pointer is not { } point || _pointerWindow is { } frozen && _frame?.Invoke(_source, point) == frozen);
    private ReadingScreenSecurity? ReadSecurity(Func<bool> allowed) => _pointer is { } point
        ? _pointSecurity?.Invoke(_source, point, allowed) : _security(_source, allowed);
    public event Action? Invalidated
    {
        add
        {
            lock (_gate)
            {
                if (_retired == 0) _invalidated += value;
                else if (value is not null) Notify(value);
            }
        }
        remove { lock (_gate) _invalidated -= value; }
    }
    internal Task<bool> InitializeAsync(CancellationToken token) => Run(() =>
    {
        bool Allowed() => OriginCurrent && !token.IsCancellationRequested;
        var window = _bounds(_source.Window); var desktop = _desktop();
        if (!Allowed() || window is not { } frame || !ReadingRegionPolicy.IsValidBounds(frame)
            || !ReadingRegionPolicy.IsValidBounds(desktop) || desktop.Width > 32768 || desktop.Height > 32768) return false;
        _originBounds = frame;
        var security = ReadSecurity(Allowed);
        if (!Allowed() || _bounds(_source.Window) != window) { if (_pointer is not null) Retire(); return false; }
        if (security is null) return false;
        if (_pointer is { } point)
        {
            _pointerWindow = _frame?.Invoke(_source, point);
            if (_pointerWindow is not { Handle: not 0 } pointed || PointerFrame is not { } frozen
                || !ReadingRegionPolicy.Contains(pointed.Bounds, frozen.Display, frozen.Bounds)
                || !ReadingVisualPolicy.Contains(frozen.Bounds, point.X, point.Y) || frozen.X != point.X || frozen.Y != point.Y) return false;
        }
        _securityReceipt = security;
        Volatile.Write(ref _evidence, new ReadingScreenGuardEvidence(_source.ProcessId, "", _source.FocusEpoch, 0, desktop)
            { WindowOrigin = new(Guid.NewGuid(), _pointerWindow?.Bounds ?? frame) });
        bool current = IsLive && Allowed();
        if (!current && _pointer is not null) Retire();
        return current;
    }, false);
    public Task<bool> IsCurrentAsync(CancellationToken token) => Run(() =>
    {
        bool Allowed() => IsLive && !token.IsCancellationRequested;
        bool current = Allowed() && ReadSecurity(Allowed) is { } security && security == _securityReceipt && Allowed();
        if (!current) Retire(); return current;
    }, false);
    public Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken token) => Run(() =>
    {
        bool Allowed() => IsLive && !token.IsCancellationRequested;
        var proof = Evidence;
        if (proof is null || !Allowed() || !ReadingRegionPolicy.Contains(proof.FieldBounds, proof.FieldBounds, region)) return null;
        if (_pointer is not null && (PointerFrame is not { } frozen || !ReadingVisualPolicy.Contains(frozen.Bounds, region))) return null;
        if (ReadSecurity(Allowed) != _securityReceipt || !Allowed()) { Retire(); return null; }
        var pixels = _capture(proof.FieldBounds, region, Allowed);
        if (pixels is null) return null;
        bool accepted = false;
        try
        {
            if (!Allowed() || !ReadingRegionPolicy.Contains(proof.FieldBounds, pixels.DisplayBounds, region)
                || pixels.Bitmap.Width != region.Width || pixels.Bitmap.Height != region.Height
                || pixels.Bitmap.Bgra.LongLength != (long)pixels.Bitmap.Width * pixels.Bitmap.Height * 4
                || ReadSecurity(Allowed) != _securityReceipt || !Allowed()) { Retire(); return null; }
            accepted = true; return pixels;
        }
        finally { if (!accepted) Array.Clear(pixels.Bitmap.Bgra); }
    }, (ReadingScreenPixels?)null);
    private Task<T> Run<T>(Func<T> operation, T rejected)
    {
        lock (_gate)
        {
            if (_retired != 0 || _disposal is not null) return Task.FromResult(rejected);
            var task = Task.Run(() => { try { return operation(); } catch { Retire(); return rejected; } });
            Track(task); return task;
        }
    }
    private void OnFocus(nint _) => Retire();
    private void OnKey(object? sender, KeyEventArgs args)
    {
        var key = args.Event;
        if (!key.IsKeyDown && key.VirtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return;
        if (!key.IsKeyDown && key.VirtualKey == _initialKey && Interlocked.Exchange(ref _initialKeyUp, 0) == 1) return;
        Retire();
    }
    public void Retire()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _retired, 1) != 0) return;
            _unsubscribe(OnFocus); _keyboard.KeyEvent -= OnKey;
            var handlers = _invalidated; _invalidated = null; if (handlers is not null) Notify(handlers);
        }
    }
    private void Notify(Action handlers)
    {
        var task = Task.Run(() =>
        {
            _notifying = this;
            try { foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch { } }
            finally { _notifying = null; }
        });
        Track(task);
    }
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; lock (_gate) _work.Remove(t); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public ValueTask DisposeAsync()
    {
        Retire();
        lock (_gate)
        {
            _disposal ??= Task.Run(DrainAsync);
            return ReferenceEquals(_notifying, this) ? ValueTask.CompletedTask : new(_disposal);
        }
    }
    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] work; lock (_gate) { work = _work.ToArray(); if (work.Length == 0) return; }
            try { await Task.WhenAll(work).ConfigureAwait(false); } catch { }
            lock (_gate) foreach (var task in work) _work.Remove(task);
        }
    }
}
