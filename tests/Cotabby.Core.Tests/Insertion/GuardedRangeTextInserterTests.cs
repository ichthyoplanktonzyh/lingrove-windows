using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.Insertion;

public sealed class GuardedRangeTextInserterTests
{
    private static FocusedField Field() => InsertionTestFields.Create("前。你好😀。后。", 3) with { SupportsRangeReplacement = true };
    private static Task<RangeEditDispatch> Run(Backend backend, Selection selections, InsertionTransactionGate? gate = null, Func<bool>? permit = null) =>
        new GuardedRangeTextInserter(backend, selections, gate ?? new()).ReplaceRangeAsync(Field(), new(2, 5), "你好😀。", "a translated sentence",
            permit ?? (() => true), CancellationToken.None);

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RangeEditPreparesBeforeSelectionAndDispatchesOnceWithNoBackspaces(bool type)
    {
        var backend = new Backend { Type = type }; var selection = new Selection(backend);
        Assert.Equal(RangeEditDispatch.Dispatched, await Run(backend, selection));
        Assert.Equal(type ? new[] { "select", "type" } : new[] { "prepare", "select", "paste", "wait", "dispose" }, backend.Events);
        Assert.Equal(1, backend.Dispatches);
    }

    [Fact]
    public async Task UserCopyOrRetiredPermitDuringPreparationPreventsEvenSelection()
    {
        var backend = new Backend { Type = false }; var selection = new Selection(backend);
        backend.Prepared = () => backend.Lease.IsCurrent = false;
        Assert.Equal(RangeEditDispatch.Rejected, await Run(backend, selection));
        Assert.Equal(new[] { "prepare", "dispose" }, backend.Events);
        Assert.Equal(0, selection.Calls); Assert.Equal(0, backend.Dispatches);
    }

    [Fact]
    public async Task StaleSnapshotAfterWaitingForTheSharedGatePreventsSelection()
    {
        var gate = new InsertionTransactionGate(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = gate.RunAsync(async _ => { entered.SetResult(); await release.Task; return true; }, CancellationToken.None);
        await entered.Task;
        var backend = new Backend(); var selection = new Selection(backend); var edit = Run(backend, selection, gate);
        backend.Current = Field() with { FocusChangeSequence = 7 }; release.SetResult(); await blocker;
        Assert.Equal(RangeEditDispatch.Rejected, await edit); Assert.Equal(0, selection.Calls); Assert.Equal(0, backend.Dispatches);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task FinalSelectionOrClipboardRevocationPreventsContentDispatch(bool revokeSelection)
    {
        var backend = new Backend { Type = false }; var selection = new Selection(backend);
        backend.BeforeSend = () => { if (revokeSelection) selection.Valid = false; else backend.Lease.IsCurrent = false; };
        Assert.Equal(RangeEditDispatch.Uncertain, await Run(backend, selection));
        Assert.Equal(0, backend.Dispatches); Assert.Contains("dispose", backend.Events);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PartialDispatchAndCleanupFailureAreUncertainAndNeverRetried(bool cleanupFailure)
    {
        var backend = new Backend { Type = false, SendResult = cleanupFailure }; var selection = new Selection(backend);
        if (cleanupFailure) backend.Lease.DisposeException = new InvalidOperationException("synthetic cleanup failure");
        Assert.Equal(RangeEditDispatch.Uncertain, await Run(backend, selection));
        Assert.Equal(1, backend.Dispatches); Assert.Equal(1, selection.Calls);
        Assert.Equal(new[] { "prepare", "select", "paste", "wait", "dispose" }, backend.Events);
    }

    [Fact]
    public async Task AcceptedNativeWorkKeepsGateAndClipboardUntilItFinishes()
    {
        var backend = new Backend { Type = false }; var selection = new Selection(backend) { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var gate = new InsertionTransactionGate(); var edit = Run(backend, selection, gate); await selection.Entered.Task;
        bool otherEntered = false;
        var other = gate.RunAsync(_ => { otherEntered = true; return Task.FromResult(true); }, CancellationToken.None);
        Assert.False(otherEntered); Assert.DoesNotContain("dispose", backend.Events);
        selection.Held.SetResult(); Assert.Equal(RangeEditDispatch.Dispatched, await edit); await other;
        Assert.True(otherEntered); Assert.Equal("dispose", backend.Events[^1]);
    }

    private sealed class Selection(Backend backend) : IRangeSelectionDispatcher
    {
        internal bool Valid = true; internal int Calls;
        internal TaskCompletionSource? Held;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<RangeEditDispatch> SelectAndDispatchAsync(FocusedField target, LearningTextRange range, string source,
            Func<bool> permit, Func<Func<bool>, bool> dispatch)
        {
            Calls++; backend.Events.Add("select"); Entered.SetResult(); if (Held is not null) await Held.Task;
            Assert.Equal(new(2, 5), range); Assert.Equal("你好😀。", source);
            return dispatch(() => Valid && permit()) ? RangeEditDispatch.Dispatched : RangeEditDispatch.Uncertain;
        }
    }
    private sealed class Backend : ITextInsertionBackend, IConditionalTextInsertionBackend
    {
        internal FocusedField Current = Field(); internal bool Type = true, SendResult = true; internal int Dispatches;
        internal List<string> Events = []; internal Action? Prepared, BeforeSend;
        internal FakeClipboardPasteLease Lease;
        internal Backend() => Lease = new(Events);
        public FocusedField? ReadFocusedField() => Current;
        public FocusedField? ReadFocusedField(Func<bool> permit) => permit() ? Current : null;
        public bool CanType(string text) => Type;
        public bool SendUnicodeEdit(FocusedField target, int deleteUtf16Count, string text) => throw new InvalidOperationException();
        public bool SendPasteEdit(FocusedField target, int deleteUtf16Count, IClipboardPasteLease lease) => throw new InvalidOperationException();
        public bool SendUnicodeEdit(FocusedField target, int count, string text, Func<bool> permit) => Send("type", count, permit);
        public bool SendPasteEdit(FocusedField target, int count, IClipboardPasteLease lease, Func<bool> permit) => Send("paste", count, () => lease.IsCurrent && permit());
        private bool Send(string action, int count, Func<bool> permit)
        { Assert.Equal(0, count); BeforeSend?.Invoke(); if (!permit()) return false; Dispatches++; Events.Add(action); return SendResult; }
        public Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct)
        { Events.Add("prepare"); Prepared?.Invoke(); return Task.FromResult<IClipboardPasteLease?>(Lease); }
        public Task WaitForPasteAsync() { Events.Add("wait"); return Task.CompletedTask; }
    }
}
