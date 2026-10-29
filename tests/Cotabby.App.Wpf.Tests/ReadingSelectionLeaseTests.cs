using System.Runtime.Versioning;
using Cotabby.Core.Input;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Cotabby.Win32.Focus;
using Cotabby.Win32.Reading;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cotabby.App.Wpf.Tests;

[SupportedOSPlatform("windows8.1")]
public sealed class ReadingSelectionLeaseTests
{
    [Fact]
    public async Task LivePermissionClosesBeforeNotificationAndOnEpochChange()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        long epoch = 3;
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), new FakeEvidence { Block = false }, 10, 42, 3, null,
            () => Interlocked.Read(ref epoch), () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        using var committed = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        lease.RetireCommittedForTest = () => { committed.Set(); release.Wait(); };
        var retiring = Task.Run(lease.Retire);
        try { Assert.True(committed.Wait(TimeSpan.FromSeconds(5))); Assert.False(lease.IsLive); }
        finally { release.Set(); await retiring; }
        await using var next = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), new FakeEvidence { Block = false }, 10, 42, 3, null,
            () => Interlocked.Read(ref epoch), () => 10, _ => 42);
        Assert.True(next.Capture(CancellationToken.None)); Assert.True(next.IsLive);
        Interlocked.Exchange(ref epoch, 4); Assert.False(next.IsLive);
    }

    [Fact]
    public async Task LivePermissionRejectsForegroundAndProcessChangesWithoutEvents()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        nint foreground = 10; int pid = 42;
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), new FakeEvidence { Block = false }, 10, 42, 3, null,
            () => 3, () => foreground, _ => pid);
        Assert.True(lease.Capture(CancellationToken.None)); Assert.True(lease.IsLive);
        foreground = 11; Assert.False(lease.IsLive);
        foreground = 10; pid = 43; Assert.False(lease.IsLive);
        pid = 42; Assert.True(lease.IsLive);
    }

    [Fact]
    public async Task NativeRetirementWithdrawsPermissionBeforeNotification()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false };
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None)); Assert.True(lease.IsLive);
        Interlocked.Exchange(ref evidence.Last!.Active, 0);
        Assert.False(lease.IsLive);
    }

    [Fact]
    public async Task RetiredDuringExplicitContextNeverPublishesContext()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false, BlockContext = true, NoContext = false };
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        Assert.False(evidence.ContextEntered.IsSet);
        var enrichment = lease.EnrichAsync(CancellationToken.None);
        try
        {
            Assert.True(evidence.ContextEntered.Wait(TimeSpan.FromSeconds(5)));
            var retire = Task.Run(lease.Retire); await retire.WaitAsync(TimeSpan.FromSeconds(2));
            evidence.ContextRelease.Set(); await enrichment.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(lease.IsLive);
            Assert.Null(lease.Selection.Context);
        }
        finally { evidence.ContextRelease.Set(); }
    }
    [Fact]
    public async Task VerifiedBoundedWindowIsResolvedAroundTheExactSelection()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false, NoContext = false };
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None)); Assert.False(evidence.ContextEntered.IsSet);
        await lease.EnrichAsync(CancellationToken.None);
        var context = Assert.IsType<ReadingContext>(lease.Selection.Context);
        Assert.True(ReadingSelectionPolicy.ValidContext(context, lease.Selection.Text));
        Assert.Equal("selected", context.Text.Substring(context.TargetStart, context.TargetLength));
    }

    [Fact]
    public async Task TimedOutContextIsDiscardedAndWorkerIsDrained()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false, BlockContext = true, NoContext = false };
        var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        try
        {
            Assert.True(lease.Capture(CancellationToken.None));
            var enrichment = lease.EnrichAsync(CancellationToken.None);
            Assert.True(evidence.ContextEntered.Wait(TimeSpan.FromSeconds(5)));
            await enrichment.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(lease.Selection.Context);
            var disposal = lease.DisposeAsync().AsTask(); Assert.False(disposal.IsCompleted);
            evidence.ContextRelease.Set(); await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { evidence.ContextRelease.Set(); await lease.DisposeAsync(); }
    }

    [Fact]
    public async Task OptionalContextFailureKeepsVerifiedSelection()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false, NoContext = true };
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None)); await lease.EnrichAsync(CancellationToken.None); Assert.Null(lease.Selection.Context);
        Assert.Equal("selected", lease.Selection.Text);
    }
    [Fact]
    public async Task PointerReceiptUsesNativePointAndDoesNotPretendToBeSelection()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false };
        var frame = new Win32ReadingSelectionLease.HostFrame(0, 0, 100, 100);
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42, new System.Drawing.Point(14, 19), currentFrame: _ => frame);
        Assert.True(lease.Capture(CancellationToken.None));
        Assert.Equal(new System.Drawing.Point(14, 19), evidence.Pointer);
        Assert.Equal(ReadingSourceKind.Pointer, lease.Selection.Source);
        Assert.False(evidence.ContextEntered.IsSet); await lease.EnrichAsync(CancellationToken.None); Assert.True(evidence.ContextEntered.IsSet);
        frame = frame with { Right = 101 }; Assert.False(lease.IsLive);
    }
    [Fact]
    public async Task ScreenGuardDoesNotInventAnOccurrenceOrReadContext()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false };
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42, screenGuard: true, currentFrame: _ => new(0, 0, 100, 100));
        Assert.True(lease.Capture(CancellationToken.None)); Assert.True(evidence.GuardCaptured);
        var proof = Assert.IsType<ReadingScreenGuardEvidence>(lease.ScreenGuardEvidence);
        Assert.Equal("owned-fake", proof.FieldId); Assert.Equal(3, proof.FocusEpoch); Assert.True(proof.CompositionSequence > 0);
        Assert.Throws<InvalidOperationException>(() => lease.Selection); Assert.False(evidence.ContextEntered.IsSet);
        Assert.True(await lease.IsCurrentAsync(CancellationToken.None)); lease.Retire(); Assert.False(lease.IsLive); Assert.Null(lease.ScreenGuardEvidence);
    }
    [Fact]
    public async Task InitiatingKeyReleaseAfterCaptureDoesNotRetireTheReceipt()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance); var keyboard = new FakeKeyboard();
        await using var lease = new Win32ReadingSelectionLease(focus, keyboard, new FakeEvidence { Block = false }, 10, 42, 3, 0x54,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        keyboard.Emit(0x54, false); Assert.True(lease.IsLive);
        keyboard.Emit(0x10, false); keyboard.Emit(0x11, false); keyboard.Emit(0x12, false); Assert.True(lease.IsLive);
        keyboard.Emit(0x54, false); Assert.False(lease.IsLive);
    }
    [Fact]
    public async Task PendingReceiptAcceptsOnlyItsArmedSuppressedShortcut()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance); var keyboard = new FakeKeyboard();
        await using var lease = new Win32ReadingSelectionLease(focus, keyboard, new FakeEvidence { Block = false }, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None)); lease.ArmActivationKey(0x54);
        keyboard.Emit(0x54, true, suppressed: true); keyboard.Emit(0x54, false); Assert.True(lease.IsLive);
        keyboard.Emit(0x54, true); Assert.False(lease.IsLive);
    }
    [Fact]
    public async Task ExplicitRegionCapturesOnceAndRetirementDiscardsPixels()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false };
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42, screenGuard: true, currentFrame: _ => new(0, 0, 100, 100));
        Assert.True(lease.Capture(CancellationToken.None));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        byte[] pixels = Enumerable.Repeat((byte)42, 16 * 16 * 4).ToArray(); int calls = 0;
        lease.CapturePixelsForTest = (_, _, _, _, permit) =>
        { Interlocked.Increment(ref calls); entered.Set(); release.Wait(); return (new ReadingBitmap(16, 16, pixels), new ScreenRect(0, 0, 100, 100)); };
        var capture = lease.CaptureRegionAsync(new ScreenRect(10, 10, 16, 16), CancellationToken.None);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(await lease.CaptureRegionAsync(new ScreenRect(10, 10, 16, 16), CancellationToken.None));
            lease.Retire(); release.Set(); Assert.Null(await capture); Assert.All(pixels, value => Assert.Equal(0, value)); Assert.Equal(1, calls);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task CompletedValidationCannotPublishAfterRetirement()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false };
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lease.BeforeValidationPublicationForTest = () => { entered.TrySetResult(); return release.Task; };
        var validation = lease.IsCurrentAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lease.Retire();
        release.SetResult();
        Assert.False(await validation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RetiredLeaseReplaysNotificationToLateAndConcurrentSubscribers()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), new FakeEvidence { Block = false }, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lease.Invalidated += () => first.TrySetResult();
        lease.Retire();
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int calls = 0;
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            lease.Invalidated += () => { if (Interlocked.Increment(ref calls) == 16) all.TrySetResult(); })));
        await all.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(16, calls);
    }

    [Fact]
    public async Task SubscribersRacingRetirementAllReceiveTerminalNotification()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), new FakeEvidence { Block = false }, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        using var committed = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        lease.RetireCommittedForTest = () => { committed.Set(); release.Wait(); };
        var retiring = Task.Run(lease.Retire);
        Assert.True(committed.Wait(TimeSpan.FromSeconds(5)));
        int calls = 0;
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscribers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            lease.Invalidated += () => { if (Interlocked.Increment(ref calls) == 16) all.TrySetResult(); })).ToArray();
        try
        {
            release.Set();
            await Task.WhenAll(subscribers.Append(retiring)).WaitAsync(TimeSpan.FromSeconds(5));
            await all.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(16, calls);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task DisposeCannotPassRetirementBeforeCleanupAndNotificationAreRegistered()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false };
        var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        using var committed = new ManualResetEventSlim();
        using var releaseRetire = new ManualResetEventSlim();
        using var notificationEntered = new ManualResetEventSlim();
        using var releaseNotification = new ManualResetEventSlim();
        lease.RetireCommittedForTest = () => { committed.Set(); releaseRetire.Wait(); };
        lease.Invalidated += () => { notificationEntered.Set(); releaseNotification.Wait(); };
        var retiring = Task.Run(lease.Retire);
        Assert.True(committed.Wait(TimeSpan.FromSeconds(5)));
        var disposal = Task.Run(async () => await lease.DisposeAsync());
        try
        {
            Assert.False(disposal.IsCompleted);
            releaseRetire.Set();
            await retiring.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(notificationEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(disposal.IsCompleted);
            releaseNotification.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, evidence.RetireCalls);
            Assert.True(evidence.Drained);
        }
        finally { releaseRetire.Set(); releaseNotification.Set(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReentrantOwnerDisposalDoesNotSkipExternalNotificationDrain(bool ownerFirst)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var keyboard = new FakeKeyboard();
        var reader = new Win32ReadingSelectionReader(focus, keyboard);
        var lease = new Win32ReadingSelectionLease(focus, keyboard, new FakeEvidence { Block = false }, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        reader.AttachForTest(lease);
        using var returned = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        lease.Invalidated += () => { reader.DisposeAsync().AsTask().GetAwaiter().GetResult(); returned.Set(); release.Wait(); };
        Task? external = ownerFirst ? reader.DisposeAsync().AsTask() : null;
        if (!ownerFirst) lease.Retire();
        try
        {
            Assert.True(returned.Wait(TimeSpan.FromSeconds(5)));
            external ??= reader.DisposeAsync().AsTask();
            Assert.False(external.IsCompleted);
            release.Set();
            await external.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task HeldValidationRetiresAndDisposeJoinsUnderlyingWork()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var keyboard = new FakeKeyboard();
        var evidence = new FakeEvidence();
        await using var lease = new Win32ReadingSelectionLease(focus, keyboard, evidence, 10, 42, 3, null,
            () => 3, () => 10, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lease.Invalidated += () => invalidated.TrySetResult();
        var validation = lease.IsCurrentAsync(CancellationToken.None);
        Assert.True(evidence.Entered.Wait(TimeSpan.FromSeconds(5)));
        lease.Retire();
        var disposal = lease.DisposeAsync().AsTask();
        try
        {
            Assert.False(disposal.IsCompleted);
            evidence.Release.Set();
            Assert.False(await validation.WaitAsync(TimeSpan.FromSeconds(5)));
            await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(evidence.Drained);
        }
        finally { evidence.Release.Set(); }
    }

    [Fact]
    public async Task FocusEpochAndForegroundChangesPermanentlyRetireLease()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var focus = new UiaFocusTracker(NullLogger<UiaFocusTracker>.Instance);
        var evidence = new FakeEvidence { Block = false };
        long epoch = 3; nint foreground = 10;
        await using var lease = new Win32ReadingSelectionLease(focus, new FakeKeyboard(), evidence, 10, 42, 3, null,
            () => epoch, () => foreground, _ => 42);
        Assert.True(lease.Capture(CancellationToken.None));
        foreground = 20;
        Assert.False(await lease.IsCurrentAsync(CancellationToken.None));
        foreground = 10; epoch = 3;
        Assert.False(await lease.IsCurrentAsync(CancellationToken.None));
    }

    private sealed class FakeKeyboard : IKeyboardHook
    {
        public event EventHandler<KeyEventArgs>? KeyEvent;
        public void Emit(int key, bool down, bool suppressed = false)
        {
            var args = new KeyEventArgs(new KeyboardEvent { Kind = KeyKind.Shortcut, Character = '\0', VirtualKey = key, IsKeyDown = down }) { Suppress = suppressed };
            KeyEvent?.Invoke(this, args);
        }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeEvidence : IReadingSelectionEvidence
    {
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Release = new();
        public bool Block = true, Drained, BlockContext, NoContext = true;
        public readonly ManualResetEventSlim ContextEntered = new();
        public readonly ManualResetEventSlim ContextRelease = new();
        public ReadingAcquisitionWindow? ReadReadingContext(UiaTextEditEvidence.ReadingLease lease, Func<bool> allowed)
        { ContextEntered.Set(); if (BlockContext) ContextRelease.Wait(); return !NoContext && allowed() ? new("The selected word.", 4, 8, ReadingBoundary.Unknown, ReadingBoundary.Unknown, ReadingContextMethod.Characters) : null; }
        public int RetireCalls;
        public System.Drawing.Point? Pointer;
        public bool GuardCaptured;
        public UiaTextEditEvidence.ReadingLease? Last;
        public UiaTextEditEvidence.ReadingLease? CaptureReading(nint host, int pid, Func<bool> allowed, Action invalidated, bool ownedTest = false) =>
            allowed() ? Last = new(invalidated) { Text = "selected", FieldId = "owned-fake", Ready = true, ProcessId = pid, Host = host } : null;
        public UiaTextEditEvidence.ReadingLease? CapturePointer(nint host, int pid, System.Drawing.Point point, Func<bool> allowed, Action invalidated)
        { Pointer = point; return CaptureReading(host, pid, allowed, invalidated); }
        public UiaTextEditEvidence.ReadingLease? CaptureScreenGuard(nint host, int pid, Func<bool> allowed, Action invalidated)
        {
            GuardCaptured = true; var lease = CaptureReading(host, pid, allowed, invalidated);
            if (lease is not null) { lease.FieldWindow = 20; lease.FieldLeft = 0; lease.FieldTop = 0; lease.FieldRight = 100; lease.FieldBottom = 100; }
            return lease;
        }
        public bool IsReadingCurrent(UiaTextEditEvidence.ReadingLease lease, Func<bool> allowed)
        {
            Entered.Set(); if (Block) Release.Wait(); return allowed();
        }
        public void RetireReading(UiaTextEditEvidence.ReadingLease lease) { Interlocked.Increment(ref RetireCalls); Interlocked.Exchange(ref lease.Active, 0); }
        public Task DrainRetainedAsync() { Drained = true; return Task.CompletedTask; }
    }
}
