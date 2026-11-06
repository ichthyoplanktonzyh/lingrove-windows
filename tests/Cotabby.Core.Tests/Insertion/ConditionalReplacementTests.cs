using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;

namespace Cotabby.Core.Tests.Insertion;

public sealed class ConditionalReplacementTests
{
    [Fact]
    public async Task RetiredWhileWaitingForSharedGateNeverReadsOrDispatches()
    {
        var gate = new InsertionTransactionGate(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task holder = gate.RunAsync(async _ => { entered.SetResult(); await release.Task; return true; }, CancellationToken.None);
        await entered.Task; var target = InsertionTestFields.Create("hello", 5); var backend = new Backend(target);
        var inserter = new GuardedTextInserter(backend, gate); int live = 1;
        var edit = inserter.ReplaceWithPermitAsync(target, 5, "hi", () => Volatile.Read(ref live) == 1, CancellationToken.None);
        Interlocked.Exchange(ref live, 0); release.SetResult(); await holder;
        Assert.False(await edit); Assert.Equal(0, backend.Reads); Assert.Equal(0, backend.Dispatches);
    }

    [Fact]
    public async Task RevokedAtNativeBoundaryCannotDispatch()
    {
        var target = InsertionTestFields.Create("hello", 5); int live = 1;
        var backend = new Backend(target) { BeforeDispatch = () => Interlocked.Exchange(ref live, 0) };
        var edit = new GuardedTextInserter(backend);
        Assert.False(await edit.ReplaceWithPermitAsync(target, 5, "hi", () => Volatile.Read(ref live) == 1, CancellationToken.None));
        Assert.Equal(1, backend.Reads); Assert.Equal(0, backend.Dispatches);
    }
    private sealed class Backend(FocusedField field) : ITextInsertionBackend, IConditionalTextInsertionBackend
    {
        public int Reads, Dispatches; public Action? BeforeDispatch;
        public FocusedField? ReadFocusedField() => throw new InvalidOperationException();
        public FocusedField? ReadFocusedField(Func<bool> permit) { if (!permit()) return null; Reads++; return field; }
        public bool CanType(string text) => true;
        public bool SendUnicodeEdit(FocusedField target, int count, string text) => throw new InvalidOperationException();
        public bool SendUnicodeEdit(FocusedField target, int count, string text, Func<bool> permit)
        { BeforeDispatch?.Invoke(); if (!permit()) return false; Dispatches++; return true; }
        public bool SendPasteEdit(FocusedField target, int count, IClipboardPasteLease lease) => throw new InvalidOperationException();
        public bool SendPasteEdit(FocusedField target, int count, IClipboardPasteLease lease, Func<bool> permit) => throw new InvalidOperationException();
        public Task<IClipboardPasteLease?> PreparePasteAsync(string text, CancellationToken ct) => throw new InvalidOperationException();
        public Task WaitForPasteAsync() => Task.CompletedTask;
    }
}
