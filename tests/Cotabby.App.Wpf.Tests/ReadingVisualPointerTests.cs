using System.Text.RegularExpressions;
using Cotabby.App.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Production pointer acquisition over synthetic origin/word geometry and owned pixel
/// arrays. No window, screen, OCR provider, input hook, endpoint, clipboard or user editor is used.</summary>
public sealed class ReadingVisualPointerTests
{
    private static ReadingVisualContext Context() => new(new(_ => 25), text => Regex.Matches(text, @"[^.!?]+[.!?]?")
        .Select(match => new LearningTextRange(match.Index, match.Length)).ToArray());
    private static readonly ReadingForeground Origin = new(11, 42, 3, "synthetic");
    private static IReadOnlyList<ReadingOcrLine> Lines(ReadingBitmap bitmap, bool expanded)
    {
        string text = expanded ? "Earlier sentence. Keep your history safe. Another sentence." : "Keep your history safe.";
        var values = text.Split(' '); int target = Array.IndexOf(values, "history");
        var words = values.Select((value, index) => new ReadingOcrWord(value,
            new(expanded ? bitmap.Width / 2 - 35 + (index - target) * 60 : index switch { 0 => 0, 1 => 50, 2 => bitmap.Width / 2 - 35, _ => bitmap.Width - 60 },
                bitmap.Height / 2 - 10, value == "history" ? 70 : 50, 20))).ToArray();
        return [new(text, words)];
    }
    private sealed class Ocr : IReadingOcr
    {
        internal int Calls; internal readonly List<ReadingBitmap> Crops = [];
        internal Func<ReadingBitmap, int, CancellationToken, Task<IReadOnlyList<ReadingOcrLine>>> Read = (bitmap, pass, _) => Task.FromResult(Lines(bitmap, pass > 1));
        public Task<IReadOnlyList<ReadingOcrLine>> RecognizeAsync(ReadingBitmap bitmap, CancellationToken token)
        { Crops.Add(bitmap); return Read(bitmap, ++Calls, token); }
    }
    private sealed class Guard : IReadingVisualGuard
    {
        internal byte[] Pixels = Enumerable.Repeat((byte)73, 1000 * 600 * 4).ToArray(); internal int Captures, Stops;
        internal Action? BeforeCapture, BeforeCurrent;
        public ReadingVisualFrame? Frame { get; } = new(500, 300, new(0, 0, 1000, 800), new(0, 0, 1000, 600), 1);
        public ReadingScreenGuardEvidence? Evidence { get; } = new(42, "", 3, 0, new(0, 0, 1000, 800)) { WindowOrigin = new(Guid.NewGuid(), new(0, 0, 1000, 600)) };
        public bool IsLive { get; private set; } = true;
        public event Action? Invalidated;
        public Task<bool> IsCurrentAsync(CancellationToken token) { BeforeCurrent?.Invoke(); return Task.FromResult(IsLive && !token.IsCancellationRequested); }
        public Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken token)
        { BeforeCapture?.Invoke(); Captures++; Assert.Equal(Frame!.Bounds, region); return Task.FromResult<ReadingScreenPixels?>(new(new(1000, 600, Pixels), Frame.Display)); }
        public void Retire() { if (!IsLive) return; IsLive = false; Invalidated?.Invoke(); }
        public ValueTask DisposeAsync() { Stops++; Retire(); return ValueTask.CompletedTask; }
    }
    private sealed class Fallback : IReadingPointerReader
    {
        internal int Calls; internal Func<Task<IReadingSelectionLease?>> Read = () => Task.FromResult<IReadingSelectionLease?>(null);
        public Task<IReadingSelectionLease?> CapturePointerAsync(int x, int y, CancellationToken token, int? key = null)
        { Calls++; Assert.Equal(500, x); Assert.Equal(300, y); return Read(); }
    }
    private static NativeReadingVisualPointerReader Reader(Guard? guard, Ocr ocr, Fallback fallback, Func<ReadingForeground?>? origin = null, Func<DateTimeOffset>? clock = null)
        => new((x, y, _, _) => { Assert.Equal(500, x); Assert.Equal(300, y); return Task.FromResult<IReadingVisualGuard?>(guard); }, origin ?? (() => Origin), fallback, ocr, Context(), clock: clock);
    [Fact]
    public async Task CaptureTimeFreezesWhenPixelsArriveAndOcrCannotMoveTheEncounterTime()
    {
        var now = DateTimeOffset.UnixEpoch; var guard = new Guard { BeforeCapture = () => now = now.AddMinutes(1) };
        var ocr = new Ocr { Read = (bitmap, pass, _) => { now = now.AddDays(1); return Task.FromResult(Lines(bitmap, pass > 1)); } };
        await using var lease = await Reader(guard, ocr, new(), clock: () => now).CapturePointerAsync(500, 300, default); Assert.NotNull(lease);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1), lease.Selection.CapturedAt); Assert.True(now > lease.Selection.CapturedAt);
    }
    [Fact]
    public async Task TwoOcrCropsShareOneFrameAndRetainTheExactOccurrenceAndWindowReceipt()
    {
        var guard = new Guard(); var ocr = new Ocr(); var fallback = new Fallback();
        await using var lease = await Reader(guard, ocr, fallback).CapturePointerAsync(500, 300, default); Assert.NotNull(lease);
        Assert.Equal(1, guard.Captures); Assert.Equal(2, ocr.Calls); Assert.Equal(0, fallback.Calls);
        Assert.Equal(new[] { (720, 240), (1000, 240) }, ocr.Crops.Select(crop => (crop.Width, crop.Height)));
        Assert.Equal("history", lease.Selection.Text); Assert.Equal(ReadingSourceKind.Pointer, lease.Selection.Source);
        Assert.Equal("Keep your history safe.", lease.Selection.Context!.Text.Trim());
        Assert.Equal(InputCompositionState.Unknown, lease.Selection.Composition); Assert.Equal("", lease.Selection.FieldId);
        Assert.Null(lease.Selection.SelectionAnchorId); Assert.Null(lease.Selection.SelectionStart);
        Assert.NotNull(lease.Selection.ScreenReceipt!.WindowOrigin); ReadingSelectionPolicy.ValidateReceipt(lease.Selection);
        Assert.True(await lease.IsCurrentAsync(default)); Assert.Equal(0, guard.Stops);
        Assert.All(guard.Pixels, value => Assert.Equal(0, value)); Assert.All(ocr.Crops, crop => Assert.All(crop.Bgra, value => Assert.Equal(0, value)));
    }
    [Fact]
    public async Task NativeOcrFailureUsesTheExistingFallbackOnlyWhileTheSameOriginIsCurrent()
    {
        var guard = new Guard(); var ocr = new Ocr { Read = (_, _, _) => throw new ReadingOcrUnavailableException() }; var fallback = new Fallback();
        await Assert.ThrowsAsync<ReadingOcrUnavailableException>(() => Reader(guard, ocr, fallback).CapturePointerAsync(500, 300, default));
        Assert.Equal(1, fallback.Calls); Assert.Equal(1, ocr.Calls); Assert.Equal(1, guard.Stops); Assert.All(guard.Pixels, value => Assert.Equal(0, value));
        fallback = new(); Assert.Null(await Reader(null, new(), fallback).CapturePointerAsync(500, 300, default)); Assert.Equal(1, fallback.Calls);
    }
    [Fact]
    public async Task AcquisitionCancellationDoesNotTriggerFallbackAndRevokedCaptureClearsItsReply()
    {
        var fallback = new Fallback(); var ocr = new Ocr();
        var reader = new NativeReadingVisualPointerReader((_, _, _, _) => throw new OperationCanceledException(), () => Origin, fallback, ocr, Context());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.CapturePointerAsync(500, 300, default));
        Assert.Equal(0, fallback.Calls);
        var guard = new Guard(); guard.BeforeCapture = guard.Retire;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(guard, ocr, fallback).CapturePointerAsync(500, 300, default));
        Assert.All(guard.Pixels, value => Assert.Equal(0, value)); Assert.Equal(0, ocr.Calls); Assert.Equal(0, fallback.Calls);
        guard = new Guard(); guard.BeforeCurrent = guard.Retire;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(guard, ocr, fallback).CapturePointerAsync(500, 300, default));
        Assert.Equal(0, guard.Captures); Assert.Equal(0, fallback.Calls);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellationOrChangedOriginCannotFallThroughToAnotherAcquisition(bool originChanges)
    {
        using var cancel = new CancellationTokenSource(); var origin = Origin; var guard = new Guard(); var fallback = new Fallback();
        var ocr = new Ocr { Read = (_, _, _) => { if (originChanges) origin = origin with { FocusEpoch = 4 }; else cancel.Cancel(); throw new InvalidOperationException("synthetic"); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(guard, ocr, fallback, () => origin).CapturePointerAsync(500, 300, cancel.Token));
        Assert.Equal(0, fallback.Calls); Assert.All(guard.Pixels, value => Assert.Equal(0, value)); Assert.Equal(1, guard.Stops);
    }
    [Fact]
    public async Task InvalidatedLateOcrIsJoinedAndItsPixelsAreClearedBeforeCompletion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var guard = new Guard(); var fallback = new Fallback();
        var ocr = new Ocr { Read = async (bitmap, _, _) => { entered.SetResult(); await release.Task; return Lines(bitmap, false); } };
        var work = Reader(guard, ocr, fallback).CapturePointerAsync(500, 300, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); guard.Retire(); Assert.False(work.IsCompleted);
            release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.All(ocr.Crops, crop => Assert.All(crop.Bgra, value => Assert.Equal(0, value))); Assert.All(guard.Pixels, value => Assert.Equal(0, value));
            Assert.Equal(0, fallback.Calls); Assert.Equal(1, guard.Stops);
        }
        finally { release.TrySetResult(); try { await work; } catch (OperationCanceledException) { } }
    }
    [Fact]
    public async Task AnInteriorFragmentNeedsOnlyOneCropAndSecondPassFailureKeepsAnHonestFirstHit()
    {
        foreach (bool interior in new[] { false, true })
        {
            var guard = new Guard(); var fallback = new Fallback(); var ocr = new Ocr { Read = (bitmap, pass, _) =>
            {
                if (pass == 2) throw new TimeoutException("synthetic");
                var lines = Lines(bitmap, false);
                if (interior) lines = [lines[0] with { Words = lines[0].Words.Select((word, i) => i == 0 ? word with { Bounds = word.Bounds with { X = 80 } }
                    : i == 3 ? word with { Bounds = word.Bounds with { X = bitmap.Width - 100 } } : word).ToArray() }];
                return Task.FromResult(lines);
            } };
            await using var lease = await Reader(guard, ocr, fallback).CapturePointerAsync(500, 300, default); Assert.NotNull(lease);
            Assert.Equal("history", lease.Selection.Text); Assert.Equal(interior ? 1 : 2, ocr.Calls); Assert.Equal(0, fallback.Calls);
            Assert.All(ocr.Crops, crop => Assert.All(crop.Bgra, value => Assert.Equal(0, value)));
        }
    }
    [Fact]
    public async Task NoSpatialTargetCanExpandButAChangedRepeatedWordCannotReplaceTheFirstHit()
    {
        var guard = new Guard(); var ocr = new Ocr { Read = (bitmap, pass, _) => Task.FromResult(pass == 1 ? (IReadOnlyList<ReadingOcrLine>)[] : Lines(bitmap, true)) };
        await using (var lease = await Reader(guard, ocr, new()).CapturePointerAsync(500, 300, default))
        { Assert.NotNull(lease); Assert.Equal(2, ocr.Calls); Assert.Equal(600, ocr.Crops[1].Height); }
        guard = new(); ocr = new() { Read = (bitmap, pass, _) => Task.FromResult(pass == 1 ? Lines(bitmap, false)
            : (IReadOnlyList<ReadingOcrLine>)[new("other", [new("other", new(bitmap.Width / 2 - 35, bitmap.Height / 2 - 10, 70, 20))])]) };
        await using var first = await Reader(guard, ocr, new()).CapturePointerAsync(500, 300, default); Assert.NotNull(first);
        Assert.Equal("history", first.Selection.Text); Assert.Equal(2, ocr.Calls);
    }
    [Fact]
    public void CropCopiesExactRowsAndNeverResamplesOrMutatesTheOriginalFrame()
    {
        byte[] source = Enumerable.Range(0, 8 * 8 * 4).Select(index => (byte)index).ToArray();
        var crop = ReadingVisualReader.Crop(new(8, 8, source), new(-10, -20, 8, 8), new(-8, -17, 4, 4));
        Assert.Equal(source.AsSpan((3 * 8 + 2) * 4, 16).ToArray(), crop.Bgra[..16]);
        Assert.Equal(source.AsSpan((6 * 8 + 2) * 4, 16).ToArray(), crop.Bgra[^16..]);
        Array.Clear(crop.Bgra); Assert.NotEqual(0, source[(3 * 8 + 2) * 4]);
    }
}
