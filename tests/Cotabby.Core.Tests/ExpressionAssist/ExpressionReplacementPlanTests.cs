using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;

namespace Cotabby.Core.Tests.ExpressionAssist;

public sealed class ExpressionReplacementPlanTests
{
    private static ExpressionEditorObservation Field(string value, int start, int length) =>
        new(1, "field", 1, 1, value, new(start, length), false, true, InputCompositionState.Clear, 1);

    [Fact]
    public void AsciiTriggerSupportsReplacementAndCancellationInOneBatch()
    {
        var receipt = ExpressionEditorReceiptPolicy.Capture(Field("abc??d", 5, 0), "??")!;
        var plan = ExpressionReplacementPlan.Create(receipt, "你好")!;
        Assert.Equal(2, plan.Backspaces);
        Assert.Equal("abc你好d", plan.ExpectedValue);
        var cancel = ExpressionReplacementPlan.Create(receipt, "")!;
        Assert.Equal(2, cancel.Backspaces);
        Assert.Equal("abcd", cancel.ExpectedValue);
        Assert.False(plan.RequiresRangeSelection);
    }

    [Theory]
    [InlineData("？？", "x")]
    [InlineData("？？", "")]
    [InlineData("😀😀", "字")]
    [InlineData("⁇⁇", "")]
    public void UnicodeTriggerUsesItsLiteralRangeForInsertionAndCancel(string trigger, string replacement)
    {
        string text = "a" + trigger + "b";
        var receipt = ExpressionEditorReceiptPolicy.Capture(Field(text, 1 + trigger.Length, 0), trigger)!;
        var plan = ExpressionReplacementPlan.Create(receipt, replacement)!;
        Assert.True(plan.RequiresRangeSelection); Assert.Equal(0, plan.Backspaces);
        Assert.Equal("a" + replacement + "b", plan.ExpectedValue);
    }

    [Fact]
    public void SelectedTextUsesHostSelectionWithoutUtf16BackspaceCounting()
    {
        var receipt = ExpressionEditorReceiptPolicy.Capture(Field("a😀b", 1, 2), null)!;
        var plan = ExpressionReplacementPlan.Create(receipt, "日")!;
        Assert.Equal(0, plan.Backspaces);
        Assert.Equal("a日b", plan.ExpectedValue);
        Assert.Null(ExpressionReplacementPlan.Create(receipt, ""));
    }

    [Fact]
    public void UnsafeOrOversizedOutputsAreRejectedBeforeAnyEffect()
    {
        var receipt = ExpressionEditorReceiptPolicy.Capture(Field("a", 1, 0), null)!;
        Assert.Null(ExpressionReplacementPlan.Create(receipt, "bad\ttext"));
        Assert.Null(ExpressionReplacementPlan.Create(receipt, new string('x', ExpressionEditorReceiptPolicy.MaximumValueUtf16Units)));
        Assert.Null(ExpressionReplacementPlan.Create(receipt, "\ud800"));
    }
}
