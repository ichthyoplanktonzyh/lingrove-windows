using Cotabby.App.UI.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;
using Cotabby.Core.Runtime.Metrics;
using Cotabby.Win32.Text;

namespace Cotabby.App.Reading;

/// <summary>Explicit screen reading. The native owner retains field or origin-window proof; the
/// picker selects geometry only, and pixels stay request-owned until Windows OCR completes.</summary>
internal interface IReadingScreenGuard : IAsyncDisposable
{
    ReadingScreenGuardEvidence? Evidence { get; }
    bool IsLive { get; }
    event Action? Invalidated;
    Task<bool> IsCurrentAsync(CancellationToken token);
    Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken token);
    void Retire();
}
internal sealed class NativeReadingScreenReader : IReadingScreenReader
{
    private readonly Func<CancellationToken, int?, Task<IReadingScreenGuard?>> _acquire;
    private readonly Func<ScreenRect, CancellationToken, Task<ScreenRect?>> _pick;
    private readonly IReadingOcr _ocr;
    private readonly Action<PerformanceRequestObservation>? _observe;
    private readonly Func<DateTimeOffset> _clock;
    internal NativeReadingScreenReader(Win32ReadingSelectionReader owner, IReadingOcr ocr, Action<PerformanceRequestObservation>? observe = null)
        : this(async (token, key) => { var lease = await owner.CaptureScreenGuardAsync(token, key); return lease is null ? null : new Guard(lease); },
            async (bounds, token) => { using var picker = new ReadingRegionPicker(bounds); return await picker.SelectAsync(token); }, ocr, observe) { }
    internal NativeReadingScreenReader(Win32ReadingWindowSource owner, IReadingOcr ocr, Action<PerformanceRequestObservation>? observe = null)
        : this(async (token, key) => { var lease = await owner.CaptureAsync(token, key); return lease is null ? null : new WindowGuard(lease); },
            async (bounds, token) => { using var picker = new ReadingRegionPicker(bounds); return await picker.SelectAsync(token); }, ocr, observe) { }
    internal NativeReadingScreenReader(Func<CancellationToken, int?, Task<IReadingScreenGuard?>> acquire,
        Func<ScreenRect, CancellationToken, Task<ScreenRect?>> pick, IReadingOcr ocr, Action<PerformanceRequestObservation>? observe = null,
        Func<DateTimeOffset>? clock = null)
    { _acquire = acquire; _pick = pick; _ocr = ocr; _observe = observe; _clock = clock ?? (() => DateTimeOffset.UtcNow); }
    private int _selecting;
    public bool IsSelectingRegion => Volatile.Read(ref _selecting) != 0;
    public async Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
    {
        IReadingScreenGuard? guard = null; bool transferred = false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancellationGate = new object(); Task? invalidation = null; bool cancellationClosed = false;
        void Invalidated()
        {
            lock (cancellationGate) if (!cancellationClosed) invalidation ??= request.CancelAsync();
        }
        try
        {
            guard = await _acquire(request.Token, initiatingVirtualKey);
            if (guard is null || guard.Evidence is not { } proof) return null;
            if (!guard.IsLive || !await guard.IsCurrentAsync(request.Token)) throw new OperationCanceledException();
            var capturedAt = _clock();
            guard.Invalidated += Invalidated;
            if (!guard.IsLive || request.IsCancellationRequested) throw new OperationCanceledException();
            ScreenRect? region;
            Interlocked.Increment(ref _selecting);
            try { region = await _pick(proof.FieldBounds, request.Token); }
            finally { Interlocked.Decrement(ref _selecting); }
            if (region is null || !guard.IsLive || request.IsCancellationRequested
                || guard.Evidence != proof || !await guard.IsCurrentAsync(request.Token)) throw new OperationCanceledException();
            request.CancelAfter(TimeSpan.FromSeconds(12));
            var pixels = await guard.CaptureRegionAsync(region.Value, request.Token);
            if (pixels is null) { request.Token.ThrowIfCancellationRequested(); if (!guard.IsLive) throw new OperationCanceledException(); return null; }
            try
            {
                if (!guard.IsLive || request.IsCancellationRequested) throw new OperationCanceledException();
                var startedAt = DateTimeOffset.UtcNow; var watch = System.Diagnostics.Stopwatch.StartNew();
                var requestId = Guid.NewGuid().ToString("N"); var outcome = ModelRequestOutcome.Failed; string text;
                try
                {
                    var lines = await _ocr.RecognizeAsync(pixels.Bitmap, request.Token);
                    request.Token.ThrowIfCancellationRequested();
                    if (!guard.IsLive) throw new OperationCanceledException();
                    text = ReadingOcrTextPolicy.Text(lines, pixels.Bitmap.Width, pixels.Bitmap.Height);
                    outcome = ModelRequestOutcome.Completed;
                }
                catch (OperationCanceledException) { outcome = ModelRequestOutcome.Cancelled; throw; }
                finally
                {
                    try { _observe?.Invoke(new(ModelRequestSource.Ocr, requestId, "Windows OCR", startedAt, watch.ElapsedMilliseconds, outcome)); } catch { }
                }
                if (!guard.IsLive || request.IsCancellationRequested || guard.Evidence != proof
                    || !await guard.IsCurrentAsync(request.Token) || !guard.IsLive) throw new OperationCanceledException();
                // The full OCR target is retained. Crop boundaries never certify surrounding
                // sentences, and Windows OCR exposes no confidence score to manufacture here.
                var context = await Task.Run(() => ReadingContextPolicy.Resolve(
                    new(text, 0, text.Length, ReadingBoundary.Crop, ReadingBoundary.Crop, ReadingContextMethod.Ocr), text,
                    new IcuLearningEvidenceSentenceTokenizer().TokenizeUnknown), CancellationToken.None);
                if (!guard.IsLive || request.IsCancellationRequested || !await guard.IsCurrentAsync(request.Token) || !guard.IsLive)
                    throw new OperationCanceledException();
                var selection = new ReadingSelection(Guid.NewGuid(), text, proof.ProcessId, proof.FieldId, proof.FocusEpoch,
                    null, text.Length, false, true, proof.WindowOrigin is null ? InputCompositionState.Clear : InputCompositionState.Unknown, proof.CompositionSequence, region.Value, context)
                {
                    Source = ReadingSourceKind.Screenshot,
                    ScreenReceipt = new(Guid.NewGuid(), proof.FieldBounds, pixels.DisplayBounds, region.Value) { WindowOrigin = proof.WindowOrigin },
                    CapturedAt = capturedAt,
                };
                ReadingSelectionPolicy.ValidateReceipt(selection);
                guard.Invalidated -= Invalidated; transferred = true; return new ScreenLease(guard, selection);
            }
            finally { Array.Clear(pixels.Bitmap.Bgra); }
        }
        catch (ReadingOcrUnavailableException) { throw; }
        finally
        {
            if (guard is not null) guard.Invalidated -= Invalidated;
            Task? pending; lock (cancellationGate) { cancellationClosed = true; pending = invalidation; }
            if (pending is not null) try { await pending; } catch { }
            if (guard is not null && !transferred) { guard.Retire(); await guard.DisposeAsync(); }
        }
    }
    private sealed class ScreenLease(IReadingScreenGuard guard, ReadingSelection selection) : IReadingSelectionLease
    {
        public ReadingSelection Selection => selection;
        public bool IsLive => guard.IsLive;
        public event Action? Invalidated { add => guard.Invalidated += value; remove => guard.Invalidated -= value; }
        public Task<bool> IsCurrentAsync(CancellationToken token) => guard.IsCurrentAsync(token);
        public void Retire() => guard.Retire();
        public ValueTask DisposeAsync() => guard.DisposeAsync();
    }
    private sealed class Guard(Win32ReadingSelectionLease native) : IReadingScreenGuard
    {
        public ReadingScreenGuardEvidence? Evidence => native.ScreenGuardEvidence;
        public bool IsLive => native.IsLive;
        public event Action? Invalidated { add => native.Invalidated += value; remove => native.Invalidated -= value; }
        public Task<bool> IsCurrentAsync(CancellationToken token) => native.IsCurrentAsync(token);
        public Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken token) => native.CaptureRegionAsync(region, token);
        public void Retire() => native.Retire();
        public ValueTask DisposeAsync() => native.DisposeAsync();
    }
    internal sealed class WindowGuard(Win32ReadingWindowLease native) : IReadingVisualGuard
    {
        public ReadingVisualFrame? Frame => native.PointerFrame;
        public ReadingScreenGuardEvidence? Evidence => native.Evidence;
        public bool IsLive => native.IsLive;
        public event Action? Invalidated { add => native.Invalidated += value; remove => native.Invalidated -= value; }
        public Task<bool> IsCurrentAsync(CancellationToken token) => native.IsCurrentAsync(token);
        public Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken token) => native.CaptureRegionAsync(region, token);
        public void Retire() => native.Retire();
        public ValueTask DisposeAsync() => native.DisposeAsync();
    }
}
