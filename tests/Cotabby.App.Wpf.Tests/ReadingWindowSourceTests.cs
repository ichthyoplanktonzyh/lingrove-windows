using System.Runtime.Versioning;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Focus;
using Cotabby.Win32.Reading;

namespace Cotabby.App.Wpf.Tests;

/// <summary>Only synthetic origin/password metadata, local event objects and owned BGRA arrays.
/// These tests never call a screen, UIA focus API, OCR provider or global hook.</summary>
[SupportedOSPlatform("windows8.1")]
public sealed class ReadingWindowSourceTests
{
    [Fact]
    public async Task WindowCaptureNeedsNoEditableFieldAndDoesNotReadPixelsBeforeTheGesture()
    {
        var effects = new Effects(); await using var source = effects.Source();
        await using var lease = await source.CaptureAsync(default, 0x54); Assert.NotNull(lease);
        Assert.Equal(0, effects.Captures); Assert.Equal("", lease.Evidence!.FieldId); Assert.Equal(0, lease.Evidence.CompositionSequence);
        Assert.NotNull(lease.Evidence.WindowOrigin); Assert.True(await lease.IsCurrentAsync(default));
        var pixels = await lease.CaptureRegionAsync(new(500, 30, 16, 16), default);
        Assert.NotNull(pixels); Assert.Equal(1, effects.Captures);
        Assert.True(lease.Evidence.WindowOrigin!.WindowBounds.Right < 500);
        Array.Clear(pixels.Bitmap.Bgra); Assert.Equal(0, effects.Keyboard.Starts);
    }
    [Fact]
    public async Task MissingPasswordEvidenceOrStaleOriginCannotAcquireALease()
    {
        var effects = new Effects { SecureOrUnknown = true }; await using var source = effects.Source();
        Assert.Null(await source.CaptureAsync(default)); Assert.Equal(0, effects.Captures); Assert.Equal(0, effects.Keyboard.Subscribers); Assert.Null(effects.Focus);
        effects.SecureOrUnknown = false; effects.BeforeSecurity = () => effects.Current = effects.Current with { FocusEpoch = 2 };
        Assert.Null(await source.CaptureAsync(default)); Assert.Equal(0, effects.Captures); Assert.Equal(0, effects.Keyboard.Subscribers);
    }
    [Theory]
    [InlineData("frame")] [InlineData("password")] [InlineData("focus")]
    public async Task ChangedWindowOrSecurityDiscardsCapturedPixelsAndRetiresTheLease(string changed)
    {
        var effects = new Effects(); await using var source = effects.Source();
        await using var lease = await source.CaptureAsync(default); Assert.NotNull(lease);
        effects.AfterPixels = () =>
        {
            if (changed == "frame") effects.Window = new(1, 0, 200, 200);
            else if (changed == "password") effects.SecureOrUnknown = true;
            else effects.Current = effects.Current with { Window = 22 };
        };
        Assert.Null(await lease.CaptureRegionAsync(new(500, 30, 16, 16), default));
        Assert.NotNull(effects.Pixels); Assert.All(effects.Pixels, value => Assert.Equal(0, value)); Assert.False(lease.IsLive);
    }
    [Fact]
    public async Task RegionBudgetIsCheckedBeforeAnyPixelEffect()
    {
        var effects = new Effects(); await using var source = effects.Source();
        await using var lease = await source.CaptureAsync(default); Assert.NotNull(lease);
        Assert.Null(await lease.CaptureRegionAsync(new(0, 0, 2, 2), default));
        Assert.Null(await lease.CaptureRegionAsync(new(1900, 0, 200, 16), default));
        Assert.Equal(0, effects.Captures); Assert.True(lease.IsLive);
    }
    [Fact]
    public async Task ShutdownJoinsAnAlreadyStartedPixelReadAndClearsItsLateResult()
    {
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var effects = new Effects { BeforePixels = () => { entered.SetResult(); release.Wait(); } }; var source = effects.Source();
        try
        {
            await using var lease = await source.CaptureAsync(default); Assert.NotNull(lease);
            var capture = lease.CaptureRegionAsync(new(500, 30, 16, 16), default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stop = source.DisposeAsync().AsTask(); Assert.False(stop.IsCompleted); release.Set();
            Assert.Null(await capture); await stop;
            Assert.All(effects.Pixels!, value => Assert.Equal(0, value)); Assert.Equal(0, effects.Keyboard.Subscribers); Assert.Null(effects.Focus);
        }
        finally { release.Set(); await source.DisposeAsync(); }
    }
    [Fact]
    public async Task FocusNotificationDuringRegistrationCannotLeaveAKeyboardSubscriber()
    {
        var effects = new Effects { RetireDuringSubscribe = true }; await using var source = effects.Source();
        Assert.Null(await source.CaptureAsync(default)); Assert.Null(effects.Focus); Assert.Equal(0, effects.Keyboard.Subscribers);
    }
    [Fact]
    public async Task InitialChordReleasesAreIgnoredButLaterInputRetiresAndLateSubscribersAreNotified()
    {
        var effects = new Effects(); await using var source = effects.Source();
        await using var lease = await source.CaptureAsync(default, 0x54); Assert.NotNull(lease);
        effects.Keyboard.Raise(0x54, false); effects.Keyboard.Raise(0x11, false); Assert.True(lease.IsLive);
        effects.Keyboard.Raise(0x41, true); Assert.False(lease.IsLive);
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); lease.Invalidated += notified.SetResult;
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact]
    public async Task PointerAcquisitionRetiredByInputCannotBecomeAnOrdinaryMissingEvidenceReply()
    {
        var effects = new Effects(); effects.BeforeSecurity = () => effects.Keyboard.Raise(0x41, true);
        await using var source = effects.Source();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.CapturePointerAsync(100, 100, default));
        Assert.Equal(0, effects.Captures); Assert.Equal(0, effects.Keyboard.Subscribers); Assert.Null(effects.Focus);
    }
    [Theory]
    [InlineData("point")] [InlineData("scale")]
    public async Task PointerFramesRequireCurrentPointSecurityAndFrozenDisplayScale(string changed)
    {
        var effects = new Effects(); await using var source = effects.Source();
        await using var lease = await source.CapturePointerAsync(100, 100, default); Assert.NotNull(lease);
        Assert.NotNull(lease.PointerFrame); Assert.Equal(0, effects.Captures);
        Assert.Null(await lease.CaptureRegionAsync(new(500, 30, 16, 16), default)); Assert.Equal(0, effects.Captures);
        effects.AfterPixels = () => { if (changed == "point") effects.PointIdentity = "other"; else effects.Scale = 2; };
        Assert.Null(await lease.CaptureRegionAsync(lease.PointerFrame.Bounds, default));
        Assert.All(effects.Pixels!, value => Assert.Equal(0, value)); Assert.False(lease.IsLive);
    }
    [Fact]
    public async Task PointedWindowHasItsOwnFrameAndAnIdenticallySizedReplacementCannotReuseItsReceipt()
    {
        var effects = new Effects { PointWindow = 22, PointBounds = new(10, 10, 150, 150) };
        await using var source = effects.Source(); await using var lease = await source.CapturePointerAsync(100, 100, default);
        Assert.NotNull(lease); Assert.Equal(effects.PointBounds, lease.Evidence!.WindowOrigin!.WindowBounds);
        Assert.True(await lease.IsCurrentAsync(default));
        effects.AfterPixels = () => effects.PointWindow = 33;
        Assert.Null(await lease.CaptureRegionAsync(lease.PointerFrame!.Bounds, default));
        Assert.False(lease.IsLive); Assert.All(effects.Pixels!, value => Assert.Equal(0, value));
    }
    private sealed class Effects
    {
        internal ReadingForeground Current = new(11, Environment.ProcessId + 1000, 1, "owned-fake");
        internal ScreenRect Window = new(0, 0, 200, 200);
        internal readonly Hook Keyboard = new(); internal Action<nint>? Focus;
        internal bool SecureOrUnknown, RetireDuringSubscribe; internal int Captures; internal byte[]? Pixels;
        internal string PointIdentity = "point"; internal double Scale = 1;
        internal nint PointWindow = 11; internal ScreenRect? PointBounds;
        internal Action? BeforeSecurity, BeforePixels, AfterPixels;
        internal Win32ReadingWindowSource Source() => new(() => Current, Keyboard,
            handler => { Focus += handler; if (RetireDuringSubscribe) handler(11); }, handler => Focus -= handler,
            (origin, allowed) => { BeforeSecurity?.Invoke(); return !SecureOrUnknown && allowed() ? new ReadingScreenSecurity(origin.ProcessId, "security-identity") : null; },
            _ => Window, () => new(-1000, 0, 3000, 1000), (_, region, _) =>
            {
                BeforePixels?.Invoke(); Captures++; Pixels = Enumerable.Repeat((byte)73, checked((int)region.Width * (int)region.Height * 4)).ToArray();
                AfterPixels?.Invoke(); return new(new((int)region.Width, (int)region.Height, Pixels), new(0, 0, 2000, 1000));
            }, (origin, _, allowed) => { BeforeSecurity?.Invoke(); return !SecureOrUnknown && allowed() ? new(origin.ProcessId, PointIdentity) : null; },
            (_, point) => ReadingVisualPolicy.Snapshot(point.X, point.Y, new(0, 0, 2000, 1000), PointBounds ?? Window, Scale) is { } frame
                ? new(PointWindow, PointBounds ?? Window, new(point.X, point.Y, new(0, 0, 2000, 1000), frame, Scale)) : null);
    }
    private sealed class Hook : IKeyboardHook
    {
        private EventHandler<KeyEventArgs>? _events;
        public event EventHandler<KeyEventArgs>? KeyEvent { add { _events += value; Subscribers++; } remove { if (_events?.GetInvocationList().Contains(value) == true) { _events -= value; Subscribers--; } } }
        internal int Starts, Subscribers;
        internal void Raise(int key, bool down) => _events?.Invoke(this, new(new KeyboardEvent { Kind = KeyKind.Character, Character = 'x', VirtualKey = key, IsKeyDown = down }));
        public void Start() => Starts++; public void Stop() { } public void Dispose() { }
    }
}
