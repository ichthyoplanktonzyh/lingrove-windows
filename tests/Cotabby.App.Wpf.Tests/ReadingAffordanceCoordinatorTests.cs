using System.Windows.Threading;
using Cotabby.App.Reading;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Reading;

namespace Cotabby.App.Wpf.Tests;

[Collection("WPF views")]
public sealed class ReadingAffordanceCoordinatorTests(WpfDispatcher dispatcher)
{
    [Fact]
    public Task GestureCapturesOnceAndExplicitActivationTransfersSameLease() => dispatcher.RunAsync(async () =>
    {
        var mouse = new Mouse(); var reader = new Reader(); var origin = new ReadingForeground(10, 42, 3, "editor");
        Action? click = null; IReadingSelectionLease? activated = null; int withdrawals = 0;
        await using var owner = new ReadingAffordanceCoordinator(Dispatcher.CurrentDispatcher, mouse, reader,
            () => origin, _ => true, _ => true, (_, _) => false, () => withdrawals++,
            (lease, _) => activated = lease, (_, _, _, action) => click = action, () => click = null);
        mouse.Emit(new(ReadingPointerKind.LeftDown, 1, 1, 0, 1));
        mouse.Emit(new(ReadingPointerKind.LeftUp, 12, 1, 1, 2));
        await Until(() => click is not null);
        Assert.Equal(1, reader.Captures); Assert.Equal(0, reader.Lease.Enrichments); Assert.Null(activated);
        var gesture = Assert.IsType<ReadingSelectionGesture>(reader.Lease.Selection.Gesture);
        Assert.Equal(reader.Lease.Selection.Id, gesture.SelectionId); Assert.Equal(ReadingGestureKind.Drag, gesture.Kind);
        Assert.Equal((1d, 1d, 12d, 1d), (gesture.StartX, gesture.StartY, gesture.EndX, gesture.EndY));
        Assert.NotNull(ReadingGesturePolicy.SearchBounds(reader.Lease.Selection, now: DateTimeOffset.UtcNow));
        owner.ArmShortcut(origin, 0x54); Assert.Equal(0x54, reader.Lease.ArmedKey);
        var first = click!; first(); first();
        Assert.Same(reader.Lease, activated); Assert.Equal(1, reader.Captures); Assert.True(withdrawals > 0);
        await activated!.DisposeAsync();
    });

