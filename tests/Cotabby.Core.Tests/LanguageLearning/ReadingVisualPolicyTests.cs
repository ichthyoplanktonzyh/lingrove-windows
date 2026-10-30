using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;
using System.Text.RegularExpressions;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;

namespace Cotabby.Core.Tests.LanguageLearning;

/// <summary>Source-derived 3b VisualPolicy/OCRPolicy/OCRColumns observations. Coordinates are
/// reflected into Windows top-left pixels; these tests do not execute Swift or capture a screen.</summary>
public sealed class ReadingVisualPolicyTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void FrozenFrameStaysInsidePointedDisplayAndWindowWithoutDownsampling(double scale)
    {
        var display = new ScreenRect(-1600, -1900, 1600, 1000); var window = new ScreenRect(-1700, -2100, 1100, 1100);
        var frame = Snapshot(-1595, -1895, display, window, scale); Assert.NotNull(frame);
        Assert.True(Contains(display, frame.Value)); Assert.True(Contains(window, frame.Value)); Assert.True(Contains(frame.Value, -1595, -1895));
        Assert.True(frame.Value.Width * frame.Value.Height <= 16_000_000);
        Assert.Null(Snapshot(0, 0, display, window, scale)); Assert.Null(Snapshot(-1595, -1895, display, default, scale));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void WindowsOcrDimensionCeilingCapsTheFrozenFrameAtHighDpi(double scale)
    {
        var bounds = new ScreenRect(-8000, -4000, 16000, 8000);
        var frame = Snapshot(0, 0, bounds, bounds, scale, 2600)!.Value;
        Assert.True(frame.Width <= 2600); Assert.True(frame.Height <= 2600);
        Assert.True(frame.Width * frame.Height <= 16_000_000); Assert.True(Contains(frame, 0, 0));
    }
    [Fact]
    public void ExpansionUsesTheClippedAxisAndProvenSentenceSides()
    {
        var frame = new ScreenRect(-1000, -1500, 1000, 600); var crop = new ScreenRect(-860, -1320, 720, 240);
        Assert.Equal(new(-1000, -1320, 1000, 240), Expanded(crop, new(-200, -1170, 60, 20), null, frame, 1));
        Assert.Equal(new(-860, -1500, 720, 600), Expanded(crop, new(-700, -1320, 100, 20), null, frame, 1));
        Assert.Equal(frame, Expanded(crop, null, null, frame, 1));
        Assert.Equal(crop, Expanded(crop, new(-700, -1170, 100, 20), null, frame, 1));
        var context = new ReadingContext("target", 0, 6, ReadingBoundary.Sentence, ReadingBoundary.Unknown, ReadingContextMethod.Ocr);
        Assert.Equal(new(-860, -1320, 860, 240), Expanded(crop, new(-200, -1170, 60, 20), context, frame, 1));
    }
    private static ReadingVisualText Repeated() => new([new("history history", new(100, 180, 600, 20))],
        [new("history", new(100, 180, 50, 20), 0, 0, 7), new("history", new(500, 180, 50, 20), 0, 8, 7)]);
    [Fact]
    public void ExactRepeatedOccurrenceAndSmallToleranceDoNotSelectAmbiguousNeighbours()
    {
        var text = Repeated(); Assert.Equal(8, Hit(520, 190, text)!.Start); Assert.Equal(8, Hit(552, 190, text)!.Start);
        Assert.Null(Hit(700, 190, text));
        Assert.Null(Hit(520, 190, text with { Words = [.. text.Words, text.Words[0] with { Bounds = text.Words[1].Bounds }] }));
    }
    [Theory]
    [InlineData(9, 1)] [InlineData(8, .1)] [InlineData(8, double.NaN)]
    public void BadOffsetsOrConfidenceCannotInventAHittableWord(int offset, double confidence)
    {
        var text = Repeated(); Assert.Null(Hit(520, 190, text with { Words = [text.Words[1] with { Start = offset, Confidence = confidence }] }));
    }
    [Fact]
    public void SharedHyphenatedEnvelopeReturnsThePrintedCompound()
    {
        var box = new ScreenRect(100, 180, 300, 20);
        var text = new ReadingVisualText([new("OpenAI-compatible chat endpoint", new(100, 180, 800, 20))],
            [new("OpenAI", box, 0, 0, 6), new("compatible", box, 0, 7, 10)]);
        Assert.Equal("OpenAI-compatible", Hit(250, 190, text)!.Text);
        var phrase = new ReadingVisualText([new("OpenAI compatible", box)], [new("OpenAI", box, 0, 0, 6), new("compatible", box, 0, 7, 10)]);
        Assert.Null(Hit(250, 190, phrase));
    }
    [Fact]
    public void WindowsWordOrderAnchorsExactUtf16AndRejectsUnobservedLetterGaps()
    {
        var text = ReadingVisualText.FromWindows([new("😀 history history", [new("😀", new(0, 0, 20, 20)),
            new("history", new(30, 0, 70, 20)), new("history", new(110, 0, 70, 20))])], 200, 30);
        Assert.Equal(new[] { 0, 3, 11 }, text.Words.Select(word => word.Start)); Assert.All(text.Words, word => Assert.Null(word.Confidence));
        var missing = ReadingVisualText.FromWindows([new("other history history", [new("history", new(30, 0, 70, 20))])], 200, 30);
        Assert.Empty(missing.Words);
    }
    private static int? Script(int scalar) => scalar is >= 0x4E00 and <= 0x9FFF ? 17 : scalar is >= 0x600 and <= 0x6FF ? 2 : 25;
    private static ReadingVisualContext Context() => new(new(Script), text => [new(0, text.Length)]);
    [Fact]
    public void CompatibleWrappedRowsJoinWhileASeparateColumnAndHeadingStayOut()
    {
        var lines = new ReadingVisualLine[] { new("Keep your", new(50, 100, 200, 20)), new("history safe.", new(50, 125, 200, 20)),
            new("Unrelated sidebar", new(600, 115, 200, 20)), new("Separate heading", new(50, 80, 200, 8)) };
        var word = new ReadingVisualWord("history", new(50, 125, 80, 20), 1, 0, 7);
        var block = Context().Resolve(word, new(lines, [word]), 1000, 400); Assert.NotNull(block);
        Assert.Equal("Keep your history safe.", block.Window.Text); Assert.Equal(10, block.Window.TargetStart);
        Assert.Equal("history", block.Context!.Text.Substring(block.Context.TargetStart, block.Context.TargetLength));
        Assert.Equal(new[] { 0, 1 }, block.Sources.Select(source => source.Line));
    }
    [Fact]
    public void ChineseSoftWrapRetainsLiteralTextWithoutAnInventedSpace()
    {
        var word = new ReadingVisualWord("历史记录", new(300, 184, 200, 16), 1, 2, 4);
        var block = Context().Resolve(word, new([new("我们需要", new(100, 164, 800, 16)), new("保留历史记录。", new(100, 184, 800, 16))], [word]), 1000, 400)!;
        Assert.Equal("我们需要保留历史记录。", block.Window.Text); Assert.Equal(6, block.Context!.TargetStart);
    }
    [Fact]
    public void CorroboratedNarrowGuttersPreventJoiningAdjacentColumns()
    {
        var lines = new ReadingVisualLine[] { new("Keep your", new(100, 156, 300, 20)), new("Unrelated right start", new(412, 156, 300, 20)),
            new("history safe.", new(100, 180, 300, 20)), new("Unrelated right end", new(412, 180, 300, 20)) };
        var word = new ReadingVisualWord("history", new(100, 180, 80, 20), 2, 0, 7);
        var block = Context().Resolve(word, new(lines, [word]), 1000, 400)!;
        Assert.Equal("Keep your history safe.", block.Window.Text);
    }
    [Fact]
    public void TallerInlineFragmentsPreserveTheirOriginalTargetRangeAndUniformRtlRowsReverseVisualOrder()
    {
        var lines = new ReadingVisualLine[] { new("Earlier sentence. Keep your", new(50, 180, 400, 20)), new("history", new(455, 168, 100, 32)),
            new("safe. Next sentence.", new(560, 180, 300, 20)) };
        var word = new ReadingVisualWord("history", lines[1].Bounds!.Value, 1, 0, 7);
        var block = Context().Resolve(word, new(lines, [word]), 1000, 400)!;
        Assert.Equal("Earlier sentence. Keep your history safe. Next sentence.", block.Window.Text);
        Assert.Equal(block.Window.Text.IndexOf("history", StringComparison.Ordinal), block.Window.TargetStart);
        var layout = new ReadingVisualLayout(Script);
        var row = Assert.Single(layout.Rows(new([new("العالم", new(100, 100, 50, 20)), new("مرحبا", new(156, 100, 50, 20))], [])));
        Assert.Equal("مرحبا العالم", row.Line.Text); Assert.Equal(0, row.Sources[1].RowStart);
        Assert.Equal(2, layout.Rows(new([new("world", new(100, 100, 50, 20)), new("مرحبا", new(156, 100, 50, 20))], [])).Count);
    }
    [Fact]
    public void RepeatedSidebarSplitsMergedProviderLinesWithBothOriginalSourceRangesIntact()
    {
        string[] texts = ["Earlier sentence. Keep your review Repeated sidebar label starts.",
            "history safe. Next sentence. Repeated sidebar label history ends."];
        var lines = new List<ReadingVisualLine>(); var words = new List<ReadingVisualWord>();
        for (int index = 0; index < texts.Length; index++)
        {
            double y = 140 + index * 32; lines.Add(new(texts[index], new(50, y, 900, 20))); double x = 50;
            foreach (Match match in Regex.Matches(texts[index], @"\w+"))
            {
                if (match.Value == "Repeated") x = 600;
                double width = match.Length * 6; words.Add(new(match.Value, new(x, y, width, 20), index, match.Index, match.Length)); x += width + 4;
            }
        }
        var extraction = new ReadingVisualText(lines, words); Assert.Equal(4, new ReadingVisualLayout(Script).Rows(extraction).Count);
        var matches = words.Where(word => word.Text == "history").ToArray();
        for (int side = 0; side < 2; side++)
        {
            var block = Context().Resolve(matches[side], extraction, 1000, 400)!;
            Assert.Contains(side == 0 ? "Keep your" : "sidebar", block.Window.Text);
            Assert.DoesNotContain(side == 0 ? "sidebar" : "Keep your", block.Window.Text);
            foreach (var source in block.Sources) Assert.Equal(lines[source.Line].Text.Substring(source.OriginalStart, source.Length),
                block.Window.Text.Substring(source.WindowStart, source.Length));
        }
    }
    [Fact]
    public void RepeatedTargetRemainsTheSecondOccurrenceAndClippingKeepsGraphemeEdges()
    {
        string line = "The bank was beside another bank."; int start = line.LastIndexOf("bank", StringComparison.Ordinal);
        var word = new ReadingVisualWord("bank", new(700, 180, 100, 16), 0, start, 4);
        var block = Context().Resolve(word, new([new(line, new(100, 180, 800, 16))], [word]), 1000, 400)!;
        Assert.Equal(start, block.Window.TargetStart); Assert.Equal(start, block.Context!.TargetStart);
        string large = new string('a', 700) + "😀" + " target " + new string('b', 700);
        word = new("target", new(700, 180, 100, 16), 0, 703, 6);
        block = Context().Resolve(word, new([new(large, new(100, 180, 800, 16))], [word]), 1000, 400)!;
        Assert.True(block.Window.Text.Length - block.Window.TargetLength <= 1024); Assert.DoesNotContain('\uFFFD', block.Window.Text);
        Assert.Equal("target", block.Window.Text.Substring(block.Window.TargetStart, block.Window.TargetLength));
    }
    [Fact]
    public void PointerVisualReceiptRequiresAnOriginBoundedFrameAndExactAnchor()
    {
        var selection = new ReadingSelection(Guid.NewGuid(), "history", 42, "", 3, null, 7, false, true, InputCompositionState.Unknown, 0, new(200, 200, 70, 20))
        { Source = ReadingSourceKind.Pointer, ScreenReceipt = new(Guid.NewGuid(), new(0, 0, 2000, 1000), new(0, 0, 2000, 1000), new(100, 100, 720, 240))
            { WindowOrigin = new(Guid.NewGuid(), new(50, 50, 1000, 800)) } };
        ReadingSelectionPolicy.ValidateReceipt(selection);
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(selection with { Source = ReadingSourceKind.Selection }));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(selection with { Anchor = new(900, 200, 70, 20) }));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(selection with { Composition = InputCompositionState.Clear }));
    }
}
