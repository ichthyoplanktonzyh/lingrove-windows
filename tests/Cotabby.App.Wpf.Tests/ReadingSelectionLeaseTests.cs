using System.Runtime.Versioning;
using Cotabby.Core.Input;
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
        public event EventHandler<KeyEventArgs>? KeyEvent { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeEvidence : IReadingSelectionEvidence
    {
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Release = new();
        public bool Block = true, Drained;
        public int RetireCalls;
        public UiaTextEditEvidence.ReadingLease? CaptureReading(nint host, int pid, Func<bool> allowed, Action invalidated, bool ownedTest = false) =>
            allowed() ? new(invalidated) { Text = "selected", FieldId = "owned-fake", Ready = true, ProcessId = pid, Host = host } : null;
        public bool IsReadingCurrent(UiaTextEditEvidence.ReadingLease lease, Func<bool> allowed)
        {
            Entered.Set(); if (Block) Release.Wait(); return allowed();
        }
        public void RetireReading(UiaTextEditEvidence.ReadingLease lease) { Interlocked.Increment(ref RetireCalls); Interlocked.Exchange(ref lease.Active, 0); }
        public Task DrainRetainedAsync() { Drained = true; return Task.CompletedTask; }
    }
}
