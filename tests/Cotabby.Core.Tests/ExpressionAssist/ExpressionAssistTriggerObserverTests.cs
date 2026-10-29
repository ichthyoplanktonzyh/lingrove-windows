using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class ExpressionAssistTriggerObserverTests
{
    private static FocusedField Field(string text) => new()
    {
        ProcessId = 1, ProcessName = "editor", FieldId = "field", FocusChangeSequence = 1,
        Text = text, CaretOffset = text.Length, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty,
        IsSingleLine = true, IsSecure = false, HasReliableSelection = true,
        Composition = InputCompositionState.Clear, CompositionSequence = 1,
    };

    [Fact]
    public void TriggerRequiresRecentMutationAndCommittedMatchingField()
    {
        var observer = new ExpressionAssistTriggerObserver(); var now = TimeSpan.FromSeconds(1);
        Assert.Null(observer.Observe(Field("word??"), 0, now));
        var arm = observer.Mutation(Field("word?"), "?", "??", now);
        Assert.True(arm.Probe);
        Assert.Null(observer.Observe(Field("word??") with { Composition = InputCompositionState.Composing }, arm.Epoch, now));
        Assert.Null(observer.Observe(Field("word??") with { FocusChangeSequence = 2 }, arm.Epoch, now));
        Assert.Equal("??", observer.Observe(Field("word??"), arm.Epoch, now + TimeSpan.FromMilliseconds(35)));
        Assert.Null(observer.Observe(Field("word??"), arm.Epoch, now + TimeSpan.FromMilliseconds(36)));
    }

    [Fact]
    public void MutationEpochAndDeadlineRejectStaleProbeButPublisherCanMatchOrdinaryMutation()
    {
        var observer = new ExpressionAssistTriggerObserver(); var now = TimeSpan.FromSeconds(1);
        var old = observer.Mutation(Field(""), "?", "??", now);
        var current = observer.Mutation(Field("x"), "x", "??", now);
        Assert.False(current.Probe);
        Assert.Null(observer.Observe(Field("??"), old.Epoch, now));
        Assert.Equal("？？", observer.Observe(Field("？？"), current.Epoch, now));
        current = observer.Mutation(Field(""), "?", "??", now);
        Assert.Null(observer.Observe(Field("??"), current.Epoch, now + TimeSpan.FromMilliseconds(500)));
        observer.Cancel(); Assert.Null(observer.Observe(Field("??"), current.Epoch, now));
    }
}
