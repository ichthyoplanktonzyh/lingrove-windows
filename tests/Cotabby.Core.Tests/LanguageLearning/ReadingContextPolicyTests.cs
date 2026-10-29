using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.Core.Tests.LanguageLearning;

/// <summary>Source-derived cases from LearningReadingTargetPolicyTests at 3b; not Swift execution.</summary>
public sealed class ReadingContextPolicyTests
{
    [Theory]
    [InlineData("following", "following ")]
    [InlineData("following", "following\r\n")]
    [InlineData("café", "cafe\u0301")]
    [InlineData("be\u00a0used\nin", "be used in")]
    public void AlignmentReplacesOnlyAnchoredOccurrenceAndRecomputesUtf16(string actual, string selected)
    {
        string text = $"Earlier {actual}, later {actual} remains here."; int start = text.LastIndexOf(actual, StringComparison.Ordinal);
        var original = new ReadingAcquisitionWindow(text, start, actual.Length, ReadingBoundary.Container, ReadingBoundary.Container, ReadingContextMethod.Characters);
        var aligned = ReadingContextPolicy.Align(original, selected)!;
        Assert.Equal(start, aligned.TargetStart); Assert.Equal(selected.Length, aligned.TargetLength);
        Assert.Equal(selected, aligned.Text.Substring(start, selected.Length)); Assert.EndsWith(" remains here.", aligned.Text);
        var resolved = ReadingContextPolicy.Resolve(aligned, selected, text => [new(0, text.Length)]);
        Assert.NotNull(resolved); Assert.True(ReadingSelectionPolicy.ValidContext(resolved, selected));
    }
    [Theory]
    [InlineData("history", "History")] [InlineData("history.", "history")] [InlineData("note book", "notebook")]
    [InlineData(" ", "\n")] [InlineData("be used in", "used in")]
    public void LexicalDifferencesAreNotAlignment(string a, string b) => Assert.False(ReadingContextPolicy.Equivalent(a, b));
    [Fact]
    public void SplitGraphemeCannotBeRepairedAndResolvedContractRemainsExact()
    {
        const string text = "The cafe\u0301 and café"; int start = text.IndexOf("cafe", StringComparison.Ordinal);
        Assert.Null(ReadingContextPolicy.Align(new(text, start, 4, ReadingBoundary.Container, ReadingBoundary.Container, ReadingContextMethod.Characters), "café"));
        var wrong = new ReadingContext("Cafe\u0301 noir", 0, 5, ReadingBoundary.Container, ReadingBoundary.Container, ReadingContextMethod.Characters);
        Assert.False(ReadingSelectionPolicy.ValidContext(wrong, "Café"));
    }
    [Fact]
    public void WindowRangeReallocatesUnusedBudgetAndEnforcesBound()
    {
        Assert.Equal(new LearningTextRange(0, 1028), ReadingContextPolicy.WindowRange(4, 4, 3000));
        Assert.Equal(new LearningTextRange(1972, 1028), ReadingContextPolicy.WindowRange(2996, 4, 3000));
        Assert.Null(ReadingContextPolicy.WindowRange(0, 3073, 4000));
    }
    [Fact]
    public void IntroducedCrLfPreservesLiteralAndMasksOnlyTheSelectedToken()
    {
        const string text = "Keep your history safely.\nSeparate paragraph."; int start = text.IndexOf("history", StringComparison.Ordinal);
        var aligned = ReadingContextPolicy.Align(new(text, start, 7, ReadingBoundary.Container, ReadingBoundary.Container, ReadingContextMethod.Characters), "history\r\n")!;
        Assert.True(aligned.TargetIntroducesLineBreaks);
        var context = ReadingContextPolicy.Resolve(aligned, "history\r\n", tokenized =>
        { Assert.Contains("history  ", tokenized); return [new(0, tokenized.IndexOf("\n", StringComparison.Ordinal))]; });
        Assert.NotNull(context); Assert.Equal("history", context.Text.Substring(context.TargetStart, context.TargetLength));
        Assert.DoesNotContain("Separate", context.Text);
    }
}
