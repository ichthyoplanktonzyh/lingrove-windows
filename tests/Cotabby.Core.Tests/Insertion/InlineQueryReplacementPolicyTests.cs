using Cotabby.Core.Focus;
using Cotabby.Core.Insertion;

namespace Cotabby.Core.Tests.Insertion;

public sealed class InlineQueryReplacementPolicyTests
{
    private static FocusedField Capture(string text = "before | after", int caret = 7) => new()
    {
        ProcessId = 42,
        ProcessName = "test-editor",
        FieldId = "document",
        FocusChangeSequence = 3,
        Composition = InputCompositionState.Clear,
        CompositionSequence = 1,
        HasReliableSelection = true,
        IsTextWindowTruncated = false,
        Text = text,
        CaretOffset = caret,
        CaretRect = ScreenRect.Empty,
        FieldRect = ScreenRect.Empty,
        IsSingleLine = false,
        IsSecure = false,
    };

    private static FocusedField Typed(FocusedField capture, string run) => capture with
    {
        Text = capture.Text.Insert(capture.CaretOffset, run),
        CaretOffset = capture.CaretOffset + run.Length,
    };

    [Theory]
    [InlineData("/5+5")]
    [InlineData(":smile")]
    public void AcceptsOnlyTheExactRunAtTheOriginalCaret(string run)
    {
        var capture = Capture();
        var current = Typed(capture, run);

        Assert.Same(current, InlineQueryReplacementPolicy.Resolve(capture, run, current));
    }

    [Fact]
    public void SupportsUnicodeSurroundingTextWithoutGuessingItsLength()
    {
        var capture = Capture("你好👋 suffix", 4);
        var current = Typed(capture, ":smile");

        Assert.Same(current, InlineQueryReplacementPolicy.Resolve(capture, ":smile", current));
    }

    [Fact]
    public void RejectsDifferentQueryOfTheSameLength()
    {
        var capture = Capture();
        Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, "/5+5", Typed(capture, "/6+6")));
    }

    [Fact]
    public void RejectsMovingToAnotherIdenticalQueryInTheSameField()
    {
        var capture = Capture("/5+5 tail ", 10);
        var current = Typed(capture, "/5+5") with { CaretOffset = 4 };

        Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, "/5+5", current));
    }

    [Fact]
    public void RejectsChangedPrefixOrSuffix()
    {
        var capture = Capture();
        var current = Typed(capture, ":smile");

        Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, ":smile", current with { Text = "changed" + current.Text }));
        Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, ":smile", current with { Text = current.Text + "!" }));
    }

    [Fact]
    public void RejectsLostIdentityOrEligibilityEvenWhenTextMatches()
    {
        var capture = Capture();
        var current = Typed(capture, "/5+5");
        FocusedField?[] invalid =
        [
            null,
            current with { FieldId = "another" },
            current with { ProcessId = 43 },
            current with { FocusChangeSequence = 4 },
            current with { IsSecure = true },
            current with { HasSelection = true },
            current with { HasReliableSelection = false },
        ];

        foreach (var field in invalid)
            Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, "/5+5", field));
    }

    [Theory]
    [InlineData("")]
    [InlineData("smile")]
    [InlineData("?5+5")]
    public void RejectsMissingTrigger(string run)
    {
        var capture = Capture();
        Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, run, Typed(capture, run)));
    }

    [Fact]
    public void RejectsMalformedRun()
    {
        // Construct malformed UTF-16 in the method body; custom-attribute string encoding can
        // replace an unpaired surrogate before a theory ever receives its argument.
        string run = ":" + new string('\uD800', 1);
        var capture = Capture();
        Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, run, Typed(capture, run)));
    }

    [Fact]
    public void RejectsInvalidOriginBeforeTryingToInsertTheRun()
    {
        var capture = Capture() with { CaretOffset = -1 };
        Assert.Null(InlineQueryReplacementPolicy.Resolve(capture, "/5+5", Capture()));
    }
}
