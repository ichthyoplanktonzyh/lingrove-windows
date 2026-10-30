using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Win32.Reading;
using Cotabby.Win32.Text;

namespace Cotabby.App.Reading;

internal interface IReadingVisualGuard : IReadingScreenGuard
{
    ReadingVisualFrame? Frame { get; }
}

/// <summary>Mac 3b SourceReader's explicit OCR-first pointer route. The existing window owner
/// supplies security/origin evidence. Native failures may use the original UIA adapter, but a
/// canceled/stale origin never becomes another acquisition against a later field.</summary>
internal sealed class NativeReadingVisualPointerReader : IReadingPointerReader
{
    private readonly Func<int, int, CancellationToken, int?, Task<IReadingVisualGuard?>> _acquire;
    private readonly Func<ReadingForeground?> _origin;
    private readonly IReadingPointerReader _fallback;
    private readonly ReadingVisualReader _visual;
    private readonly Action<PerformanceRequestObservation>? _observe;
    private readonly Func<DateTimeOffset> _clock;
    internal NativeReadingVisualPointerReader(Win32ReadingWindowSource owner, Func<ReadingForeground?> origin,
        IReadingPointerReader fallback, IReadingOcr ocr, Action<PerformanceRequestObservation>? observe = null)
        : this(async (x, y, token, key) => { var lease = await owner.CapturePointerAsync(x, y, token, key); return lease is null ? null : new NativeReadingScreenReader.WindowGuard(lease); },
            origin, fallback, ocr, new(new(IcuWritingScriptEvidence.ReadScript), new IcuLearningEvidenceSentenceTokenizer().TokenizeUnknown), observe) { }
    internal NativeReadingVisualPointerReader(Func<int, int, CancellationToken, int?, Task<IReadingVisualGuard?>> acquire,
        Func<ReadingForeground?> origin, IReadingPointerReader fallback, IReadingOcr ocr, ReadingVisualContext context,
        Action<PerformanceRequestObservation>? observe = null, Func<DateTimeOffset>? clock = null)
    { _acquire = acquire; _origin = origin; _fallback = fallback; _visual = new(ocr, context); _observe = observe; _clock = clock ?? (() => DateTimeOffset.UtcNow); }
    public async Task<IReadingSelectionLease?> CapturePointerAsync(int x, int y, CancellationToken token, int? initiatingVirtualKey = null)
    {
        token.ThrowIfCancellationRequested(); var origin = _origin(); if (origin is null) return null;
        bool OriginCurrent() => !token.IsCancellationRequested && _origin() == origin;
        IReadingVisualGuard? guard = null; bool transferred = false; Exception? failure = null;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        var gate = new object(); Task? invalidation = null; bool closed = false;
        void Invalidated() { lock (gate) if (!closed) invalidation ??= request.CancelAsync(); }
        var started = _clock(); var watch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = ModelRequestOutcome.Failed; bool captured = false;
        try
        {
            guard = await _acquire(x, y, request.Token, initiatingVirtualKey);
            if (!OriginCurrent() || request.IsCancellationRequested) throw new OperationCanceledException();
            if (guard is not null && guard.Evidence is { WindowOrigin: not null } proof && guard.Frame is { } frame)
            {
                if (proof.ProcessId != origin.ProcessId || proof.FocusEpoch != origin.FocusEpoch) throw new OperationCanceledException();
                guard.Invalidated += Invalidated;
                async Task<bool> Current(CancellationToken ct) => OriginCurrent() && guard.IsLive && guard.Evidence == proof
                    && guard.Frame == frame && await guard.IsCurrentAsync(ct) && OriginCurrent() && guard.IsLive;
                if (!await Current(request.Token)) throw new OperationCanceledException();
                var pixels = await guard.CaptureRegionAsync(frame.Bounds, request.Token);
                if (pixels is not null)
                {
                    var capturedAt = _clock();
                    captured = true;
                    try
                    {
                        if (pixels.DisplayBounds != frame.Display || frame.X != x || frame.Y != y || !await Current(request.Token)) throw new OperationCanceledException();
                        var result = await _visual.ReadAsync(pixels.Bitmap, frame, Current, request.Token, TimeSpan.FromSeconds(12) - watch.Elapsed);
                        if (!await Current(request.Token)) throw new OperationCanceledException();
                        if (result is not null)
                        {
                            var selection = new ReadingSelection(Guid.NewGuid(), result.Text, proof.ProcessId, "", proof.FocusEpoch,
                                null, result.Text.Length, false, true, InputCompositionState.Unknown, 0, result.Anchor, result.Context)
                            {
                                Source = ReadingSourceKind.Pointer, CapturedAt = capturedAt,
                                ScreenReceipt = new(Guid.NewGuid(), proof.FieldBounds, frame.Display, frame.Bounds) { WindowOrigin = proof.WindowOrigin },
                            };
                            ReadingSelectionPolicy.ValidateReceipt(selection);
                            guard.Invalidated -= Invalidated; transferred = true; outcome = ModelRequestOutcome.Completed;
                            return new Lease(guard, selection);
                        }
                    }
                    finally { Array.Clear(pixels.Bitmap.Bgra); }
                }
                if (!await Current(request.Token)) throw new OperationCanceledException();
            }
        }
        catch (OperationCanceledException) { outcome = ModelRequestOutcome.Cancelled; throw; }
        catch (Exception error)
        {
            if (!OriginCurrent() || request.IsCancellationRequested || guard is not null && !guard.IsLive)
            { outcome = ModelRequestOutcome.Cancelled; throw new OperationCanceledException(); }
            failure = error;
        }
        finally
        {
            if (guard is not null) guard.Invalidated -= Invalidated;
            Task? pending; lock (gate) { closed = true; pending = invalidation; }
            if (pending is not null) try { await pending; } catch { }
            if (guard is not null && !transferred) { guard.Retire(); await guard.DisposeAsync(); }
            if (captured) try { _observe?.Invoke(new(ModelRequestSource.Ocr, Guid.NewGuid().ToString("N"), "Windows OCR", started, watch.ElapsedMilliseconds, outcome)); } catch { }
        }
        if (!OriginCurrent()) throw new OperationCanceledException();
        var fallback = await _fallback.CapturePointerAsync(x, y, token, initiatingVirtualKey);
        if (!OriginCurrent() || fallback is not null && (fallback.Selection.ProcessId != origin.ProcessId || fallback.Selection.FocusEpoch != origin.FocusEpoch))
        {
            if (fallback is not null) { fallback.Retire(); await fallback.DisposeAsync(); }
            throw new OperationCanceledException();
        }
        if (fallback is not null) return fallback;
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return null;
    }
    private sealed class Lease(IReadingVisualGuard guard, ReadingSelection selection) : IReadingSelectionLease
    {
        public ReadingSelection Selection => selection;
        public bool IsLive => guard.IsLive;
        public event Action? Invalidated { add => guard.Invalidated += value; remove => guard.Invalidated -= value; }
        public Task<bool> IsCurrentAsync(CancellationToken token) => guard.IsCurrentAsync(token);
        public void Retire() => guard.Retire();
        public ValueTask DisposeAsync() => guard.DisposeAsync();
    }
}

/// <summary>Selection remains the existing fast UIA path; only an explicit pointer action enters
/// visual acquisition. Passive selection icons never trigger a screenshot.</summary>
internal sealed class ReadingSourceReader(NativeReadingSelectionReader selection, IReadingPointerReader pointer)
    : IReadingSelectionReader, IReadingPointerReader, IReadingPendingKeyOwner
{
    public void ArmPendingActivationKey(int key) => selection.ArmPendingActivationKey(key);
    public Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? key = null) => selection.CaptureAsync(token, key);
    public Task<IReadingSelectionLease?> CapturePointerAsync(int x, int y, CancellationToken token, int? key = null) => pointer.CapturePointerAsync(x, y, token, key);
}
