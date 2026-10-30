using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class ReadingVisualReceiptTests
{
    private static ReadingSelection Selection() => new(Guid.NewGuid(), "Hello.", 21, "", 8, null, 6, false, true,
        InputCompositionState.Unknown, 0, new(500, 30, 100, 30))
    {
        Source = ReadingSourceKind.Screenshot,
        ScreenReceipt = new(Guid.NewGuid(), new(-1000, 0, 3000, 1000), new(0, 0, 2000, 1000), new(500, 30, 100, 30))
            { WindowOrigin = new(Guid.NewGuid(), new(0, 0, 200, 200)) },
    };
    [Fact]
    public void ExplicitVisualRegionHasWindowEvidenceWithoutInventedCaretOrComposition()
    {
        var selection = Selection(); ReadingSelectionPolicy.ValidateReceipt(selection);
        Assert.Equal(ReadingRouteReason.TerminalText, ReadingSelectionPolicy.Route(selection, 1).Reason);
        Assert.True(selection.ScreenReceipt!.Region.X > selection.ScreenReceipt.WindowOrigin!.WindowBounds.Right);
    }
    [Fact]
    public void VisualProofCannotAuthorizeAFieldReadOrPretendToHaveClearComposition()
    {
        var selection = Selection();
        foreach (var invalid in new[] { selection with { Source = ReadingSourceKind.Selection }, selection with { Source = ReadingSourceKind.Pointer },
            selection with { FieldId = "fake-field" }, selection with { Composition = InputCompositionState.Clear }, selection with { CompositionSequence = 1 },
            selection with { SelectionStart = 0 }, selection with { SelectionAnchorId = Guid.NewGuid() }, selection with { IsSecure = true } })
            Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(invalid));
    }
    [Fact]
    public void MissingOrMalformedWindowAndDisplayEvidenceIsUnavailable()
    {
        var selection = Selection(); var receipt = selection.ScreenReceipt!;
        foreach (var invalid in new[] { receipt with { WindowOrigin = null }, receipt with { WindowOrigin = receipt.WindowOrigin! with { Id = Guid.Empty } },
            receipt with { WindowOrigin = receipt.WindowOrigin! with { WindowBounds = ScreenRect.Empty } }, receipt with { EvidenceId = Guid.Empty },
            receipt with { Region = new(1900, 30, 200, 30) } })
            Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(selection with { ScreenReceipt = invalid }));
    }
}
