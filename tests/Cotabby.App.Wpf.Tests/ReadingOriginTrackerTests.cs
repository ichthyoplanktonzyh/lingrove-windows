using Cotabby.Win32.Reading;
using System.Runtime.Versioning;

namespace Cotabby.App.Wpf.Tests;

[SupportedOSPlatform("windows8.1")]
public sealed class ReadingOriginTrackerTests
{
    [Fact]
    public async Task ShutdownJoinsStartedResolverAndDiscardsPending()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); int reads = 0;
        var tracker = new Win32ReadingOriginTracker(() => 1, () => (nint)42, _ => 10, _ =>
        { Interlocked.Increment(ref reads); entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); return "host"; });
        try
        {
            tracker.FocusTransition((nint)42); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            tracker.FocusTransition((nint)42); var shutdown = tracker.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted); release.Set(); await shutdown;
            Assert.Equal(1, reads); Assert.Null(tracker.Current);
        }
        finally { release.Set(); await tracker.DisposeAsync(); }
    }
    [Fact]
    public async Task DelayedProcessNameCannotPublishAfterForegroundEpochChanges()
    {
        long epoch = 1; nint foreground = (nint)42;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolved = new System.Collections.Concurrent.ConcurrentQueue<int>();
        using var release = new ManualResetEventSlim();
        await using var tracker = new Win32ReadingOriginTracker(() => Interlocked.Read(ref epoch), () => foreground,
            hwnd => hwnd == (nint)42 ? 10 : 20, pid =>
            {
                resolved.Enqueue(pid);
                if (pid == 10) { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); return "old"; }
                return "readonly-host";
            });
        try
        {
            tracker.FocusTransition((nint)42); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 100; i++) { foreground = (nint)43; Interlocked.Increment(ref epoch); tracker.FocusTransition((nint)43); }
            Assert.Null(tracker.Current); Assert.Single(resolved);
            release.Set();
            for (int i = 0; i < 100 && tracker.Current is null; i++) await Task.Delay(10);
            Assert.Equal("readonly-host", tracker.Current?.ProcessName);
            Assert.Equal(101, tracker.Current?.FocusEpoch); Assert.Equal(new[] { 10, 20 }, resolved);
            foreground = (nint)44; Assert.Null(tracker.Current);
        }
        finally { release.Set(); }
    }
}
