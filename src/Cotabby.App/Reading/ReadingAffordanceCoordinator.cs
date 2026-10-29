using System.Windows.Threading;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;

namespace Cotabby.App.Reading;

/// <summary>Passive selection gesture ownership. The mouse hook only supplies metadata; the
/// retained read receipt is transferred once to an explicit activation and never grants writes.</summary>
internal sealed class ReadingAffordanceCoordinator : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IReadingPointerMonitor _pointer;
    private readonly IReadingSelectionReader _reader;
    private readonly Func<ReadingForeground?> _origin;
    private readonly Func<ReadingForeground, bool> _canCapture;
    private readonly Func<ReadingForeground, bool> _showIcon;
    private readonly Func<int, int, bool> _containsUi;
    private readonly Action _withdrawActive;
    private readonly Action<IReadingSelectionLease, ReadingForeground> _activate;
    private readonly Action<ReadingSelection, int, int, Action> _show;
    private readonly Action _hide;
    private readonly Func<bool> _ignorePointer;
    private readonly HashSet<Task> _work = [];
    private CancellationOwner? _cancel;
    private IReadingSelectionLease? _pending;
    private Task? _capture;
    private ReadingForeground? _captureOrigin;
    private ReadingForeground? _downOrigin;
    private int _downX, _downY, _ownedDown;
    private long _epoch;
    private int _closed;
    private bool _disposed;
    private Task? _disposal;

    internal ReadingAffordanceCoordinator(Dispatcher dispatcher, IReadingPointerMonitor pointer, IReadingSelectionReader reader,
        Func<ReadingForeground?> origin, Func<ReadingForeground, bool> canCapture, Func<ReadingForeground, bool> showIcon,
        Func<int, int, bool> containsUi, Action withdrawActive, Action<IReadingSelectionLease, ReadingForeground> activate,
        Action<ReadingSelection, int, int, Action> show, Action hide, Func<bool>? ignorePointer = null)
    {
        _dispatcher = dispatcher; _pointer = pointer; _reader = reader; _origin = origin; _canCapture = canCapture;
        _showIcon = showIcon; _containsUi = containsUi; _withdrawActive = withdrawActive; _activate = activate;
        _show = show; _hide = hide; _ignorePointer = ignorePointer ?? (() => false); _pointer.Pointer += OnPointer;
    }

    // Called on the observation hook thread. There is no UIA, dispatcher wait or suppression.
    private void OnPointer(ReadingPointerEvent e)
    {
        if (Volatile.Read(ref _closed) != 0 || _ignorePointer()) return;
        bool inside = _containsUi(e.X, e.Y);
        if (e.Kind == ReadingPointerKind.LeftDown)
        {
            _ownedDown = inside ? 1 : 0;
            if (inside) { _downOrigin = null; return; }
            _downX = e.X; _downY = e.Y; _downOrigin = _origin();
            Invalidate();
        }
        else if (e.Kind is ReadingPointerKind.RightDown or ReadingPointerKind.Scroll)
        {
            if (!inside) Invalidate();
        }
        else if (e.Kind == ReadingPointerKind.LeftUp)
        {
            if (Interlocked.Exchange(ref _ownedDown, 0) != 0 || inside) { _downOrigin = null; return; }
            var origin = _downOrigin; _downOrigin = null;
            double dx = (double)e.X - _downX, dy = (double)e.Y - _downY;
            if (origin is null || (dx * dx + dy * dy <= 9 && e.ClickCount < 2)) return;
            long epoch = Interlocked.Read(ref _epoch);
            Post(() => Start(epoch, origin, e.X, e.Y));
        }
    }

    internal void Invalidate()
    {
        long epoch = Interlocked.Increment(ref _epoch);
        Volatile.Read(ref _pending)?.Retire();
        Volatile.Read(ref _cancel)?.Cancel();
        _withdrawActive();
        Post(() => { if (epoch == Interlocked.Read(ref _epoch)) Clear(); });
    }

    private void Start(long epoch, ReadingForeground origin, int x, int y)
    {
        if (_disposed || epoch != Interlocked.Read(ref _epoch) || _origin() != origin || !_canCapture(origin)) return;
        Clear(); var cancellation = new CancellationOwner(); Volatile.Write(ref _cancel, cancellation);
        Volatile.Write(ref _captureOrigin, origin); _capture = CaptureAsync(epoch, origin, x, y, cancellation); Track(_capture);
    }
    private async Task CaptureAsync(long epoch, ReadingForeground origin, int x, int y, CancellationOwner owner)
    {
        IReadingSelectionLease? lease = null; bool transferred = false;
        var token = owner.Token;
        try
        {
            await Task.Delay(150, token);
            if (!Current(epoch, origin, owner)) return;
            lease = await _reader.CaptureAsync(token);
            if (lease is null || !Current(epoch, origin, owner) || !lease.IsLive || !await lease.IsCurrentAsync(token)
                || !Current(epoch, origin, owner) || lease.Selection.ProcessId != origin.ProcessId || lease.Selection.FocusEpoch != origin.FocusEpoch) return;
            Volatile.Write(ref _pending, lease); transferred = true;
            var held = lease;
            held.Invalidated += () => { held.Retire(); Post(() => { if (ReferenceEquals(_pending, held)) Clear(); }); };
            if (!held.IsLive || !Current(epoch, origin, owner)) { Clear(); return; }
            if (_showIcon(origin)) _show(held.Selection, x, y, () => Consume(epoch, origin, held));
        }
        catch (OperationCanceledException) { }
        finally { if (lease is not null && !transferred) { lease.Retire(); await lease.DisposeAsync(); } }
    }
    private bool Current(long epoch, ReadingForeground origin, CancellationOwner owner) =>
        !_disposed && epoch == Interlocked.Read(ref _epoch) && !owner.Token.IsCancellationRequested && _origin() == origin && _canCapture(origin);
    private void Consume(long epoch, ReadingForeground origin, IReadingSelectionLease lease)
    {
        if (_disposed || epoch != Interlocked.Read(ref _epoch) || _origin() != origin || !_canCapture(origin)
            || !ReferenceEquals(_pending, lease) || !lease.IsLive) return;
        Volatile.Write(ref _pending, null); _capture = null; Volatile.Write(ref _captureOrigin, null); _hide(); _activate(lease, origin);
    }
    internal bool TryConsume(ReadingForeground origin)
    {
        _dispatcher.VerifyAccess();
        var lease = _pending;
        if (lease is null || !lease.IsLive || _origin() != origin || !_canCapture(origin)) return false;
        Consume(Interlocked.Read(ref _epoch), origin, lease);
        return !ReferenceEquals(_pending, lease);
    }
    // Hook-thread metadata only. This one suppressed activation key cannot modify the host;
    // the retained lease still checks its occurrence before any provider or UI effect.
    internal void ArmShortcut(ReadingForeground origin, int virtualKey)
    {
        if (Volatile.Read(ref _closed) != 0 || Volatile.Read(ref _captureOrigin) != origin || _origin() != origin) return;
        if (Volatile.Read(ref _pending) is { } lease) lease.ArmActivationKey(virtualKey);
        else if (_reader is IReadingPendingKeyOwner owner) owner.ArmPendingActivationKey(virtualKey);
    }
    internal bool TryConsumeOrWait(ReadingForeground origin, out Task<bool> result)
    {
        _dispatcher.VerifyAccess();
        if (TryConsume(origin)) { result = Task.FromResult(true); return true; }
        if (_capture is not { IsCompleted: false } capture || _captureOrigin != origin)
        { result = Task.FromResult(false); return false; }
        result = WaitForCaptureAsync(capture, origin); return true;
    }
    private async Task<bool> WaitForCaptureAsync(Task capture, ReadingForeground origin)
    {
        try { await capture; } catch { return false; }
        return !_disposed && TryConsume(origin);
    }
    private void Clear()
    {
        _hide(); var cancel = Interlocked.Exchange(ref _cancel, null);
        _capture = null; Volatile.Write(ref _captureOrigin, null);
        if (cancel is not null) Track(cancel.CloseAsync());
        var lease = Interlocked.Exchange(ref _pending, null);
        if (lease is not null) { lease.Retire(); Track(lease.DisposeAsync().AsTask()); }
    }
    private sealed class CancellationOwner
    {
        private readonly object _gate = new(); private readonly CancellationTokenSource _source = new();
        private Task? _cancellation; private bool _closed;
        internal CancellationOwner() { Token = _source.Token; }
        internal CancellationToken Token { get; }
        internal void Cancel() { lock (_gate) if (!_closed) _cancellation ??= _source.CancelAsync(); }
        internal async Task CloseAsync()
        {
            Task? pending; lock (_gate) { if (_closed) return; _cancellation ??= _source.CancelAsync(); _closed = true; pending = _cancellation; }
            try { await pending; } finally { _source.Dispose(); }
        }
    }
    private void Track(Task task)
    {
        _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; Post(() => _work.Remove(t)); }, TaskScheduler.Default);
    }
    private void Post(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        try { _dispatcher.BeginInvoke(action); }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) { }
    }
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        _dispatcher.VerifyAccess(); _disposed = true; Volatile.Write(ref _closed, 1); _pointer.Pointer -= OnPointer; Interlocked.Increment(ref _epoch); Clear();
        while (_work.Count > 0) { var tasks = _work.ToArray(); try { await Task.WhenAll(tasks); } catch { } foreach (var task in tasks) _work.Remove(task); }
    }
}
