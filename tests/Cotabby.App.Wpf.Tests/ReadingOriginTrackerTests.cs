using Cotabby.Win32.Reading;
using System.Runtime.Versioning;

namespace Cotabby.App.Wpf.Tests;

[SupportedOSPlatform("windows8.1")]
public sealed class ReadingOriginTrackerTests
{
    [Fact]
    public async Task DelayedProcessNameCannotPublishAfterForegroundEpochChanges()
    {
        long epoch = 1; nint foreground = (nint)42;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var tracker = new Win32ReadingOriginTracker(() => Interlocked.Read(ref epoch), () => foreground,
            hwnd => hwnd == (nint)42 ? 10 : 20, pid =>
            {
                if (pid == 10) { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); return "old"; }
                return "readonly-host";
            });
        try
        {
            tracker.FocusTransition((nint)42); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            foreground = (nint)43; Interlocked.Increment(ref epoch); tracker.FocusTransition((nint)43);
            for (int i = 0; i < 100 && tracker.Current is null; i++) await Task.Delay(10);
            Assert.Equal("readonly-host", tracker.Current?.ProcessName);
            release.Set(); await Task.Delay(30);
            Assert.Equal(2, tracker.Current?.FocusEpoch); Assert.Equal("readonly-host", tracker.Current?.ProcessName);
            foreground = (nint)44; Assert.Null(tracker.Current);
        }
        finally { release.Set(); }
    }
}
