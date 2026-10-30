using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Win32.Reading;
using Cotabby.Win32.Text;

namespace Cotabby.App.Reading;

/// <summary>Mac 3b SourceReader's optional selection enrichment. Native selection authority
/// remains primary; an adopted context additionally retains the exact visual-window guard.</summary>
internal sealed class ReadingSelectionVisualEnricher
{
    private readonly Func<int, int, CancellationToken, int?, Task<IReadingVisualGuard?>> _acquire;
    private readonly Func<ReadingForeground?> _origin;
    private readonly ReadingVisualReader _reader;
    private readonly ReadingSelectionOcrPolicy _policy;
    private readonly Action<PerformanceRequestObservation>? _observe;
    internal ReadingSelectionVisualEnricher(Win32ReadingWindowSource owner, Func<ReadingForeground?> origin, IReadingOcr ocr,
        Action<PerformanceRequestObservation>? observe = null)
        : this(async (x, y, token, key) => { var lease = await owner.CapturePointerAsync(x, y, token, key); return lease is null ? null : new NativeReadingScreenReader.WindowGuard(lease); },
            origin, ocr, new(IcuWritingScriptEvidence.ReadScript), new IcuLearningEvidenceSentenceTokenizer().TokenizeUnknown, observe) { }
    internal ReadingSelectionVisualEnricher(Func<int, int, CancellationToken, int?, Task<IReadingVisualGuard?>> acquire,
        Func<ReadingForeground?> origin, IReadingOcr ocr, ReadingVisualLayout layout,
        Func<string, IReadOnlyList<Cotabby.Core.LanguageLearning.LearningTextRange>> sentences, Action<PerformanceRequestObservation>? observe = null)
    {
        _acquire = acquire; _origin = origin; var context = new ReadingVisualContext(layout, sentences);
        _reader = new(ocr, context); _policy = new(layout, context, sentences); _observe = observe;
    }
    internal IReadingSelectionLease Wrap(IReadingSelectionLease source) => new EnrichedLease(source, this);
    private async Task EnrichAsync(EnrichedLease lease, CancellationToken token, Func<ReadingVisualFrame, Task>? pixelsReady)
    {
        var selection = lease.Selection;
        if (selection.Source != ReadingSourceKind.Selection || ReadingVisualPolicy.Complete(selection.Context)
            || selection.OriginWindowBounds is null || ReadingGesturePolicy.SearchBounds(selection, now: DateTimeOffset.UtcNow) is not { } search) return;
        var origin = _origin();
        if (origin is null || origin.ProcessId != selection.ProcessId || origin.FocusEpoch != selection.FocusEpoch) throw new OperationCanceledException();
        IReadingVisualGuard? guard = null; bool transferred = false, captured = false;
        var started = DateTimeOffset.UtcNow; var watch = System.Diagnostics.Stopwatch.StartNew(); var outcome = ModelRequestOutcome.Failed;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        var gate = new object(); Task? cancellation = null; bool closed = false;
        void Invalidated() { lock (gate) if (!closed) cancellation ??= request.CancelAsync(); }
        try
        {
            double x = search.X + search.Width / 2, y = search.Y + search.Height / 2;
            if (x < int.MinValue || y < int.MinValue || x > int.MaxValue || y > int.MaxValue) return;
            guard = await _acquire((int)Math.Floor(x), (int)Math.Floor(y), request.Token, lease.PendingActivationKey);
            token.ThrowIfCancellationRequested();
            if (!lease.IsLive || _origin() != origin) throw new OperationCanceledException();
            if (guard?.Evidence is not { } proof || guard.Frame is not { } frame) return;
            guard.Invalidated += Invalidated;
            async Task<bool> Current(CancellationToken ct) => !request.IsCancellationRequested && lease.IsLive && _origin() == origin
                && guard.IsLive && guard.Evidence == proof && guard.Frame == frame && guard.ForegroundBounds == selection.OriginWindowBounds
                && await lease.IsCurrentAsync(ct) && await guard.IsCurrentAsync(ct) && lease.IsLive && guard.IsLive && _origin() == origin;
            if (proof.ProcessId != origin.ProcessId || proof.FocusEpoch != origin.FocusEpoch || !await Current(request.Token)) throw new OperationCanceledException();
            if (ReadingGesturePolicy.SearchBounds(selection, frame.Bounds, DateTimeOffset.UtcNow, frame.Scale) is null) return;
            var pixels = await guard.CaptureRegionAsync(frame.Bounds, request.Token); if (pixels is null) return;
            captured = true;
            try
            {
                if (pixels.DisplayBounds != frame.Display || !await Current(request.Token)) throw new OperationCanceledException();
                // GDI must finish before showing our own progress surface. There is no second
                // screen capture; the presentation keeps its window away from the frozen point.
                if (pixelsReady is not null) await pixelsReady(frame);
                if (!await Current(request.Token)) throw new OperationCanceledException();
                var result = await _reader.ReadSelectionAsync(pixels.Bitmap, frame, selection, _policy, Current, request.Token, TimeSpan.FromSeconds(12) - watch.Elapsed);
                if (!await Current(request.Token)) throw new OperationCanceledException();
                var preferred = ReadingSelectionOcrPolicy.Preferred(selection.Context, result?.Context, selection.Text);
                outcome = ModelRequestOutcome.Completed;
                if (preferred is null || preferred == selection.Context) return;
                guard.Invalidated -= Invalidated;
                transferred = lease.Adopt(guard, preferred);
                if (!lease.IsLive || request.IsCancellationRequested || token.IsCancellationRequested) throw new OperationCanceledException();
            }
            finally { Array.Clear(pixels.Bitmap.Bgra); }
        }
        catch (OperationCanceledException) { outcome = ModelRequestOutcome.Cancelled; throw; }
        catch (Exception)
        {
            token.ThrowIfCancellationRequested();
            if (!lease.IsLive || _origin() != origin || request.IsCancellationRequested) throw new OperationCanceledException();
            // Optional native/OCR failure retains the original exact selection and AX context.
        }
        finally
        {
            if (guard is not null) guard.Invalidated -= Invalidated;
            Task? pending; lock (gate) { closed = true; pending = cancellation; }
            if (pending is not null) try { await pending; } catch { }
            if (guard is not null && !transferred) { guard.Retire(); await guard.DisposeAsync(); }
            if (captured) try { _observe?.Invoke(new(ModelRequestSource.Ocr, Guid.NewGuid().ToString("N"), "Windows OCR", started, watch.ElapsedMilliseconds, outcome)); } catch { }
        }
    }
    private sealed class EnrichedLease : IReadingSelectionLease
    {
        [ThreadStatic] private static EnrichedLease? _notifying;
        private readonly object _gate = new(); private readonly IReadingSelectionLease _source; private readonly ReadingSelectionVisualEnricher _owner;
        private readonly CancellationTokenSource _lifetime = new(); private IReadingVisualGuard? _guard; private ReadingContext? _context;
        private Func<bool>? _operationAllowed;
        private Task? _enrichment, _cancellation, _disposal; private int _closed; private Action? _invalidated;
        internal EnrichedLease(IReadingSelectionLease source, ReadingSelectionVisualEnricher owner)
        { _source = source; _owner = owner; _source.Invalidated += Retire; }
        public ReadingSelection Selection => Volatile.Read(ref _context) is { } context ? _source.Selection with { Context = context } : _source.Selection;
        public int? PendingActivationKey => _source.PendingActivationKey;
        public bool IsLive => Volatile.Read(ref _closed) == 0 && Volatile.Read(ref _operationAllowed)?.Invoke() != false
            && _source.IsLive && (Volatile.Read(ref _guard)?.IsLive ?? true);
        public event Action? Invalidated
        {
            add { bool notify; lock (_gate) { notify = _closed != 0; if (!notify) _invalidated += value; } if (notify && value is not null) Notify(value); }
            remove { lock (_gate) _invalidated -= value; }
        }
        public async Task<bool> IsCurrentAsync(CancellationToken token)
        {
            var guard = Volatile.Read(ref _guard);
            bool current = IsLive && await _source.IsCurrentAsync(token) && (guard is null || await guard.IsCurrentAsync(token)) && IsLive;
            if (!current) Retire(); return current;
        }
        public Task EnrichAsync(CancellationToken token) => Enrich(token, null);
        public Task EnrichAsync(CancellationToken token, Func<ReadingVisualFrame, Task> pixelsReady) => Enrich(token, pixelsReady);
        private Task Enrich(CancellationToken token, Func<ReadingVisualFrame, Task>? pixelsReady)
        {
            lock (_gate)
            {
                if (_closed != 0) return Task.FromCanceled(new CancellationToken(true));
                if (_enrichment is not null) return _enrichment;
                // CancelAsync publishes the caller's state before dispatching linked callbacks.
                // Read that original state directly at admission, not only the propagated token.
                Volatile.Write(ref _operationAllowed, () => !token.IsCancellationRequested);
                return _enrichment = Task.Run(async () =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
                    await _source.EnrichAsync(linked.Token); linked.Token.ThrowIfCancellationRequested();
                    await _owner.EnrichAsync(this, linked.Token, pixelsReady);
                    linked.Token.ThrowIfCancellationRequested();
                    if (!IsLive) throw new OperationCanceledException();
                });
            }
        }
        internal bool Adopt(IReadingVisualGuard guard, ReadingContext context)
        {
            lock (_gate)
            {
                if (!IsLive || !guard.IsLive) return false;
                _guard = guard; guard.Invalidated += Retire;
                if (IsLive) Volatile.Write(ref _context, context);
                return true; // This lease owns even an immediately invalidated guard's drain.
            }
        }
        public void BindGesture(ReadingSelectionGesture gesture) => _source.BindGesture(gesture);
        public void ArmActivationKey(int key) => _source.ArmActivationKey(key);
        public void Retire()
        {
            Action? handlers; IReadingVisualGuard? guard;
            lock (_gate)
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                _cancellation ??= _lifetime.CancelAsync(); handlers = _invalidated; _invalidated = null; guard = _guard;
            }
            _source.Retire(); guard?.Retire(); if (handlers is not null) Notify(handlers);
        }
        private void Notify(Action handlers)
        {
            var previous = _notifying; _notifying = this;
            try { foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch { } }
            finally { _notifying = previous; }
        }
        public ValueTask DisposeAsync()
        {
            Retire(); lock (_gate)
            {
                _disposal ??= Task.Run(DrainAsync);
                return ReferenceEquals(_notifying, this) ? ValueTask.CompletedTask : new(_disposal);
            }
        }
        private async Task DrainAsync()
        {
            try { if (_enrichment is not null) try { await _enrichment; } catch { } if (_cancellation is not null) await _cancellation; }
            finally
            {
                _source.Invalidated -= Retire;
                try { if (_guard is { } guard) { guard.Invalidated -= Retire; await guard.DisposeAsync(); } }
                finally { try { await _source.DisposeAsync(); } finally { _lifetime.Dispose(); } }
            }
        }
    }
}
