using Cotabby.Core.Context;

namespace Cotabby.Core.Tests.Context;

public sealed class ClipboardWriteAccountingTests
{
    [Fact]
    public void OwnStagingAndRestoreDeltasDoNotLookLikeUserCopies()
    {
        var accounting = new ClipboardWriteAccounting();

        accounting.BeginOwnWrite(sequenceBeforeWrite: 40);
        accounting.EndOwnWrite(sequenceAfterWrite: 42, containsOwnText: true);
        Assert.Equal(40u, accounting.GetVisibleSequence(42));
        Assert.True(accounting.IsOwnText(42));

        accounting.BeginOwnWrite(sequenceBeforeWrite: 42);
        accounting.EndOwnWrite(sequenceAfterWrite: 45, containsOwnText: false);
        Assert.Equal(40u, accounting.GetVisibleSequence(45));
        Assert.False(accounting.IsOwnText(45));
    }

    [Fact]
    public void UserCopyDuringLeaseBecomesVisibleImmediately()
    {
        var accounting = new ClipboardWriteAccounting();

        accounting.BeginOwnWrite(sequenceBeforeWrite: 90);
        accounting.EndOwnWrite(sequenceAfterWrite: 92, containsOwnText: true);

        // The lease still exists, but the sequence changed after a user copy. Its change remains
        // visible, and the copy is not hidden as though it were the app's staged suggestion.
        Assert.Equal(91u, accounting.GetVisibleSequence(93));
        Assert.False(accounting.IsOwnText(93));
    }

    [Fact]
    public void FailedRestoreHidesStagedTextOnlyUntilTheNextExternalChange()
    {
        var accounting = new ClipboardWriteAccounting();

        accounting.BeginOwnWrite(sequenceBeforeWrite: 10);
        accounting.EndOwnWrite(sequenceAfterWrite: 12, containsOwnText: true);

        Assert.Equal(10u, accounting.GetVisibleSequence(12));
        Assert.True(accounting.IsOwnText(12));

        Assert.Equal(11u, accounting.GetVisibleSequence(13));
        Assert.False(accounting.IsOwnText(13));
    }

    [Fact]
    public void SequenceAccountingWrapsLikeTheNativeUnsignedCounter()
    {
        var accounting = new ClipboardWriteAccounting();

        accounting.BeginOwnWrite(sequenceBeforeWrite: uint.MaxValue - 1);
        accounting.EndOwnWrite(sequenceAfterWrite: 1, containsOwnText: true);

        Assert.Equal(uint.MaxValue - 1, accounting.GetVisibleSequence(1));
        Assert.True(accounting.IsOwnText(1));
    }

    [Fact]
    public void OverlappingOwnWritesAreRejected()
    {
        var accounting = new ClipboardWriteAccounting();
        accounting.BeginOwnWrite(1);

        Assert.Throws<InvalidOperationException>(() => accounting.BeginOwnWrite(1));
    }
}
