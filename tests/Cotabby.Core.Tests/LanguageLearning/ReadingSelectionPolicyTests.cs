using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class ReadingSelectionPolicyTests
{
    [Fact]
    public void OccurrenceRequiresExactlyOneValidAbsoluteOrOpaqueAnchor()
    {
        var selection = Selection("a😀");
        ReadingSelectionPolicy.ValidateReceipt(selection);
        var opaque = selection with { SelectionStart = null, SelectionAnchorId = Guid.NewGuid() };
        ReadingSelectionPolicy.ValidateReceipt(opaque);
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(opaque with { SelectionAnchorId = null }));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(selection with { SelectionAnchorId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(opaque with { SelectionAnchorId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(selection with { SelectionStart = int.MaxValue }));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.ValidateReceipt(selection with { SelectionLength = 2 }));
    }

    private static ReadingSelection Selection(string text, ReadingContext? context = null) =>
        new(Guid.NewGuid(), text, 1, "field", 1, 0, text.Length, false, true, InputCompositionState.Clear, 1, ScreenRect.Empty, context);

    [Fact]
    public void ExactCompleteSelectionRoutesToTranslationAndPreservesPunctuation()
    {
        var context = new ReadingContext("A whole sentence.", 0, 16, ReadingBoundary.Sentence, ReadingBoundary.Sentence, ReadingContextMethod.Sentence);
        Assert.Equal(ReadingRouteReason.WholeSentence, ReadingSelectionPolicy.Route(Selection("A whole sentence", context), 3).Reason);
        Assert.Equal(ReadingRouteReason.TerminalText, ReadingSelectionPolicy.Route(Selection("Hello world!"), 2).Reason);
        Assert.Equal(ReadingRouteReason.StandaloneUnit, ReadingSelectionPolicy.Route(Selection("bank:"), 1).Reason);
    }

    [Fact]
    public void ContextualWordAndIncompleteSelectionStayLookup()
    {
        var context = new ReadingContext("A small bank.", 8, 4, ReadingBoundary.Sentence, ReadingBoundary.Sentence, ReadingContextMethod.Sentence);
        var route = ReadingSelectionPolicy.Route(Selection("bank", context), 1);
        Assert.False(route.IsTranslation); Assert.Equal(ReadingRouteReason.ContextualUnit, route.Reason); Assert.Equal("bank", route.LookupText);
        var crop = new ReadingContext("some phrase.", 0, 12, ReadingBoundary.Crop, ReadingBoundary.Crop, ReadingContextMethod.Characters);
        Assert.False(ReadingSelectionPolicy.Route(Selection("some phrase.", crop), 2).IsTranslation);
    }

    [Fact]
    public void ResolvedContextRequiresExactUtf16AtTheGivenOccurrence()
    {
        var block = new ReadingContext("Cafe\u0301 noir", 0, 10, ReadingBoundary.LayoutBlock, ReadingBoundary.LayoutBlock, ReadingContextMethod.Block);
        Assert.Equal(ReadingRouteReason.WholeBlock, ReadingSelectionPolicy.Route(Selection("Cafe\u0301 noir", block), 2).Reason);
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.Route(Selection("Café noir", block), 2));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.Route(Selection("other", block), 2));
        var repeated = new ReadingContext("bank bank", 5, 4, ReadingBoundary.Container, ReadingBoundary.Container, ReadingContextMethod.Characters);
        Assert.True(ReadingSelectionPolicy.ValidContext(repeated, "bank"));
        Assert.False(ReadingSelectionPolicy.ValidContext(repeated with { TargetStart = 0, TargetLength = 9 }, "bank"));
        var whitespace = new ReadingContext("a\r\nb", 0, 4, ReadingBoundary.Container, ReadingBoundary.Container, ReadingContextMethod.Characters);
        Assert.True(ReadingSelectionPolicy.ValidContext(whitespace, "a\r\nb"));
        Assert.False(ReadingSelectionPolicy.ValidContext(whitespace, "a\nb"));
        Assert.False(ReadingSelectionPolicy.ValidContext(whitespace, "a  b"));
    }

    [Fact]
    public void TargetIsNeverTruncatedAndWhitespaceOnlyIsRejected()
    {
        ReadingSelectionPolicy.Validate(new string('x', ModelContextBudget.MaximumTextBytes));
        Assert.Throws<LearningTranslationException>(() => ReadingSelectionPolicy.Validate(new string('x', ModelContextBudget.MaximumTextBytes + 1)));
        Assert.Throws<ArgumentException>(() => ReadingSelectionPolicy.Validate("\u200b \n"));
    }
}
