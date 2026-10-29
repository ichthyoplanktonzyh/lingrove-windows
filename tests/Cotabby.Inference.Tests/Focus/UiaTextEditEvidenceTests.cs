using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cotabby.Win32.Focus;

namespace Cotabby.Inference.Tests.Focus;

[SupportedOSPlatform("windows8.1")]
public sealed class UiaTextEditEvidenceTests
{
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
