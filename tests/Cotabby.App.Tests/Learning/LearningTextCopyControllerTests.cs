using Cotabby.App.Learning;
using Cotabby.Core.Context;

namespace Cotabby.App.Tests.Learning;

/// <summary>Controls the native admission gap without a WPF thread or a real clipboard.</summary>
public sealed class LearningTextCopyControllerTests
{
    [Fact]
    public async Task PendingNativeWorkLeavesCallerFreeAndRejectsAdditionalWorkWithoutQueueing()
    {
        using var native = new BlockingWriter();
        using var controller = new LearningTextCopyController(native);
        var pending = controller.CopyAsync("first", () => true);
        await native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        Assert.False(await controller.CopyAsync("second", () => true));
        native.Release.Set();
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, native.Calls);
        Assert.Equal("first", native.Copied);
    }

    [Fact]
    public async Task RetirementWhileTheWorkerPreparesRevokesTheFinalGrant()
    {
        using var native = new BlockingWriter();
        using var controller = new LearningTextCopyController(native);
        int current = 1;
        var pending = controller.CopyAsync("old document", () => Volatile.Read(ref current) == 1);
        await native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Interlocked.Exchange(ref current, 0);
        native.Release.Set();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(native.Copied);
    }

    [Fact]
    public async Task DisposalRevokesPendingAdmissionAndDoesNotJoinTheNativeCall()
    {
        using var native = new BlockingWriter();
        var controller = new LearningTextCopyController(native);
        int grants = 0;
        var pending = controller.CopyAsync("old document", () => { Interlocked.Increment(ref grants); return true; });
        await native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Dispose();
        Assert.False(pending.IsCompleted);
        Assert.False(await controller.CopyAsync("new document", () => true));
        native.Release.Set();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, grants); Assert.Null(native.Copied);
    }

    [Fact]
    public async Task AlreadyAdmittedTransactionCompletesAfterDisposalWithoutLeavingAHalfWrite()
    {
        using var native = new BlockingWriter { WaitAfterGrant = true };
        var controller = new LearningTextCopyController(native);
        var pending = controller.CopyAsync("committed document", () => true);
        await native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Dispose();
        Assert.False(pending.IsCompleted);
        native.Release.Set();
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("committed document", native.Copied);
    }

    [Fact]
    public async Task FailedBoundaryClearsSingleWorkerStateAndDoesNotExposeItsException()
    {
        int calls = 0;
        var native = new DelegateWriter((_, grant) => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("private native diagnostic") : grant());
        using var controller = new LearningTextCopyController(native);
        Assert.False(await controller.CopyAsync("first", () => true));
        Assert.True(await controller.CopyAsync("second", () => true));
    }

    [Fact]
    public async Task InvalidPayloadDoesNotStartNativeWork()
    {
        int calls = 0;
        using var controller = new LearningTextCopyController(new DelegateWriter((_, _) => { calls++; return true; }));
        Assert.False(await controller.CopyAsync("before\0after", () => true));
        Assert.Equal(0, calls);
    }

    private sealed class DelegateWriter(Func<string, Func<bool>, bool> write) : IClipboardTextWriter
    { public bool TryWrite(string text, Func<bool> tryCommit) => write(text, tryCommit); }

    private sealed class BlockingWriter : IClipboardTextWriter, IDisposable
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public bool WaitAfterGrant;
        public int Calls;
        public string? Copied;
        public bool TryWrite(string text, Func<bool> tryCommit)
        {
            Interlocked.Increment(ref Calls);
            bool granted = false;
            if (WaitAfterGrant) granted = tryCommit();
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test did not release fake clipboard writer.");
            if (!WaitAfterGrant) granted = tryCommit();
            if (granted) Copied = text;
            return granted;
        }
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }
}
