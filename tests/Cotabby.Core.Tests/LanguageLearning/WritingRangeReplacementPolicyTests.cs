using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Tests.Insertion;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class WritingRangeReplacementPolicyTests
{
    [Theory]
    [InlineData("你好😀。", 2)]
    [InlineData("café é.", 3)]
    [InlineData("ภาษาไทย。", 0)]
    public void ExactUnicodeRangeAroundMiddleCaretUsesSelectionInsteadOfBackspaces(string source, int caret)
    {
        var saved = InsertionTestFields.Create("prefix " + source + " suffix", 7 + caret) with { SupportsRangeReplacement = true };
        var range = new LearningTextRange(7, source.Length);
        Assert.True(WritingRangeReplacementPolicy.CanReplace(saved, range, source, "translated"));
        Assert.False(WritingSentenceReplacementPolicy.CanReplace(saved, range, source, "translated"));
        var current = saved with { Text = "prefix translated suffix", CaretOffset = 17 };
        Assert.True(WritingRangeReplacementPolicy.IsAcknowledged(saved, range, source, "translated", current));
        Assert.False(WritingRangeReplacementPolicy.IsAcknowledged(saved, range, source, "translated", current with { Text = "prefix translated CHANGED" }));
    }

    [Fact]
    public void SelectionRequiresExactSourceAndOriginalSelectionReceipt()
    {
        var saved = InsertionTestFields.Create("same. same.", 6) with { SupportsRangeReplacement = true, HasSelection = true, SelectionLength = 5 };
        Assert.True(WritingRangeReplacementPolicy.CanReplace(saved, new(6, 5), "same.", "autre."));
        Assert.False(WritingRangeReplacementPolicy.CanReplace(saved, new(0, 5), "same.", "autre."));
        Assert.False(WritingRangeReplacementPolicy.IsCurrent(saved, saved with { SelectionLength = 4 }));
        Assert.False(WritingRangeReplacementPolicy.IsCurrent(saved, saved with { CaretOffset = 0 }));
        Assert.False(WritingRangeReplacementPolicy.IsCurrent(saved, saved with { SupportsRangeReplacement = false }));
        Assert.False(WritingRangeReplacementPolicy.SameField(saved, saved with { CompositionSequence = 2 }));
        Assert.False(WritingRangeReplacementPolicy.SameField(saved, saved with { FocusChangeSequence = 2 }));
    }

    [Theory]
    [InlineData("a😀b", 2, 1)]
    [InlineData("aéb", 1, 1)]
    public void PartialScalarOrGraphemeCannotBeSelected(string value, int start, int length)
    {
        var saved = InsertionTestFields.Create(value, start) with { SupportsRangeReplacement = true, HasSelection = true, SelectionLength = length };
        Assert.False(WritingRangeReplacementPolicy.CanReplace(saved, new(start, length), value.Substring(start, length), "x"));
    }

    [Fact]
    public void UnsupportedCompositionOrIncompleteValueNeverGrantsRangePermission()
    {
        var saved = InsertionTestFields.Create("hello", 2) with { SupportsRangeReplacement = true };
        foreach (var unsafeField in new[] { saved with { Composition = InputCompositionState.Unknown }, saved with { IsSecure = true },
            saved with { IsTextWindowTruncated = true }, saved with { HasReliableSelection = false }, saved with { SupportsRangeReplacement = false } })
            Assert.False(WritingRangeReplacementPolicy.CanReplace(unsafeField, new(0, 5), "hello", "译文"));
    }
}
