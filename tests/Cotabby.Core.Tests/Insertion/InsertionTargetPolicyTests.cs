using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;

namespace Cotabby.Core.Tests.Insertion;

public class InsertionTargetPolicyTests
{
    [Fact]
    public void CurrentTargetRequiresSameIdentitySequenceTextAndCaret()
    {
        var expected = InsertionTestFields.Create("hello", caretOffset: 3);

        Assert.True(InsertionTargetPolicy.IsCurrentTarget(expected, expected with { }));
        Assert.False(InsertionTargetPolicy.IsCurrentTarget(expected, expected with { ProcessId = 101 }));
        Assert.False(InsertionTargetPolicy.IsCurrentTarget(expected, expected with { FieldId = "uia:other" }));
        Assert.False(InsertionTargetPolicy.IsCurrentTarget(expected, expected with { FocusChangeSequence = 8 }));
        Assert.False(InsertionTargetPolicy.IsCurrentTarget(expected, expected with { Text = "hello!" }));
        Assert.False(InsertionTargetPolicy.IsCurrentTarget(expected, expected with { CaretOffset = 4 }));
    }

    [Fact]
    public void SameFieldAllowsTextChangesButRequiresSameFocusEpochAndReliableSnapshot()
    {
        var expected = InsertionTestFields.Create("hello", caretOffset: 3);
        var updated = expected with { Text = "hello!", CaretOffset = 6 };

        Assert.True(InsertionTargetPolicy.IsSameField(expected, updated));
        Assert.False(InsertionTargetPolicy.IsSameField(expected, updated with { FocusChangeSequence = 8 }));
        Assert.False(InsertionTargetPolicy.IsSameField(expected, updated with { FieldId = "uia:other" }));
        Assert.False(InsertionTargetPolicy.IsSameField(expected, updated with { HasReliableSelection = false }));
    }

    [Fact]
    public void FieldMustHaveReliableUnselectedNonSecureIdentityAndCaret()
    {
        var field = InsertionTestFields.Create("hello", caretOffset: 2);

        Assert.True(InsertionTargetPolicy.IsEligibleField(field));
        Assert.False(InsertionTargetPolicy.IsEligibleField(null));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { FieldId = string.Empty }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { FieldId = " " }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { IsSecure = true }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { HasSelection = true }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { HasReliableSelection = false }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { IsTextWindowTruncated = true }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { FocusChangeSequence = 0 }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { CaretOffset = -1 }));
        Assert.False(InsertionTargetPolicy.IsEligibleField(field with { CaretOffset = field.Text.Length + 1 }));
    }

    [Fact]
    public void UnknownTextWindowCompletenessFailsClosedUntilProducerProvesBothSidesComplete()
    {
        var unknown = new FocusedField
        {
            ProcessId = 100,
            ProcessName = "editor",
            Text = "hello",
            CaretOffset = 5,
            CaretRect = new ScreenRect(10, 10, 1, 20),
            FieldRect = new ScreenRect(0, 0, 500, 300),
            IsSingleLine = false,
            IsSecure = false,
            FieldId = "uia:field",
            FocusChangeSequence = 1,
            HasReliableSelection = true,
        };

        Assert.True(unknown.IsTextWindowTruncated);
        Assert.False(InsertionTargetPolicy.IsEligibleField(unknown));
        Assert.True(InsertionTargetPolicy.IsEligibleField(unknown with { IsTextWindowTruncated = false }));
    }

    [Fact]
    public void CaretAndReplacementRangeCannotSplitSurrogateOrDeleteNonAscii()
    {
        var emoji = InsertionTestFields.Create("a😀b", caretOffset: 3);

        Assert.False(InsertionTargetPolicy.IsEligibleField(emoji with { CaretOffset = 2 }));
        Assert.False(InsertionTargetPolicy.IsValidReplacement(emoji, deleteUtf16Count: 1, "x"));

        var accented = InsertionTestFields.Create("café", caretOffset: 4);
        Assert.False(InsertionTargetPolicy.IsValidReplacement(accented, deleteUtf16Count: 1, "cafe"));

        var ascii = InsertionTestFields.Create("hello", caretOffset: 5);
        Assert.True(InsertionTargetPolicy.IsValidReplacement(ascii, deleteUtf16Count: 2, "ing"));
        Assert.False(InsertionTargetPolicy.IsValidReplacement(ascii, deleteUtf16Count: 6, "ing"));
        Assert.False(InsertionTargetPolicy.IsValidReplacement(ascii, deleteUtf16Count: 1, ""));
    }

    [Fact]
    public void InsertionOutputRejectsUnpairedSurrogates()
    {
        Assert.False(InsertionTargetPolicy.IsValidInsertionText(new string('\uD800', 1)));
        Assert.False(InsertionTargetPolicy.IsValidInsertionText("bad" + new string('\uDC00', 1)));
    }

    [Theory]
    [InlineData("a\tb")]
    [InlineData("a\rb")]
    [InlineData("\uFFFD")]
    public void InsertionOutputRejectsMalformedUtf16AndUnsafeControls(string output)
    {
        Assert.False(InsertionTargetPolicy.IsValidInsertionText(output));
    }

    [Theory]
    [InlineData("🎉 café")]
    [InlineData("first line\nsecond line")]
    [InlineData(".")]
    public void InsertionOutputAllowsUnicodeAndSafeContinuationText(string output)
    {
        Assert.True(InsertionTargetPolicy.IsValidInsertionText(output));
    }

    [Fact]
    public void EmptyInsertTextIsAnAllowedNoOp()
    {
        Assert.True(InsertionTargetPolicy.IsValidInsertionText(string.Empty));
    }

    [Fact]
    public void InsertionOutputIsBoundedBeforeNativeStaging()
    {
        Assert.False(InsertionTargetPolicy.IsValidInsertionText(new string('x', 64 * 1024 + 1)));
    }
}
