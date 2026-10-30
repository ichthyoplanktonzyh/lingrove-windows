using System.Text.RegularExpressions;
using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;

namespace Cotabby.Core.Tests.LanguageLearning;

/// <summary>Committed 3b SelectionOCRPolicy/GesturePolicy source-derived observations. Exact
/// selected code units, repeated occurrences and raw-window versus sentence targets stay distinct.</summary>
public sealed class ReadingSelectionOcrPolicyTests
{
    private static readonly ScreenRect Region = new(0, 0, 1000, 600);
    private static IReadOnlyList<LearningTextRange> Sentences(string text) => Regex.Matches(text, @"[^.!?。！？]+[.!?。！？]?")
        .Select(match => new LearningTextRange(match.Index, match.Length)).ToArray();
    private static ReadingSelectionOcrPolicy Policy()
    { var layout = new ReadingVisualLayout(_ => 25); return new(layout, new(layout, Sentences), Sentences); }
    private static ReadingVisualText Fixture(params string[] texts)
    {
        var lines = new List<ReadingVisualLine>(); var words = new List<ReadingVisualWord>();
        for (int i = 0; i < texts.Length; i++)
        {
            double y = 200 + i * 25; lines.Add(new(texts[i], new(100, y, 800, 20), 1));
            foreach (Match match in Regex.Matches(texts[i], @"[\p{L}\p{M}\p{N}]+"))
                words.Add(new(match.Value, new(100 + 800d * match.Index / texts[i].Length, y, 800d * match.Length / texts[i].Length, 20), i, match.Index, match.Length, 1));
        }
        return new(lines, words);
    }
    private static ReadingSelection Selected(string target, ReadingVisualText extraction, bool last = false)
    {
        var matches = extraction.Words.Where(word => word.Text == target).ToArray(); var hit = last ? matches[^1] : matches[0];
        return new(Guid.NewGuid(), target, 42, "field", 3, null, target.Length, false, true, InputCompositionState.Clear, 1, hit.Bounds, SelectionAnchorId: Guid.NewGuid())
        { SelectionBounds = hit.Bounds, CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(200.2) };
    }
    private static ReadingContext Quote(string text, string target) => ReadingContextPolicy.Resolve(
        new(text, text.IndexOf(target, StringComparison.Ordinal), target.Length, ReadingBoundary.Crop, ReadingBoundary.Crop, ReadingContextMethod.MarkerWindow), target, Sentences)!;
    [Fact]
    public void ExactSelectionGetsSentenceContextWithoutReplacingTheNativeTarget()
    {
        var extraction = Fixture("Earlier sentence. He sat on the bank of the river. Another sentence."); var selection = Selected("bank", extraction);
        var alignment = Policy().Align(selection, extraction, Region, 1000, 600, 1); Assert.Equal(ReadingSelectionOcrPolicy.Reason.Matched, alignment.Reason);
        var context = alignment.Match!.Context; Assert.Equal("He sat on the bank of the river.", context.Text.Trim());
        Assert.True(Complete(context)); Assert.Equal(ReadingContextMethod.SelectionOcr, context.Method);
        Assert.Equal(selection.Text, context.Window!.Text.Substring(context.Window.TargetStart, context.Window.TargetLength));
    }
    [Fact]
    public void RepeatedWordsUseSpatialIdentityAndOverlappingOccurrencesRemainAmbiguous()
    {
        var extraction = Fixture("Earlier sentence. A bank financed another bank by the river. Next sentence."); var selection = Selected("bank", extraction, true);
        var result = Policy().Align(selection, extraction, Region, 1000, 600, 1).Match!.Context;
        Assert.Equal(result.Text.LastIndexOf("bank", StringComparison.Ordinal), result.TargetStart);
        extraction = extraction with { Words = extraction.Words.Select(word => word.Text == "bank" ? word with { Bounds = selection.SelectionBounds!.Value } : word).ToArray() };
        Assert.Equal(ReadingSelectionOcrPolicy.Reason.Ambiguous, Policy().Align(selection, extraction, Region, 1000, 600, 1).Reason);
    }
    [Fact]
    public void PresentationAnchorOrAnotherWordCannotReplaceActualSelectionGeometry()
    {
        var extraction = Fixture("Earlier sentence. Keep your history safe. Next sentence."); var selection = Selected("history", extraction);
        Assert.Equal(ReadingSelectionOcrPolicy.Reason.InvalidGeometry, Policy().Align(selection with { SelectionBounds = null }, extraction, Region, 1000, 600, 1).Reason);
        Assert.Null(Policy().Align(selection with { SelectionBounds = new(850, 100, 80, 24) }, extraction, Region, 1000, 600, 1).Match);
    }
    [Theory]
    [InlineData("hivory")] [InlineData("h1story")]
    public void HighlightedOcrSpellingRequiresPreciseGeometryAndExactAdjacentNativeText(string recognized)
    {
        var extraction = Fixture($"Earlier sentence. Keep your review {recognized} safely. Next sentence."); var hit = Selected(recognized, extraction);
        var selection = hit with { Text = "history", SelectionLength = 7 };
        Assert.Null(Policy().Align(selection, extraction, Region, 1000, 600, 1).Match);
        selection = selection with { Context = Quote("your review history safely.", "history") };
        var match = Policy().Align(selection, extraction, Region, 1000, 600, 1).Match; Assert.NotNull(match);
        Assert.Contains("Keep your review history safely.", match.Context.Text);
        Assert.Equal("history", match.Context.Window!.Text.Substring(match.Context.Window.TargetStart, match.Context.Window.TargetLength));
        Assert.Null(Policy().Align(selection with { SelectionBounds = new(100, 200, 800, 20) }, extraction, Region, 1000, 600, 1).Match);
        Assert.Null(Policy().Align(selection with { Context = Quote("the search history today.", "history") }, extraction, Region, 1000, 600, 1).Match);
    }
    [Fact]
    public void SelectedWhitespaceLivesInTheExactWindowWhileTheVisibleSentenceTargetIsTrimmed()
    {
        var extraction = Fixture("Earlier sentence. Keep your review hivory safely. Next sentence."); var hit = Selected("hivory", extraction);
        const string selected = "history \n"; var ax = Quote("your review history \n safely.", selected);
        var selection = hit with { Text = selected, SelectionLength = selected.Length, Context = ax };
        var context = Policy().Align(selection, extraction, Region, 1000, 600, 1).Match!.Context;
        Assert.Equal(selected, context.Window!.Text.Substring(context.Window.TargetStart, context.Window.TargetLength));
        Assert.Equal("history", context.Text.Substring(context.TargetStart, context.TargetLength));
        Assert.Same(context, ReadingSelectionOcrPolicy.Preferred(ax, context, selected));
        Assert.DoesNotContain("Window", System.Text.Json.JsonSerializer.Serialize(context));
    }
    [Fact]
    public void CanonicalEquivalentAndSoftWrappedTargetsPreserveAllOriginalSelectedCodeUnits()
    {
        var extraction = Fixture("Earlier sentence. Visit the café today. Next sentence."); var selection = Selected("café", extraction) with { Text = "cafe\u0301", SelectionLength = 5 };
        var context = Policy().Align(selection, extraction, Region, 1000, 600, 1).Match!.Context;
        Assert.Equal("cafe\u0301", context.Window!.Text.Substring(context.Window.TargetStart, context.Window.TargetLength));
        extraction = Fixture("Earlier sentence. We preserve your review", "history safely. Another sentence.");
        var first = extraction.Words.First(word => word.Text == "your"); var last = extraction.Words.First(word => word.Text == "history");
        const string selected = "your review\nhistory"; var box = Union(first.Bounds, last.Bounds);
        selection = Selected("history", extraction) with { Text = selected, SelectionLength = selected.Length, SelectionBounds = box };
        context = Policy().Align(selection, extraction, Region, 1000, 600, 1).Match!.Context;
        Assert.Contains("We preserve your review\nhistory safely.", context.Text);
        Assert.Equal(selected, context.Window!.Text.Substring(context.Window.TargetStart, context.Window.TargetLength));
    }
    [Fact]
    public void CoarseTextRunNeedsRealNeighbourEvidenceAndAWholePageCannotCorroborateAWord()
    {
        var extraction = Fixture("Earlier sentence. Keep your review history safely. Next sentence."); var selection = Selected("history", extraction);
        selection = selection with { SelectionBounds = new(100, 200, 800, 20) };
        Assert.Null(Policy().Align(selection, extraction, Region, 1000, 600, 1).Match);
        selection = selection with { Context = Quote("your review history safely.", "history") };
        Assert.NotNull(Policy().Align(selection, extraction, Region, 1000, 600, 1).Match);
        Assert.Null(Policy().Align(selection with { SelectionBounds = Region }, extraction, Region, 1000, 600, 1).Match);
    }
    [Fact]
    public void NativeContextWinsTiesOrConflictingNeighboursWhileCompatibleCoverageCanImprove()
    {
        var ax = Quote("your history safely.", "history"); var extraction = Fixture("Earlier sentence. Keep your history safely. Next sentence.");
        var selection = Selected("history", extraction) with { Context = ax }; var visual = Policy().Align(selection, extraction, Region, 1000, 600, 1).Match!.Context;
        Assert.Same(visual, ReadingSelectionOcrPolicy.Preferred(ax, visual, "history"));
        Assert.Same(visual, ReadingSelectionOcrPolicy.Preferred(visual, ax, "history"));
        var conflict = Quote("the search history today.", "history"); Assert.Same(ax, ReadingSelectionOcrPolicy.Preferred(ax, conflict, "history"));
    }
    [Fact]
    public void GestureEvidenceMustBeRecentBoundAndCannotComeFromTheCardAnchor()
    {
        var extraction = Fixture("Earlier sentence. Keep your history safe. Next sentence."); var selection = Selected("history", extraction) with { SelectionBounds = null };
        Assert.Null(ReadingGesturePolicy.SearchBounds(selection)); var box = selection.Anchor;
        var gesture = new ReadingSelectionGesture(selection.Id, box.X + box.Width / 2, box.Y + box.Height / 2, box.X + box.Width / 2, box.Y + box.Height / 2,
            ReadingGestureKind.DoubleClick, DateTimeOffset.UnixEpoch.AddSeconds(200));
        selection = selection with { Gesture = gesture };
        Assert.NotNull(ReadingGesturePolicy.SearchBounds(selection, now: DateTimeOffset.UnixEpoch.AddSeconds(205)));
        Assert.NotNull(Policy().Align(selection, extraction, Region, 1000, 600, 1).Match);
        Assert.Null(ReadingGesturePolicy.SearchBounds(selection, now: DateTimeOffset.UnixEpoch.AddSeconds(209)));
        Assert.Null(ReadingGesturePolicy.SearchBounds(selection with { Gesture = gesture with { SelectionId = Guid.NewGuid() } }));
        Assert.Null(ReadingGesturePolicy.SearchBounds(selection with { Gesture = gesture with { CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(201) } }));
        Assert.Null(ReadingGesturePolicy.SearchBounds(selection with { Gesture = gesture with { CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(190) } }));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReversedMultilineDragsMustBracketTheActualFirstAndLastWords(bool reverse)
    {
        var first = new ScreenRect(100, 100, 50, 20); var last = new ScreenRect(20, 130, 60, 20);
        var gesture = new ReadingSelectionGesture(Guid.NewGuid(), reverse ? 80 : 100, reverse ? 140 : 110, reverse ? 100 : 80, reverse ? 110 : 140, ReadingGestureKind.Drag, DateTimeOffset.UnixEpoch);
        Assert.True(ReadingGesturePolicy.Agrees(gesture, first, last));
        Assert.False(ReadingGesturePolicy.Agrees(gesture, first with { X = first.X + 80 }, last));
        Assert.False(ReadingGesturePolicy.Agrees(gesture, first, last with { Y = last.Y + 40 }));
    }
}
