using Cotabby.Core.Support;

namespace Cotabby.Core.Tests;

/// <summary>Latest committed Mac06bf81fe line/echo regressions, grouped by behavior rather than
/// expanding every cross-product into a separate test. Expected text comes from the Mac tests;
/// the historical v1 corpus keeps its original reference and bytes.</summary>
public sealed class LatestSuggestionNormalizerTests
{
    private static SuggestionNormalizationRequest Request(string preceding = "Hello", string? prefix = null, bool multiline = false) =>
        new("PROMPT", prefix ?? preceding, preceding, IsMultiLineEnabled: multiline);
    private static string Normalize(string raw, string preceding = "Hello", bool multiline = false) =>
        SuggestionTextNormalizer.NormalizeDetailed(raw, Request(preceding, multiline: multiline)).Text;

    [Fact]
    public void RemovedLineBreakKeepsTheSurvivingWordBoundaryAcrossScriptsAndPunctuation()
    {
        (string Preceding, string Raw, string Expected)[] cases = [
            ("Hello", "\nworld", " world"), ("почему то на русском", "\nчасто пишут", " часто пишут"),
            ("Hello", "\r\nworld", " world"), ("Hello ", "\nworld", "world"), ("Hello\t", "\nworld", "world"),
            ("Hello\n", "\nworld", "world"), ("Hello", "\n world", " world"), ("", "\nHello", "Hello"),
            ("Hello", "\n, world", ", world"), ("(", "\nhello", "hello"), ("今日は", "\n良い天気", "良い天気"),
            ("今日は", "\n、良い天気", "、良い天気"), ("sched", "PROMPT\nschedule", "ule"),
            ("Hello", "Hello\nworld", " world"), ("Hello", "Continuation:\nworld", " world"),
            ("Hello", "<|im_start|>\nworld", " world"), ("sched", "\nule", " ule"),
            ("sched", "ule", "ule"), ("1", "50K", "50K"), ("hello world", "\nworld is great", " is great")];
        foreach (var row in cases) Assert.Equal(row.Expected, Normalize(row.Raw, row.Preceding));
    }

    [Fact]
    public void FormattingPromptAndLabelCleanupRetainOnlyTheFinalSeamsNewline()
    {
        (string Preceding, string Raw, string Expected)[] cases = [
            ("Hello", "\t\nworld", " world"), ("Hello", "\t\r\nworld", " world"), ("Hello", "\t\n\tworld", " world"),
            ("sched", "\tule", "ule"), ("sched", "ule\n", "ule"), ("Hello", "PROMPT\nworld", " world"),
            ("Hello", "PROMPT\t\nworld", " world"), ("sched", "PROMPT\nschedule", "ule"),
            ("Hello", "PROMPT\nHello\nworld", " world"), ("sched", "PROMPT\tule", "ule"),
            ("Hello", " \nworld", " world"), ("Hello", " \t\n \tworld", " world"),
            ("Hello", "PROMPT \nworld", " world"), ("Hello", " \nContinuation:world", " world"),
            ("Hello", "Continuation: \nworld", " world"), ("Hello", "Task: \nContinuation:world", " world"),
            ("sched", "PROMPT \nschedule", "ule"), ("Hello", "PROMPT \nHello \nworld", " world"),
            ("Hello", "  world", "  world"), ("Hello", "\n  world", " world"), ("Hello ", " \nworld", "world"),
            ("Hello", " \n, world", ", world"), ("今日は", " \n良い天気", "良い天気")];
        foreach (var row in cases) Assert.Equal(row.Expected, Normalize(row.Raw, row.Preceding));
        Assert.Equal(" world", SuggestionTextNormalizer.NormalizeDetailed("BACKEND\nworld", Request(), ["BACKEND"]).Text);
        Assert.Equal("world", Normalize("PROMPT\nworld", multiline: true));
        Assert.Equal(" \nworld", Normalize(" \nworld", multiline: true));
        Assert.Equal(" \nworld", Normalize("PROMPT \nworld", multiline: true));
    }

    [Fact]
    public void PrefixOwnedIndentationSurvivesUntilTheCompleteEchoCanBeRemoved()
    {
        foreach (string prefix in new[] { " sched", "  sched", "\tsched", "\n sched" })
        foreach (string separator in new[] { "\n", "\r\n", "\t\n", " \n" })
        foreach (string suffix in new[] { "u", "ul", "ule" })
            Assert.Equal(suffix, SuggestionTextNormalizer.NormalizeDetailed("PROMPT" + separator + prefix + suffix, Request(prefix)).Text);
    }

    [Fact]
    public void CumulativePartialsAndPrefaceFilteringKeepOneStableInsertionBoundary()
    {
        foreach (string partial in new[] { "w", "wor", "world" }) Assert.Equal(" " + partial, Normalize("\n" + partial));
        Assert.Equal(new SuggestionNormalizationResult("", CompletionSuppressionReason.EmptyGeneration),
            SuggestionTextNormalizer.NormalizeDetailed("\n", Request()));
        Assert.Equal(" world", Normalize("\nworld Nearby on screen: Inbox"));
        Assert.Equal("", Normalize("\nNotes the writer keeps in mind: Imperum"));
        Assert.Equal(" from you", Normalize(" from you\nThe user usually writes in English.", "I look forward to hearing", multiline: true));
        Assert.Equal("a book Written by her", Normalize("a book Written by her"));
        Assert.Equal("a book", Normalize("a book\n  Written by the writer", multiline: true));
    }
}
