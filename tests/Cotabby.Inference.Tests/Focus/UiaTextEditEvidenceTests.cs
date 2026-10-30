using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cotabby.Win32.Focus;
using Xunit.Abstractions;

namespace Cotabby.Inference.Tests.Focus;

[SupportedOSPlatform("windows8.1")]
public sealed class UiaTextEditEvidenceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RevokedRequestCannotContinueQueuedRetainedRegistration()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var owner = new UiaTextEditEvidence(() => { });
        using var release = new ManualResetEventSlim();
        using var callerRelease = new ManualResetEventSlim();
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.RetainedRegistrationQueuedForTest = () =>
        {
            queued.TrySetResult();
            Assert.True(callerRelease.Wait(TimeSpan.FromSeconds(10)));
        };
        bool allowed = true;
        int checks = 0;
        Assert.False(owner.Execute(() => release.Wait()));
        try
        {
            var register = Task.Factory.StartNew(() => owner.RegisterRetainedHost(123, "test-only", 1, 2, () => { },
                CancellationToken.None, () => { Interlocked.Increment(ref checks); return Volatile.Read(ref allowed); }),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(owner.RetainedRegistrationPendingForTest);
            Volatile.Write(ref allowed, false);
            callerRelease.Set();
            release.Set();
            Assert.Null(await register.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(Volatile.Read(ref checks) > 0);
        }
        finally { callerRelease.Set(); release.Set(); }
    }

    [Fact]
    public async Task RevokedQueuedCompositionReadDoesNotQueryNativeFocus()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var owner = new UiaTextEditEvidence(() => { });
        using var release = new ManualResetEventSlim();
        bool allowed = true;
        Assert.False(owner.Execute(() => release.Wait()));
        try
        {
            var read = Task.Run(() => owner.Observe(123, "test-only", 1, () => Volatile.Read(ref allowed)));
            Volatile.Write(ref allowed, false);
            release.Set();
            Assert.Equal(default, await read.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task TimedOutRetainedRegistrationIsRetiredBeforeNativeFocusQuery()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var owner = new UiaTextEditEvidence(() => { });
        using var release = new ManualResetEventSlim();
        Assert.False(owner.Execute(() => release.Wait()));
        try
        {
            var result = await Task.Run(() => owner.RegisterRetainedHost(123, "test-only", 1, 2, () => { }, CancellationToken.None));
            Assert.Null(result);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task NativeFactoryCreatesAutomation3OnItsMtaOwner()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var evidence = new UiaTextEditEvidence(() => { });
        var result = await evidence.BootstrapForTest.WaitAsync(TimeSpan.FromSeconds(5));
        output.WriteLine($"CoInitializeEx=0x{result.InitializeHResult:X8}; CoCreateInstance=0x{result.FactoryHResult:X8}; IUIAutomation3={result.HasAutomation}");
        Assert.True(result.InitializeHResult >= 0, $"CoInitializeEx: 0x{result.InitializeHResult:X8}");
        Assert.True(result.FactoryHResult is >= 0, $"CoCreateInstance: 0x{result.FactoryHResult:X8}");
        Assert.True(result.HasAutomation);
        output.WriteLine($"ConnectionTimeout=0x{result.ConnectionTimeoutHResult:X8}; TransactionTimeout=0x{result.TransactionTimeoutHResult:X8}");
        Assert.True(result.ConnectionTimeoutHResult is >= 0);
        Assert.True(result.TransactionTimeoutHResult is >= 0);
    }

    [Fact]
    public async Task OwnerSerializesOperationsAndDoesNotHoldCallerOnTimeout()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var evidence = new UiaTextEditEvidence(() => { });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var order = new System.Collections.Concurrent.ConcurrentQueue<int>();
        Assert.False(evidence.Execute(() => { order.Enqueue(1); entered.Set(); release.Wait(); order.Enqueue(2); }));
        Assert.True(entered.IsSet);
        Task<bool> next = Task.Run(() => evidence.Execute(() => order.Enqueue(3)));
        release.Set();
        Assert.True(await next);
        Assert.Equal(new[] { 1, 2, 3 }, order.ToArray());
    }

    [Fact]
    public async Task TimedOutQueuedReadsAreRetiredBeforeAnyNativeFieldQuery()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        using var evidence = new UiaTextEditEvidence(() => { });
        using var release = new ManualResetEventSlim();
        Assert.False(evidence.Execute(() => release.Wait()));
        try
        {
            var reads = Enumerable.Range(0, 17).Select(_ => Task.Run(() => evidence.Observe(123, "test-only", 1))).ToArray();
            var results = await Task.WhenAll(reads);
            Assert.All(results, result => Assert.Equal((Cotabby.Core.Focus.InputCompositionState.Unknown, 0L), result));
        }
        finally
        {
            // Every read retires its epoch before the owner can leave the blocker.
            release.Set();
        }
    }

    [Fact]
    public void NativeCallbackRetainsContextAndIgnoresRetiredEpoch()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1)) return;
        int changes = 0;
        using var evidence = new UiaTextEditEvidence(() => Interlocked.Increment(ref changes));
        nint old = evidence.CreateCallbackForTest();
        try
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long first = evidence.Version;
            UiaTextEditEvidence.SignalCallbackForTest(old);
            Assert.Equal(first + 1, evidence.Version);
            Assert.Equal(1, changes);

            evidence.Invalidate();
            long retired = evidence.Version;
            UiaTextEditEvidence.SignalCallbackForTest(old);
            Assert.Equal(retired, evidence.Version);
            Assert.Equal(1, changes);

            nint current = evidence.CreateCallbackForTest();
            try
            {
                UiaTextEditEvidence.SignalCallbackForTest(current);
                Assert.Equal(retired + 1, evidence.Version);
                Assert.Equal(2, changes);
            }
            finally { Marshal.Release(current); }
            evidence.Dispose();
            long stopped = evidence.Version;
            UiaTextEditEvidence.SignalCallbackForTest(old);
            Assert.Equal(stopped, evidence.Version);
            Assert.Equal(2, changes);
        }
        finally { Marshal.Release(old); }
    }
}
