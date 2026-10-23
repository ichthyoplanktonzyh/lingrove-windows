using Cotabby.Core.Text;

namespace Cotabby.Core.Tests.Text;

public sealed class LiteralTextSelectionTests
{
    [Theory]
    [InlineData(2, 7)]
    [InlineData(7, 2)]
    public void ReverseAndForwardRangesKeepTheSameRawText(int anchor, int extent)
    {
        var selection = LiteralTextSelection.Create("  alpha <b>\n", anchor, extent)!;
        Assert.Equal(2, selection.Start); Assert.Equal(5, selection.Length);
        Assert.Equal("alpha", selection.Text);
    }

    [Fact]
    public void PartialCombiningEmojiAndCrlfRangesExpandToWholeGraphemes()
    {
        Assert.Equal("e\u0301", LiteralTextSelection.Create("cafe\u0301!", 4, 5)!.Text);
        const string emoji = "👩🏽‍💻";
        Assert.Equal(emoji, LiteralTextSelection.Create("x" + emoji + "y", 2, 4)!.Text);
        Assert.Equal("\r\n", LiteralTextSelection.Create("x\r\ny", 1, 2)!.Text);
    }

    [Fact]
    public void WhitespaceAndLiteralMarkupAreNotTrimmedOrParsed()
    {
        const string text = "\t<b>cafe\u0301</b> العربية\n";
        var selection = LiteralTextSelection.Create(text, 0, text.Length)!;
        Assert.Same(text, selection.Source); Assert.Equal(text, selection.Text);
        Assert.NotEqual(text.Normalize(), selection.Text);
    }

    [Fact]
    public void CollapsedRangeContainsNoCopyPayload()
    {
        var selection = LiteralTextSelection.Create("text", 2, 2)!;
        Assert.Equal(0, selection.Length); Assert.Empty(selection.Text);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    public void InvalidOffsetsRejectRatherThanGuessingAnotherSelection(int anchor, int extent) =>
        Assert.Null(LiteralTextSelection.Create("text", anchor, extent));

    [Fact]
    public void UnrepresentableSourceCannotCreateAClipboardSelection()
    {
        Assert.Null(LiteralTextSelection.Create("", 0, 0));
        Assert.Null(LiteralTextSelection.Create("a\0b", 0, 1));
        Assert.Null(LiteralTextSelection.Create("a" + (char)0xd800, 0, 1));
    }

    [Fact]
    public void ReusableIndexKeepsOriginalGraphemesAcrossManySelectionAndVisibleBoundaryQueries()
    {
        var map = LiteralTextSelectionMap.Create("  cafe\u0301 👩🏽‍💻\r\n")!;
        Assert.Equal("e\u0301", map.Select(6, 7)!.Text);
        Assert.Equal("👩🏽‍💻", map.Select(9, 11)!.Text);
        for (int offset = 0; offset < map.Source.Length; offset++)
        {
            int index = map.ElementAtOrBefore(offset);
            Assert.True(map.Boundary(index) <= offset);
            Assert.True(map.Boundary(index + 1) > offset);
        }
        Assert.Equal(map.Source.Length, map.Boundary(map.ElementCount));
        Assert.Null(map.Select(-1, 0)); Assert.Null(map.Select(0, map.Source.Length + 1));
    }
}