    [Fact]
    public Task OriginChangeOrRetirementDuringDelayCannotCapture() => dispatcher.RunAsync(async () =>
    {
        var mouse = new Mouse(); var reader = new Reader(); var origin = new ReadingForeground(10, 42, 3, "editor");
        int shown = 0;
        await using var owner = new ReadingAffordanceCoordinator(Dispatcher.CurrentDispatcher, mouse, reader,
            () => origin, _ => true, _ => true, (_, _) => false, () => { }, (_, _) => throw new InvalidOperationException(),
            (_, _, _, _) => shown++, () => { });
        mouse.Emit(new(ReadingPointerKind.LeftDown, 0, 0, 0, 1)); mouse.Emit(new(ReadingPointerKind.LeftUp, 10, 0, 1, 2));
        await Dispatcher.Yield(DispatcherPriority.Background);
        origin = origin with { FocusEpoch = 4 };
        await Task.Delay(250); Assert.Equal(0, reader.Captures); Assert.Equal(0, shown);
        mouse.Emit(new(ReadingPointerKind.LeftDown, 0, 0, 0, 3)); mouse.Emit(new(ReadingPointerKind.LeftUp, 10, 0, 1, 4));
        await Dispatcher.Yield(DispatcherPriority.Background); owner.Invalidate();
        await Task.Delay(250); Assert.Equal(0, reader.Captures); Assert.Equal(0, shown);
    });
    [Fact]
    public Task ShortcutJoinsPendingGestureWithoutSecondCapture() => dispatcher.RunAsync(async () =>
    {
        var mouse = new Mouse(); var reader = new Reader { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var origin = new ReadingForeground(10, 42, 3, "editor"); int activations = 0;
        await using var owner = new ReadingAffordanceCoordinator(Dispatcher.CurrentDispatcher, mouse, reader,
            () => origin, _ => true, _ => true, (_, _) => false, () => { },
            (_, _) => activations++, (_, _, _, _) => { }, () => { });
        mouse.Emit(new(ReadingPointerKind.LeftDown, 0, 0, 0, 1)); mouse.Emit(new(ReadingPointerKind.LeftUp, 10, 0, 1, 2));
        await Until(() => reader.Captures == 1);
        Assert.True(owner.TryConsumeOrWait(origin, out var pending)); Assert.False(pending.IsCompleted);
        reader.Gate.SetResult(); Assert.True(await pending); Assert.Equal(1, activations); Assert.Equal(1, reader.Captures);
    });
    [Theory]
    [InlineData(2)] [InlineData(3)]
    public Task OnlyAnActualDoubleClickBindsAnUndraggedGesture(int clickCount) => dispatcher.RunAsync(async () =>
    {
        var mouse = new Mouse(); var reader = new Reader(); var origin = new ReadingForeground(10, 42, 3, "editor"); Action? click = null;
        await using var owner = new ReadingAffordanceCoordinator(Dispatcher.CurrentDispatcher, mouse, reader, () => origin,
            _ => true, _ => true, (_, _) => false, () => { }, (_, _) => { }, (_, _, _, action) => click = action, () => click = null);
        mouse.Emit(new(ReadingPointerKind.LeftDown, 20, 30, clickCount, 1)); mouse.Emit(new(ReadingPointerKind.LeftUp, 20, 30, clickCount, 2));
        await Until(() => click is not null); Assert.Equal(1, reader.Captures); Assert.Equal(0, reader.Lease.Enrichments);
        if (clickCount == 2)
        {
            var gesture = Assert.IsType<ReadingSelectionGesture>(reader.Lease.Selection.Gesture); Assert.Equal(ReadingGestureKind.DoubleClick, gesture.Kind);
            Assert.Equal((20d, 30d, 20d, 30d), (gesture.StartX, gesture.StartY, gesture.EndX, gesture.EndY));
        }
        else Assert.Null(reader.Lease.Selection.Gesture);
    });
    private static async Task Until(Func<bool> condition)
    { for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }
    private sealed class Mouse : IReadingPointerMonitor
    {
        public event Action<ReadingPointerEvent>? Pointer;
        public void Emit(ReadingPointerEvent e) => Pointer?.Invoke(e);
        public void Start() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Reader : IReadingSelectionReader
    {
        public int Captures; public readonly Lease Lease = new(); public TaskCompletionSource? Gate;
        public async Task<IReadingSelectionLease?> CaptureAsync(CancellationToken token, int? initiatingVirtualKey = null)
        { Captures++; if (Gate is not null) await Gate.Task.WaitAsync(token); Lease.CapturedNow(); return Lease; }
    }
    private sealed class Lease : IReadingSelectionLease
    {
        private bool _retired;
        public int Enrichments; public int ArmedKey;
        public ReadingSelection Selection { get; private set; } = new(Guid.NewGuid(), "bank", 42, "field", 3, 0, 4, false, true,
            InputCompositionState.Clear, 1, ScreenRect.Empty);
        public bool IsLive => !_retired;
        public event Action? Invalidated { add { } remove { } }
        public Task<bool> IsCurrentAsync(CancellationToken token) => Task.FromResult(IsLive);
        public Task EnrichAsync(CancellationToken token) { Enrichments++; return Task.CompletedTask; }
        public void ArmActivationKey(int virtualKey) => ArmedKey = virtualKey;
        public void BindGesture(ReadingSelectionGesture gesture) => Selection = Selection with { Gesture = gesture };
        public void CapturedNow() => Selection = Selection with { CapturedAt = DateTimeOffset.UtcNow };
        public void Retire() => _retired = true;
        public ValueTask DisposeAsync() { Retire(); return ValueTask.CompletedTask; }
    }
}
