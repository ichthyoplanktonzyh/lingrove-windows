using System.Text.RegularExpressions;
using Cotabby.App.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Real optional-enrichment coordinator over owned arrays and synthetic native receipts.
/// No screenshot, UIA, hook, clipboard, model or external editor is used by these tests.</summary>
public sealed class ReadingSelectionVisualTests
{
    private static readonly ReadingForeground Origin = new(11, 42, 3, "synthetic");
    private static readonly ScreenRect Window = new(0, 0, 1000, 600);
    private static IReadOnlyList<LearningTextRange> Sentences(string text) => Regex.Matches(text, @"[^.!?]+[.!?]?")
        .Select(match => new LearningTextRange(match.Index, match.Length)).ToArray();
    private static ReadingContext Quote(string text) => ReadingContextPolicy.Resolve(new(text, text.IndexOf("history", StringComparison.Ordinal), 7,
        ReadingBoundary.Crop, ReadingBoundary.Crop, ReadingContextMethod.MarkerWindow), "history", Sentences)!;
    private sealed class Source : IReadingSelectionLease
    {
        internal ReadingSelection Value = new(Guid.NewGuid(), "history", 42, "field", 3, null, 7, false, true,
            InputCompositionState.Clear, 1, new(465, 290, 70, 20), Quote("your history safely."), SelectionAnchorId: Guid.NewGuid())
        { SelectionBounds = new(465, 290, 70, 20), OriginWindowBounds = Window, CapturedAt = DateTimeOffset.UnixEpoch };
        internal int Enrichments, Stops; internal Action? BeforeEnrich;
        public ReadingSelection Selection => Value;
        public int? PendingActivationKey { get; set; } = 0x54;
        public bool IsLive { get; private set; } = true;
        public event Action? Invalidated;
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(IsLive && !token.IsCancellationRequested);
        public Task EnrichAsync(CancellationToken token) { Enrichments++; BeforeEnrich?.Invoke(); return Task.CompletedTask; }
        public void BindGesture(ReadingSelectionGesture gesture) => Value = Value with { Gesture = gesture };
        public void ArmActivationKey(int key) => PendingActivationKey = key;
        public void Retire() { if (!IsLive) return; IsLive = false; Invalidated?.Invoke(); }
        public ValueTask DisposeAsync() { Stops++; Retire(); return ValueTask.CompletedTask; }
    }
    private sealed class Guard : IReadingVisualGuard
    {
        internal readonly byte[] Pixels = Enumerable.Repeat((byte)73, 1000 * 600 * 4).ToArray(); internal int Captures, Stops, Subscriptions;
        internal int? RetireOnSubscription;
        internal Action? BeforeAdoptionSubscription;
        private Action? _invalidated;
        public ReadingVisualFrame? Frame { get; } = new(500, 300, new(0, 0, 1000, 800), Window, 1);
        public ScreenRect? ForegroundBounds { get; set; } = Window;
        public ReadingScreenGuardEvidence? Evidence { get; } = new(42, "", 3, 0, new(0, 0, 1000, 800)) { WindowOrigin = new(Guid.NewGuid(), Window) };
        public bool IsLive { get; private set; } = true;
        public event Action? Invalidated
        {
            add { _invalidated += value; Subscriptions++; if (Subscriptions == 2) BeforeAdoptionSubscription?.Invoke();
                if (Subscriptions == RetireOnSubscription) Retire(); else if (!IsLive) value?.Invoke(); }
            remove => _invalidated -= value;
        }
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(IsLive && !token.IsCancellationRequested);
        public Task<ReadingScreenPixels?> CaptureRegionAsync(ScreenRect region, CancellationToken token)
        { Captures++; Assert.Equal(Window, region); return Task.FromResult<ReadingScreenPixels?>(new(new(1000, 600, Pixels), Frame!.Display)); }
        public void Retire() { if (!IsLive) return; IsLive = false; _invalidated?.Invoke(); }
        public ValueTask DisposeAsync() { Stops++; Retire(); return ValueTask.CompletedTask; }
    }
    private sealed class Ocr : IReadingOcr
    {
        internal int Calls; internal readonly List<ReadingBitmap> Inputs = [];
        internal Func<ReadingBitmap, int, CancellationToken, Task<IReadOnlyList<ReadingOcrLine>>> Read = (bitmap, pass, _) => Task.FromResult(Lines(bitmap, pass));
        public Task<IReadOnlyList<ReadingOcrLine>> RecognizeAsync(ReadingBitmap bitmap, CancellationToken token)
        { Inputs.Add(bitmap); return Read(bitmap, ++Calls, token); }
    }
    private static IReadOnlyList<ReadingOcrLine> Lines(ReadingBitmap bitmap, int pass)
    {
        string text = pass == 1 ? "Keep your history safely." : "Earlier sentence. Keep your history safely. Next sentence.";
        string[] values = text.Split(' '); int target = Array.IndexOf(values, "history");
        return [new(text, values.Select((value, index) => new ReadingOcrWord(value,
            new(pass == 1 ? index switch { 0 => 0, 1 => 50, 2 => bitmap.Width / 2 - 35, _ => bitmap.Width - 60 }
                : bitmap.Width / 2 - 35 + (index - target) * 60, bitmap.Height / 2 - 10, value == "history" ? 70 : 50, 20))).ToArray())];
    }
    private static ReadingSelectionVisualEnricher Owner(Guard? guard, Ocr ocr, Action? acquiring = null, Func<ReadingForeground?>? origin = null)
        => new((x, y, _, key) => { acquiring?.Invoke(); Assert.Equal(500, x); Assert.Equal(300, y); Assert.Equal(0x54, key); return Task.FromResult<IReadingVisualGuard?>(guard); },
            origin ?? (() => Origin), ocr, new(_ => 25), Sentences);
    [Fact]
    public async Task ExplicitEnrichmentUsesOneFrameAndKeepsTheNativeSelectionIdentityAndAuthority()
    {
        var source = new Source(); var original = source.Selection; var guard = new Guard(); var ocr = new Ocr(); int acquisitions = 0;
        await using var lease = Owner(guard, ocr, () => acquisitions++).Wrap(source);
        Assert.Equal(0, acquisitions); Assert.Equal(0, source.Enrichments); Assert.Same(original, lease.Selection);
        await lease.EnrichAsync(default);
        Assert.Equal(1, acquisitions); Assert.Equal(1, guard.Captures); Assert.Equal(2, ocr.Calls); Assert.Equal(1, source.Enrichments);
        Assert.Equal(original.Id, lease.Selection.Id); Assert.Equal(original.Text, lease.Selection.Text); Assert.Equal(original.SelectionAnchorId, lease.Selection.SelectionAnchorId);
        Assert.Equal(original.CapturedAt, lease.Selection.CapturedAt); Assert.Equal(original.Anchor, lease.Selection.Anchor);
        Assert.Equal(InputCompositionState.Clear, lease.Selection.Composition); Assert.Null(lease.Selection.ScreenReceipt);
        Assert.Equal("Keep your history safely.", lease.Selection.Context!.Text.Trim()); Assert.Equal(ReadingContextMethod.SelectionOcr, lease.Selection.Context.Method);
        Assert.True(ReadingVisualPolicy.Complete(lease.Selection.Context)); Assert.True(await lease.IsCurrentAsync(default)); Assert.Equal(0, guard.Stops);
        Assert.All(guard.Pixels, value => Assert.Equal(0, value)); Assert.All(ocr.Inputs, image => Assert.All(image.Bgra, value => Assert.Equal(0, value)));
        await lease.EnrichAsync(default); Assert.Equal(1, guard.Captures); Assert.Equal(2, ocr.Calls);
    }
    [Theory]
    [InlineData("geometry")] [InlineData("frame")] [InlineData("complete")]
    public async Task MissingGeometryWindowProofOrAlreadyCompleteNativeContextNeverCapturesPixels(string reason)
    {
        var source = new Source(); int acquisitions = 0;
        source.Value = reason switch { "geometry" => source.Value with { SelectionBounds = null }, "frame" => source.Value with { OriginWindowBounds = null },
            _ => source.Value with { Context = source.Value.Context! with { Left = ReadingBoundary.Sentence, Right = ReadingBoundary.Sentence } } };
        await using var lease = Owner(new(), new(), () => acquisitions++).Wrap(source); await lease.EnrichAsync(default);
        Assert.Equal(0, acquisitions); Assert.Same(source.Selection, lease.Selection); Assert.True(lease.IsLive);
    }
    [Fact]
    public async Task CompleteContextPublishedByNativeEnrichmentSkipsTheVisualStage()
    {
        var source = new Source(); source.BeforeEnrich = () => source.Value = source.Value with
            { Context = source.Value.Context! with { Left = ReadingBoundary.Sentence, Right = ReadingBoundary.Sentence } };
        int acquisitions = 0; await using var lease = Owner(new(), new(), () => acquisitions++).Wrap(source);
        await lease.EnrichAsync(default); Assert.Equal(1, source.Enrichments); Assert.Equal(0, acquisitions); Assert.Same(source.Selection, lease.Selection);
    }
    [Fact]
    public async Task ProgressIsOfferedOnlyAfterTheSingleFrameWasCapturedAndBeforeOcr()
    {
        var source = new Source(); var guard = new Guard(); var ocr = new Ocr(); bool progress = false;
        await using var lease = Owner(guard, ocr).Wrap(source);
        await lease.EnrichAsync(default, frame =>
        {
            Assert.Equal(1, guard.Captures); Assert.Equal(0, ocr.Calls); Assert.Equal(guard.Frame, frame);
            Assert.Contains((byte)73, guard.Pixels); progress = true; return Task.CompletedTask;
        });
        Assert.True(progress); Assert.Equal(1, guard.Captures); Assert.Equal(2, ocr.Calls);
    }
    [Fact]
    public async Task ExternalCancellationJoinsAnUncooperativeOcrWithoutPublishingContext()
    {
        using var cancel = new CancellationTokenSource(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source(); var guard = new Guard(); var ocr = new Ocr { Read = async (bitmap, _, _) => { entered.SetResult(); await release.Task; return Lines(bitmap, 1); } };
        await using var lease = Owner(guard, ocr).Wrap(source); var original = source.Selection; var work = lease.EnrichAsync(cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel(); Assert.False(work.IsCompleted); release.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work); Assert.Same(original, lease.Selection);
            Assert.Equal(1, guard.Stops); Assert.All(guard.Pixels, value => Assert.Equal(0, value));
            Assert.All(ocr.Inputs, image => Assert.All(image.Bgra, value => Assert.Equal(0, value)));
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task OptionalOcrFailureOrConflictingContextRetainsOriginalNativeMaterial()
    {
        foreach (bool conflict in new[] { false, true })
        {
            var source = new Source(); if (conflict) source.Value = source.Value with { Context = Quote("the search history today.") };
            var original = source.Selection; var guard = new Guard(); var ocr = new Ocr();
            if (!conflict) ocr.Read = (_, _, _) => throw new ReadingOcrUnavailableException();
            await using var lease = Owner(guard, ocr).Wrap(source); await lease.EnrichAsync(default);
            Assert.Same(original, lease.Selection); Assert.True(lease.IsLive); Assert.Equal(1, guard.Stops); Assert.True(await lease.IsCurrentAsync(default));
            Assert.All(guard.Pixels, value => Assert.Equal(0, value));
        }
    }
    [Fact]
    public async Task AdoptedWindowInvalidationRetiresBothSourcesAndLateSubscribersAreNotStranded()
    {
        var source = new Source(); var guard = new Guard(); await using var lease = Owner(guard, new()).Wrap(source); await lease.EnrichAsync(default);
        int notifications = 0; lease.Invalidated += () => notifications++; guard.Retire();
        Assert.False(lease.IsLive); Assert.False(source.IsLive); Assert.Equal(1, notifications);
        lease.Invalidated += () => notifications++; Assert.Equal(2, notifications);
        Assert.False(await lease.IsCurrentAsync(default));
    }
    [Fact]
    public async Task ShutdownDuringUncooperativeOcrJoinsLateWorkAndSupportsReentrantNotificationDrain()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source(); var guard = new Guard(); var ocr = new Ocr { Read = async (bitmap, _, _) => { entered.SetResult(); await release.Task; return Lines(bitmap, 1); } };
        var lease = Owner(guard, ocr).Wrap(source); var work = lease.EnrichAsync(default); bool callbackReturned = false;
        lease.Invalidated += () => { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); callbackReturned = true; };
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); source.Retire(); Assert.True(callbackReturned); Assert.False(lease.IsLive);
            var stop = lease.DisposeAsync().AsTask(); Assert.False(stop.IsCompleted); Assert.False(work.IsCompleted); Assert.Equal(0, source.Stops);
            release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work); await stop;
            Assert.Equal(1, source.Stops); Assert.Equal(1, guard.Stops); Assert.All(guard.Pixels, value => Assert.Equal(0, value));
            Assert.All(ocr.Inputs, image => Assert.All(image.Bgra, value => Assert.Equal(0, value)));
        }
        finally { release.TrySetResult(); await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task AmbiguousSecondCropDiscardsTheFirstVisualCandidateInsteadOfKeepingARepeatedWord()
    {
        var source = new Source(); var guard = new Guard(); var ocr = new Ocr { Read = (bitmap, pass, _) =>
        {
            if (pass == 1) return Task.FromResult(Lines(bitmap, 1));
            string text = "Keep your history safely. Keep your history safely.";
            string[] words = text.Split(' ');
            return Task.FromResult<IReadOnlyList<ReadingOcrLine>>([new(text, words.Select((word, index) => new ReadingOcrWord(word,
                new(word == "history" ? bitmap.Width / 2 - 35 : 10 + index * 80, bitmap.Height / 2 - 10, word == "history" ? 70 : 50, 20))).ToArray())]);
        } };
        var original = source.Selection; await using var lease = Owner(guard, ocr).Wrap(source); await lease.EnrichAsync(default);
        Assert.Equal(2, ocr.Calls); Assert.Same(original, lease.Selection); Assert.Equal(1, guard.Stops); Assert.True(lease.IsLive);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeRetirementOrOriginChangeDuringOcrCannotPublishAVisualContext(bool originChange)
    {
        var source = new Source(); var origin = Origin; var guard = new Guard(); var ocr = new Ocr { Read = (bitmap, _, _) =>
        { if (originChange) origin = origin with { FocusEpoch = 4 }; else source.Retire(); return Task.FromResult(Lines(bitmap, 1)); } };
        await using var lease = Owner(guard, ocr, origin: () => origin).Wrap(source); var original = source.Selection;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.EnrichAsync(default)); Assert.Same(original, lease.Selection);
        Assert.All(guard.Pixels, value => Assert.Equal(0, value)); Assert.Equal(1, guard.Stops);
    }
    [Fact]
    public async Task RetirementDuringGuardAdoptionCannotPublishAndDisposesTheTransferredGuardExactlyOnce()
    {
        var source = new Source(); var guard = new Guard { RetireOnSubscription = 2 }; var lease = Owner(guard, new()).Wrap(source);
        var original = source.Selection; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.EnrichAsync(default));
        Assert.False(lease.IsLive); Assert.Same(original, lease.Selection); await lease.DisposeAsync();
        Assert.Equal(1, guard.Stops); Assert.Equal(1, source.Stops);
    }
    [Fact]
    public async Task CancelAsyncBeforeLinkedPropagationCannotPublishContextDuringGuardAdoption()
    {
        using var cancel = new CancellationTokenSource(); using var release = new ManualResetEventSlim();
        CancellationTokenRegistration delayed = default; Task? cancellation = null;
        var source = new Source(); var guard = new Guard { BeforeAdoptionSubscription = () =>
        {
            delayed = cancel.Token.Register(() => release.Wait()); // Later callback blocks propagation to the existing linked token.
            cancellation = cancel.CancelAsync();
        } };
        var lease = Owner(guard, new()).Wrap(source); var original = source.Selection;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.EnrichAsync(cancel.Token).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(cancel.IsCancellationRequested); Assert.False(lease.IsLive); Assert.Same(original, lease.Selection);
        }
        finally { release.Set(); if (cancellation is not null) await cancellation; delayed.Dispose(); await lease.DisposeAsync(); }
        Assert.Equal(1, guard.Stops); Assert.Equal(1, source.Stops);
    }
}
