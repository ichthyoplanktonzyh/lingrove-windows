using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class WritingCommittedMutationPolicyTests
{
    private static FocusedField Field(string text, int caret) => new() { ProcessId = 22, ProcessName = "fixture", FieldId = "1.2",
        Text = text, CaretOffset = caret, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty, IsSingleLine = false,
        IsSecure = false, HasReliableSelection = true, IsTextWindowTruncated = false, FocusChangeSequence = 2,
        Composition = InputCompositionState.Clear, CompositionSequence = 1 };
    [Fact]
    public void SameFieldCommittedMutationIsDistinctFromNavigationAndUnknownComposition()
    {
        var before = Field("first", 5);
        Assert.True(WritingCommittedMutationPolicy.HasMutation(before, before with { Text = "first!", CaretOffset = 6 }));
        Assert.False(WritingCommittedMutationPolicy.HasMutation(before, before with { CaretOffset = 3 }));
        Assert.False(WritingCommittedMutationPolicy.HasMutation(before, before with { Text = "first!", Composition = InputCompositionState.Unknown }));
        Assert.False(WritingCommittedMutationPolicy.HasMutation(before, before with { Text = "first!", FocusChangeSequence = 3 }));
        Assert.False(WritingCommittedMutationPolicy.HasMutation(before, before with { Text = "first!", IsTextWindowTruncated = true }));
    }

    [Fact]
    public void PreeditRetainsCommittedBaselineForCommitAndCancellation()
    {
        var baseline = Field("", 0);
        var preedit = baseline with { Text = "你", CaretOffset = 1, Composition = InputCompositionState.Composing, CompositionSequence = 2 };
        Assert.Same(baseline, WritingCommittedMutationPolicy.BaselineAfter(baseline, preedit));
        var committed = baseline with { Text = "你好", CaretOffset = 2, CompositionSequence = 3 };
        Assert.True(WritingCommittedMutationPolicy.HasMutation(baseline, committed));
        Assert.Same(committed, WritingCommittedMutationPolicy.BaselineAfter(baseline, committed));
        Assert.False(WritingCommittedMutationPolicy.HasMutation(baseline, baseline with { CompositionSequence = 3 }));
        var differentField = preedit with { FieldId = "other" };
        Assert.Same(differentField, WritingCommittedMutationPolicy.BaselineAfter(baseline, differentField));
    }
}
