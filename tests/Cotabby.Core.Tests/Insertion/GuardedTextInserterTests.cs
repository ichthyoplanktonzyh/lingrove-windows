using Cotabby.Core.Insertion;

namespace Cotabby.Core.Tests.Insertion;

public class GuardedTextInserterTests
{
    [Fact]
    public async Task InsertDispatchesUnicodeAsOneEditAndSupportsNonBmpText()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend { Current = target };
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " 🎉 café", CancellationToken.None);

        Assert.True(inserted);
        var edit = Assert.Single(backend.UnicodeEdits);
        Assert.Equal(0, edit.DeleteUtf16Count);
        Assert.Equal(" 🎉 café", edit.Text);
        Assert.Empty(backend.PasteEdits);
        Assert.Equal(1, backend.ReadCount);
    }

    [Fact]
    public async Task EmptyInsertIsSuccessfulNoOpAfterFreshTargetCheck()
    {
        var target = InsertionTestFields.Create(string.Empty, caretOffset: 0);
        var backend = new FakeInsertionBackend { Current = target };
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, string.Empty, CancellationToken.None);

        Assert.True(inserted);
        Assert.Equal(1, backend.ReadCount);
        Assert.Equal(0, backend.CanTypeCount);
        Assert.Empty(backend.UnicodeEdits);
        Assert.Empty(backend.PasteEdits);
    }

    [Fact]
    public async Task ReplacementDispatchesDeletionAndInsertionInOneBatch()
    {
        var target = InsertionTestFields.Create("teh", caretOffset: 3);
        var backend = new FakeInsertionBackend { Current = target };
        var inserter = new GuardedTextInserter(backend);

        bool replaced = await inserter.ReplaceAsync(target, deleteCount: 3, "the", CancellationToken.None);

        Assert.True(replaced);
        var edit = Assert.Single(backend.UnicodeEdits);
        Assert.Equal(3, edit.DeleteUtf16Count);
        Assert.Equal("the", edit.Text);
        Assert.Empty(backend.PasteEdits);
    }

    [Fact]
    public async Task PastePreparesBeforeEditAndWaitsBeforeRestoringClipboardLease()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.DefaultLease = lease;
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.True(inserted);
        Assert.Equal(["prepare", "paste", "wait", "dispose"], backend.Events);
        Assert.Equal([" world"], backend.PreparedTexts);
        Assert.True(lease.WasDisposed);
        Assert.Equal(1, backend.PasteWaitCount);
    }

    [Fact]
    public async Task PasteReplacementCarriesDeletionAndTextInOneNativeBatch()
    {
        var target = InsertionTestFields.Create("teh", caretOffset: 3);
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.DefaultLease = lease;
        var inserter = new GuardedTextInserter(backend);

        bool replaced = await inserter.ReplaceAsync(target, deleteCount: 3, "the", CancellationToken.None);

        Assert.True(replaced);
        var edit = Assert.Single(backend.PasteEdits);
        Assert.Equal(3, edit.DeleteUtf16Count);
        Assert.Equal("<clipboard>", edit.Text);
        Assert.Equal(["prepare", "paste", "wait", "dispose"], backend.Events);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("caret")]
    [InlineData("selection")]
    [InlineData("secure")]
    [InlineData("sequence")]
    [InlineData("field")]
    [InlineData("process")]
    [InlineData("unreliable")]
    public async Task PasteRechecksFreshSnapshotAfterPreparation(string change)
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.DefaultLease = lease;
        backend.PrepareOverride = (_, _) =>
        {
            backend.Current = change switch
            {
                "text" => target with { Text = "hello!" },
                "caret" => target with { CaretOffset = 4 },
                "selection" => target with { HasSelection = true },
                "secure" => target with { IsSecure = true },
                "sequence" => target with { FocusChangeSequence = target.FocusChangeSequence + 1 },
                "field" => target with { FieldId = "uia:other" },
                "process" => target with { ProcessId = target.ProcessId + 1 },
                "unreliable" => target with { HasReliableSelection = false },
                _ => throw new ArgumentOutOfRangeException(nameof(change)),
            };
            return Task.FromResult<IClipboardPasteLease?>(lease);
        };
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.False(inserted);
        Assert.Empty(backend.PasteEdits);
        Assert.Equal(0, backend.PasteWaitCount);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task InitiallyUnreliableSelectionEvidenceIsRejectedBeforeBackendWork()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5) with { HasReliableSelection = false };
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.False(inserted);
        Assert.Equal(0, backend.ReadCount);
        Assert.Equal(0, backend.PrepareCount);
    }

    [Fact]
    public async Task QueuedOperationRechecksAndRejectsTargetMadeStaleByEarlierTransaction()
    {
        var target = InsertionTestFields.Create("a", caretOffset: 1);
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var prepareStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrepare = new TaskCompletionSource<IClipboardPasteLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.PrepareOverride = async (_, ct) =>
        {
            prepareStarted.TrySetResult(true);
            return await releasePrepare.Task.WaitAsync(ct).ConfigureAwait(false);
        };
        var inserter = new GuardedTextInserter(backend);

        Task<bool> first = inserter.InsertAsync(target, "b", CancellationToken.None);
        Task<bool>? queued = null;
        try
        {
            await prepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queued = inserter.InsertAsync(target, "c", CancellationToken.None);
            Assert.False(queued.IsCompleted);

            releasePrepare.TrySetResult(lease);
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(await queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Single(backend.PasteEdits);
            Assert.Equal(1, backend.PrepareCount);
        }
        finally
        {
            releasePrepare.TrySetResult(lease);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (queued is not null) await queued.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CancellationWhileQueuedPreventsAnyBackendDispatch()
    {
        var target = InsertionTestFields.Create("a", caretOffset: 1);
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var prepareStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrepare = new TaskCompletionSource<IClipboardPasteLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.PrepareOverride = async (_, ct) =>
        {
            prepareStarted.TrySetResult(true);
            return await releasePrepare.Task.WaitAsync(ct).ConfigureAwait(false);
        };
        var inserter = new GuardedTextInserter(backend);

        Task<bool> first = inserter.InsertAsync(target, "b", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task<bool>? queued = null;
        try
        {
            await prepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queued = inserter.InsertAsync(target, "c", cancellation.Token);
            cancellation.Cancel();

            Assert.False(await queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, backend.PrepareCount);
            releasePrepare.TrySetResult(lease);
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Single(backend.PasteEdits);
        }
        finally
        {
            cancellation.Cancel();
            releasePrepare.TrySetResult(lease);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (queued is not null) await queued.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CancellationAfterPastePreparationDisposesLeaseWithoutDispatch()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        using var cancellation = new CancellationTokenSource();
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.PrepareOverride = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult<IClipboardPasteLease?>(lease);
        };
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", cancellation.Token);

        Assert.False(inserted);
        Assert.True(lease.WasDisposed);
        Assert.Empty(backend.PasteEdits);
        Assert.Equal(0, backend.PasteWaitCount);
    }

    [Fact]
    public async Task ClipboardPreparationFailureNeverDeletesReplacementText()
    {
        var target = InsertionTestFields.Create("teh", caretOffset: 3);
        var backend = new FakeInsertionBackend
        {
            Current = target,
            CanTypeResult = false,
            PrepareOverride = (_, _) => Task.FromResult<IClipboardPasteLease?>(null),
        };
        var inserter = new GuardedTextInserter(backend);

        bool replaced = await inserter.ReplaceAsync(target, deleteCount: 3, "the", CancellationToken.None);

        Assert.False(replaced);
        Assert.Empty(backend.UnicodeEdits);
        Assert.Empty(backend.PasteEdits);
    }

    [Fact]
    public async Task LostClipboardOwnershipRejectsPasteAndStillDisposesLease()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var lease = new FakeClipboardPasteLease(backend.Events, isCurrent: false);
        backend.DefaultLease = lease;
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.False(inserted);
        Assert.Empty(backend.PasteEdits);
        Assert.Equal(0, backend.PasteWaitCount);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task UncertainPasteFailureWaitsAndCleansUpWithoutReportingSuccess()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend
        {
            Current = target,
            CanTypeResult = false,
            PasteEditResult = false,
        };
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.DefaultLease = lease;
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.False(inserted);
        Assert.Single(backend.PasteEdits);
        Assert.Equal(["prepare", "paste", "wait", "dispose"], backend.Events);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task PasteDispatchExceptionStillWaitsAndDisposesLease()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend
        {
            Current = target,
            CanTypeResult = false,
            PasteEditException = new InvalidOperationException("native dispatch failed after partial input"),
        };
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.DefaultLease = lease;
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.False(inserted);
        Assert.Equal(["prepare", "paste", "wait", "dispose"], backend.Events);
        Assert.True(lease.WasDisposed);
    }

    [Fact]
    public async Task PasteWaitOrLeaseCleanupFailureCannotReportSuccess()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend
        {
            Current = target,
            CanTypeResult = false,
            WaitForPasteException = new InvalidOperationException("host did not settle"),
        };
        var lease = new FakeClipboardPasteLease(backend.Events);
        backend.DefaultLease = lease;
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.False(inserted);
        Assert.True(lease.WasDisposed);
        Assert.Contains("dispose", backend.Events);

        var cleanupBackend = new FakeInsertionBackend { Current = target, CanTypeResult = false };
        var failingLease = new FakeClipboardPasteLease(cleanupBackend.Events)
        {
            DisposeException = new InvalidOperationException("clipboard restore failed"),
        };
        cleanupBackend.DefaultLease = failingLease;
        var cleanupInserter = new GuardedTextInserter(cleanupBackend);

        Assert.False(await cleanupInserter.InsertAsync(target, " world", CancellationToken.None));
        Assert.True(failingLease.WasDisposed);
    }

    [Fact]
    public async Task FailedUnicodeDispatchIsNotAutomaticallyRetried()
    {
        var target = InsertionTestFields.Create("hello", caretOffset: 5);
        var backend = new FakeInsertionBackend
        {
            Current = target,
            UnicodeEditResult = false,
        };
        var inserter = new GuardedTextInserter(backend);

        bool inserted = await inserter.InsertAsync(target, " world", CancellationToken.None);

        Assert.False(inserted);
        Assert.Single(backend.UnicodeEdits);
        Assert.Empty(backend.PasteEdits);
    }
}
