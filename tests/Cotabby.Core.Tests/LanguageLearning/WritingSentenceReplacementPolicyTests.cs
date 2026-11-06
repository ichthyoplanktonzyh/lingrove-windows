using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class WritingSentenceReplacementPolicyTests
{
    private static FocusedField Field(string text, int caret) => new() { ProcessId = 12, ProcessName = "fixture", FieldId = "1.2",
        Text = text, CaretOffset = caret, CaretRect = ScreenRect.Empty, FieldRect = ScreenRect.Empty, IsSingleLine = false,
        IsSecure = false, HasReliableSelection = true, IsTextWindowTruncated = false, FocusChangeSequence = 7,
        Composition = InputCompositionState.Clear, CompositionSequence = 3 };

    [Fact]
    public void ExactCaretEndingAsciiRangeIsAdmittedButMiddleAndUnicodeAreNot()
    {
        var field = Field("before. Hello", 13);
        Assert.True(WritingSentenceReplacementPolicy.CanReplace(field, new(8, 5), "Hello", "Bonjour"));
        Assert.False(WritingSentenceReplacementPolicy.CanReplace(field with { CaretOffset = 10 }, new(8, 5), "Hello", "Bonjour"));
        Assert.False(WritingSentenceReplacementPolicy.CanReplace(Field("café", 4), new(0, 4), "café", "coffee"));
        Assert.False(WritingSentenceReplacementPolicy.CanReplace(field with { Composition = InputCompositionState.Unknown }, new(8, 5), "Hello", "Bonjour"));
        Assert.False(WritingSentenceReplacementPolicy.CanReplace(field, new(8, 5), "Wrong", "Bonjour"));
    }

    [Fact]
    public void AcknowledgementRequiresExactValueAndOriginalFieldReceipt()
    {
        var before = Field("before. Hello", 13); var after = Field("before. Bonjour", 15);
        Assert.True(WritingSentenceReplacementPolicy.IsAcknowledged(before, new(8, 5), "Hello", "Bonjour", after));
        Assert.False(WritingSentenceReplacementPolicy.IsAcknowledged(before, new(8, 5), "Hello", "Bonjour", after with { FocusChangeSequence = 8 }));
        Assert.False(WritingSentenceReplacementPolicy.IsAcknowledged(before, new(8, 5), "Hello", "Bonjour", after with { Text = "before. Bonjour!" }));
        Assert.False(WritingSentenceReplacementPolicy.IsAcknowledged(before, new(8, 5), "Hello", "Bonjour", after with { CompositionSequence = 4 }));
    }
}
