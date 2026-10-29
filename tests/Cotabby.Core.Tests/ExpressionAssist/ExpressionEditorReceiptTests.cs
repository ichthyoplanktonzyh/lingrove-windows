using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class ExpressionEditorReceiptTests
{
    private static ExpressionEditorObservation Field(string value = "hello??", int start = 7, int length = 0) =>
        new(42, "field", 3, 2, value, new(start, length), false, true, InputCompositionState.Clear, 1);

    [Fact]
    public void ExactTriggerAndSelectionRemainDistinct()
    {
        var trigger = ExpressionEditorReceiptPolicy.Capture(Field(), "??");
        Assert.NotNull(trigger);
        Assert.Equal(new ExpressionEditorRange(5, 2), trigger.ReplacementRange);
        Assert.Equal(new ExpressionEditorRange(7, 0), trigger.Original.Selection);
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(Field(), "?!"));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(Field(start: 5, length: 2), "??"));
        var selection = ExpressionEditorReceiptPolicy.Capture(Field(start: 0, length: 5), null);
        Assert.Equal("hello", selection!.Source);
        Assert.Equal(selection.Original.Selection, selection.ReplacementRange);
    }

    [Fact]
    public void MissingCompositionTruncationSecureAndMalformedRangesFailClosed()
    {
        var field = Field();
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(field with { IsSecure = true }, null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(field with { IsComplete = false }, null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(field with { Composition = InputCompositionState.Unknown }, null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(field with { Composition = InputCompositionState.Composing }, null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(field with { CompositionSequence = 0 }, null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(field with { ObservationVersion = 0 }, null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(Field(new string('x', 65537), 0), null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(Field("a😀b", 2), null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(Field("e\u0301", 1), null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(Field("e\u0301", 0, 1), null));
        Assert.Null(ExpressionEditorReceiptPolicy.Capture(Field("\ud800", 0), null));
    }

    [Fact]
    public void ReturnReadRequiresExactFieldTextSelectionAndCommittedEvidence()
    {
        var saved = ExpressionEditorReceiptPolicy.Capture(Field(), "??")!;
        var resumed = saved.Original with { FocusEpoch = 4, ObservationVersion = 3, CompositionSequence = 2 };
        Assert.True(ExpressionEditorReceiptPolicy.MatchesOriginal(saved, resumed));
        Assert.False(ExpressionEditorReceiptPolicy.MatchesOriginal(saved, resumed with { FieldId = "other" }));
        Assert.False(ExpressionEditorReceiptPolicy.MatchesOriginal(saved, resumed with { ProcessId = 43 }));
        Assert.False(ExpressionEditorReceiptPolicy.MatchesOriginal(saved, resumed with { Value = "jello??" }));
        Assert.False(ExpressionEditorReceiptPolicy.MatchesOriginal(saved, resumed with { Selection = new(6, 0) }));
        Assert.False(ExpressionEditorReceiptPolicy.MatchesOriginal(saved, resumed with { Composition = InputCompositionState.Unknown }));
        Assert.False(ExpressionEditorReceiptPolicy.MatchesOriginal(saved with { ReplacementRange = new(0, 1), Source = "h" }, resumed));
    }

    [Fact]
    public void ReadAndRestoreFollowFrozenMacOwnershipRule()
    {
        Assert.True(ExpressionEditorReceiptPolicy.PermitsHostRead(10, 10, 20, false));
        Assert.True(ExpressionEditorReceiptPolicy.PermitsHostRead(20, 10, 20, true));
        Assert.False(ExpressionEditorReceiptPolicy.PermitsHostRead(20, 10, 20, false));
        Assert.False(ExpressionEditorReceiptPolicy.PermitsHostRead(30, 10, 20, true));
        Assert.False(ExpressionEditorReceiptPolicy.PermitsHostRead(null, 10, 20, true));
        Assert.True(ExpressionEditorReceiptPolicy.ShouldRestoreHost(20, 10, 20, true));
        Assert.False(ExpressionEditorReceiptPolicy.ShouldRestoreHost(30, 10, 20, true));
        Assert.False(ExpressionEditorReceiptPolicy.ShouldRestoreHost(20, 10, 20, false));
    }
}
