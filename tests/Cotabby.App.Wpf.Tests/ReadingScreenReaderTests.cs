using Cotabby.App.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;
using Cotabby.Core.Runtime.Metrics;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingScreenReaderTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task ExplicitDesktopRegionUsesWindowProofWithoutATextFieldOrFakeCompositionReceipt() => dispatcher.RunAsync(async () =>
    {
        var origin = new ReadingVisualOrigin(Guid.NewGuid(), new(0, 0, 200, 200));
        var guard = new Guard
        {
            EvidenceOverride = new(1, "", 2, 0, new(-1000, 0, 3000, 1000)) { WindowOrigin = origin },
            Display = new(0, 0, 2000, 1000),
        };
        var reader = new NativeReadingScreenReader((_, _) => Task.FromResult<IReadingScreenGuard?>(guard), (bounds, _) =>
        {
            Assert.Equal(guard.Evidence!.FieldBounds, bounds); Assert.Equal(0, guard.Captures);
            return Task.FromResult<ScreenRect?>(new(500, 30, 16, 16));
        }, new Ocr());
        await using var lease = await reader.CaptureAsync(default); Assert.NotNull(lease);
        Assert.Equal("", lease.Selection.FieldId); Assert.Equal(InputCompositionState.Unknown, lease.Selection.Composition);
        Assert.Equal(0, lease.Selection.CompositionSequence); Assert.Equal(origin, lease.Selection.ScreenReceipt!.WindowOrigin);
        ReadingSelectionPolicy.ValidateReceipt(lease.Selection); Assert.All(guard.Pixels, value => Assert.Equal(0, value));
    });
    [Fact]
    public Task ExplicitRegionCarriesItsOwnProofAndClearsPixels() => dispatcher.RunAsync(async () =>
    {
        var guard = new Guard(); var observations = new List<PerformanceRequestObservation>();
        var now = DateTimeOffset.UnixEpoch; var ocr = new Ocr { OnRecognize = () => now = now.AddMinutes(10) };
        var reader = new NativeReadingScreenReader((_, _) => Task.FromResult<IReadingScreenGuard?>(guard),
            (_, _) => { now = now.AddMinutes(10); return Task.FromResult<ScreenRect?>(new(10, 10, 16, 16)); }, ocr, observations.Add, () => now);
        var lease = await reader.CaptureAsync(default);
        Assert.NotNull(lease); Assert.Equal(ReadingSourceKind.Screenshot, lease.Selection.Source);
        Assert.Null(lease.Selection.SelectionStart); Assert.Null(lease.Selection.SelectionAnchorId);
        Assert.NotNull(lease.Selection.ScreenReceipt); Assert.Equal("Hello.", lease.Selection.Text);
        Assert.NotNull(lease.Selection.Context);
        Assert.Equal(ReadingContextMethod.Ocr, lease.Selection.Context.Method);
        Assert.Equal(ReadingBoundary.Crop, lease.Selection.Context.Left);
        Assert.Equal(ReadingBoundary.Crop, lease.Selection.Context.Right);
        Assert.True(ReadingSelectionPolicy.ValidContext(lease.Selection.Context, lease.Selection.Text));
        Assert.Equal(DateTimeOffset.UnixEpoch, lease.Selection.CapturedAt);
        Assert.Equal(1, guard.Captures); Assert.Equal(1, ocr.Calls); Assert.All(guard.Pixels, x => Assert.Equal(0, x));
        var metric = Assert.Single(observations); Assert.Equal(ModelRequestSource.Ocr, metric.Source); Assert.Equal(ModelRequestOutcome.Completed, metric.Outcome);
        Assert.Equal("Windows OCR", metric.ModelName); Assert.DoesNotContain("Hello", metric.RequestId);
        await lease.DisposeAsync();
    });
    [Fact]
    public Task RetiredDuringOcrDiscardsResultAndClearsPixels() => dispatcher.RunAsync(async () =>
    {
        var guard = new Guard(); var ocr = new Ocr { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var observations = new List<PerformanceRequestObservation>();
        var reader = new NativeReadingScreenReader((_, _) => Task.FromResult<IReadingScreenGuard?>(guard),
            (_, _) => Task.FromResult<ScreenRect?>(new(10, 10, 16, 16)), ocr, observations.Add);
        var work = reader.CaptureAsync(default); await ocr.Entered.Task;
        guard.Retire(); ocr.Gate.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.All(guard.Pixels, x => Assert.Equal(0, x)); Assert.Equal(1, guard.Captures);
        Assert.Equal(ModelRequestOutcome.Cancelled, Assert.Single(observations).Outcome);
    });
    [Fact]
    public Task CancelledPickerNeverCapturesPixels() => dispatcher.RunAsync(async () =>
    {
        var guard = new Guard(); var ocr = new Ocr();
        var reader = new NativeReadingScreenReader((_, _) => Task.FromResult<IReadingScreenGuard?>(guard),
            (_, _) => Task.FromResult<ScreenRect?>(null), ocr);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.CaptureAsync(default)); Assert.Equal(0, guard.Captures); Assert.Equal(0, ocr.Calls);
    });
    private sealed class Guard : IReadingScreenGuard
    {
        public int Captures; public byte[] Pixels = Enumerable.Repeat((byte)1, 16 * 16 * 4).ToArray();
        public ReadingScreenGuardEvidence? EvidenceOverride;
        public ScreenRect Display = new(0, 0, 100, 100);
        public bool IsLive { get; private set; } = true;
        public ReadingScreenGuardEvidence? Evidence => IsLive ? EvidenceOverride ?? new(42, "field", 3, 2, new(0, 0, 100, 100)) : null;
        public event Action? Invalidated;
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(IsLive && !token.IsCancellationRequested);
        public Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken token)
        { Captures++; return Task.FromResult<ReadingScreenPixels?>(new(new(16, 16, Pixels), Display)); }
        public void Retire() { if (!IsLive) return; IsLive = false; Invalidated?.Invoke(); }
        public ValueTask DisposeAsync() { Retire(); return ValueTask.CompletedTask; }
    }
    private sealed class Ocr : IReadingOcr
    {
        public int Calls; public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Gate;
        public Action? OnRecognize;
        public async Task<IReadOnlyList<ReadingOcrLine>> RecognizeAsync(ReadingBitmap bitmap, CancellationToken token)
        {
            Calls++; OnRecognize?.Invoke(); Entered.TrySetResult(); if (Gate is not null) await Gate.Task;
            return [new("Hello.", [new("Hello.", new(0, 0, 10, 10))])];
        }
    }
}
