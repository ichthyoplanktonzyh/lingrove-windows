using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class LearningInputMethodPolicyTests
{
    [Fact]
    public void PositiveMarkedTextRejectsPreviewUntilCompositionIsCommitted()
    {
        var assessment = LearningInputMethodPolicy.Assess(new LearningTextRange(4, 2), isComposingInputMethod: true);

        Assert.Equal(LearningInputMethodDisposition.RejectPreview, assessment.Disposition);
        Assert.False(assessment.PermitsReplacement);
        Assert.Null(assessment.ReplacementUnavailableReason);
        Assert.Contains("candidate", assessment.PreviewRejectionReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitEmptyMarkedRangeAllowsReplacementEvenForCompositionCapableInput()
    {
        var assessment = LearningInputMethodPolicy.Assess(new LearningTextRange(int.MinValue, 0), isComposingInputMethod: true);

        Assert.Equal(LearningMarkedRangeEvidence.ExplicitlyEmpty, assessment.Evidence);
        Assert.True(assessment.PermitsReplacement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedNegativeRangeIsCopyOnlyRegardlessOfInputSource(bool composing)
    {
        var assessment = LearningInputMethodPolicy.Assess(new LearningTextRange(4, -1), composing);

        Assert.Equal(LearningInputMethodDisposition.CopyOnly, assessment.Disposition);
        Assert.False(assessment.PermitsReplacement);
        Assert.Equal(LearningInputMethodPolicy.UnavailableMarkedRangeReason, assessment.ReplacementUnavailableReason);
    }

    [Theory]
    [InlineData(true, LearningMarkedRangeEvidence.UnavailableWithComposition)]
    [InlineData(false, LearningMarkedRangeEvidence.UnavailableWithoutComposition)]
    public void MissingRangeStaysUnknownButRetainsMacConfirmedReplacementBehavior(
        bool composing, LearningMarkedRangeEvidence expectedEvidence)
    {
        var assessment = LearningInputMethodPolicy.Assess(markedRange: null, isComposingInputMethod: composing);

        Assert.Equal(LearningInputMethodDisposition.ReplacementAllowed, assessment.Disposition);
        Assert.Equal(expectedEvidence, assessment.Evidence);
        Assert.Null(assessment.ReplacementUnavailableReason);
    }
}
